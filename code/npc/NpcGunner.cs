using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox;

enum NpcState
{
	Inactive, // do nothing
	Idle, // stand still, look around
	Patrol, // wander randomly or around a position

	Shock, // aim at target. if target shoots, engage; if target not visible, alarm
	Alarm, // attempting to raise alarm
	Search, // approach last known position of target
	Engage, // keep medium range
	Push, // close up range
}
enum NpcEvent
{
	Enable, // inactive -> idle
	Disable, // any -> inactive

	ResumePatrol, // idle -> patrol
	PausePatrol, // patrol -> idle

	EnterShock, // idle/patrol -> shock
	SoundAlarm, // idle/patrol/shock -> alarm

	GetTarget, // idle/patrol/shock/alarm -> engage
	PushTarget, // engage/search -> push
	SeekTarget, // push/patrol/search -> search
	Disengage, // engage/push/search -> patrol
}
class NpcStateTransition
{
	readonly NpcState CurrentState;
	readonly NpcEvent Command;

	public NpcStateTransition( NpcState currentState, NpcEvent command )
	{
		CurrentState = currentState;
		Command = command;
	}

	public override int GetHashCode()
	{
		return 17 + 31 * CurrentState.GetHashCode() + 31 * Command.GetHashCode();
	}

	public override bool Equals( object obj )
	{
		NpcStateTransition other = obj as NpcStateTransition;
		return other != null && this.CurrentState == other.CurrentState && this.Command == other.Command;
	}
}

public struct NpcTargetInfo
{
	public NpcTargetInfo( Entity target )
	{
		Target = target;
		LastPosition = Target.Position;
		Enmity = 1;

		TimeSinceVisible = 0;
		TimeSinceReappear = 0;
	}

	public readonly Entity Target;
	public Vector3 LastPosition;

	// Total amount of damage dealt to us
	public float Enmity;

	public TimeSince TimeSinceVisible;
	public TimeSince TimeSinceReappear;

	public bool IsValid()
	{
		return Target != null && Target.IsValid();
	}

	public override string ToString()
	{
		return "(" + (IsValid() ? Target.ToString() : "INVALID") + ", " + Enmity + "enm, " + Math.Round(TimeSinceVisible, 1) + "tsv)";
	}
}
public struct NpcParams
{
	public float PatrolSpeed;
	public float CombatSpeed;

	// The rate at which we will fire semi-automatic weapons.
	public float WeaponSemiRate;

	// The distance within which we try to back off
	public float MinCombatDistance;
	// The distance beyond which we will close in if pushing
	public float PushCombatDistance;
	// The distance beyond which we don't try to shoot (not effective)
	public float MaxCombatDistance;
}

class NpcGunner : NpcPawn
{

	[ConVar.Replicated]
	public static bool npc_gunner_vision { get; set; } = true;

	[ConVar.Replicated]
	public static bool npc_gunner_nopatrol { get; set; } = false;

	[ServerCmd( "npc_gunner_dm" )] // sets them up to kill each other
	public static void NpcBattleRoyale()
	{
		var arr = All.OfType<NpcGunner>().ToArray();
		if ( arr.Length <= 1 ) return;

		foreach ( var npc in arr )
		{
			//var tgt = random.Next(0, arr.Length);
			//if ( arr[tgt] == npc ) tgt = (tgt + 1) % arr.Length;
			//npc.FireEvent( NpcEvent.SeekTarget, arr[tgt] );
			foreach ( var npc2 in arr )
			{
				if (npc != npc2)
				{
					npc.TargetList.Add( npc2, new NpcTargetInfo( npc2 ) );
				}
			}
		}
	}

	// --------------------------------------------------
	// Variables
	// --------------------------------------------------

	public BaseHeistWeapon Weapon { get; protected set; }

	// The rate at which we will fire semi-automatic weapons. There is a random additional delay to make it seem realistic.
	protected float WeaponSemiRate = 3f;
	protected float WeaponSemiVariance = 0.5f;

	protected float PatrolSpeed = 40f;
	protected float CombatSpeed = 100f;

	// The distance within which we try to back off
	protected float MinCombatDistance = 80f;

	// The distance beyond which we will close in if pushing
	protected float PushCombatDistance = 300f;

	// The distance beyond which we don't try to shoot (not effective)
	protected float MaxCombatDistance = 750f;

	TimeSince TimeSincePathThink;
	TimeSince TimeSinceVisCheck;
	TimeSince TimeSinceScan;
	TimeSince TimeSinceAttack;
	TimeSince TimeSinceState;
	bool LastVisCheck;

	public Sandbox.Nav.Patrol Patrol;

	// Builds up to 1 when seeing a target calm before attacking
	protected float Suspicion = 0f;
	bool Cool = true;

	// Builds up to 1 when shot or shooting
	protected float Suppression = 0f;

	public NpcTargetInfo Target;
	public Dictionary<Entity, NpcTargetInfo> TargetList;
	bool HasTarget()
	{
		return Target.IsValid();
	}
	NpcPoint ClaimedPoint;

	// Alias for setting info values of the current target.
	public Vector3 LastTargetPosition { 
		get
		{
			if ( Target.IsValid() )
				return Target.LastPosition;
			return Vector3.Zero;
		}
		protected set
		{
			if ( Target.IsValid() )
				Target.LastPosition = value;
		}
	}
	public float TimeSinceTargetVisible
	{
		get
		{
			if ( Target.IsValid() )
				return Target.TimeSinceVisible;
			return int.MaxValue;
		}
		protected set
		{
			if ( Target.IsValid() )
				Target.TimeSinceVisible = value;
		}
	}
	public float TimeSinceTargetReappear
	{
		get
		{
			if ( Target.IsValid() )
				return Target.TimeSinceReappear;
			return 0f;
		}
		protected set
		{
			if ( Target.IsValid() )
				Target.TimeSinceReappear = value;
		}
	}

	// --------------------------------------------------
	// State Machine Stuff
	// --------------------------------------------------

	Dictionary<NpcStateTransition, NpcState> transitions;
	Dictionary<NpcEvent, Action<Entity>> eventFunc;

	protected virtual void InitializeStates()
	{
		transitions = new Dictionary<NpcStateTransition, NpcState>
		{
			{ new NpcStateTransition(NpcState.Inactive, NpcEvent.Enable), NpcState.Idle },
			//{ new NpcStateTransition(NpcState.Idle, NpcEvent.Disable), NpcState.Inactive },

			{ new NpcStateTransition(NpcState.Idle, NpcEvent.ResumePatrol), NpcState.Patrol },
			{ new NpcStateTransition(NpcState.Patrol, NpcEvent.PausePatrol), NpcState.Idle },

			{ new NpcStateTransition(NpcState.Idle, NpcEvent.EnterShock), NpcState.Shock },
			{ new NpcStateTransition(NpcState.Patrol, NpcEvent.EnterShock), NpcState.Shock },

			{ new NpcStateTransition(NpcState.Idle, NpcEvent.SoundAlarm), NpcState.Alarm },
			{ new NpcStateTransition(NpcState.Patrol, NpcEvent.SoundAlarm), NpcState.Alarm },
			{ new NpcStateTransition(NpcState.Shock, NpcEvent.SoundAlarm), NpcState.Alarm },

			{ new NpcStateTransition(NpcState.Idle, NpcEvent.GetTarget), NpcState.Engage },
			{ new NpcStateTransition(NpcState.Patrol, NpcEvent.GetTarget), NpcState.Engage },
			{ new NpcStateTransition(NpcState.Search, NpcEvent.GetTarget), NpcState.Engage },
			{ new NpcStateTransition(NpcState.Shock, NpcEvent.GetTarget), NpcState.Engage },
			{ new NpcStateTransition(NpcState.Alarm, NpcEvent.GetTarget), NpcState.Engage },

			{ new NpcStateTransition(NpcState.Engage, NpcEvent.PushTarget), NpcState.Push },
			{ new NpcStateTransition(NpcState.Search, NpcEvent.PushTarget), NpcState.Push },

			{ new NpcStateTransition(NpcState.Idle, NpcEvent.SeekTarget), NpcState.Search },
			{ new NpcStateTransition(NpcState.Search, NpcEvent.SeekTarget), NpcState.Search }, // a new unknown target might override our current one
			{ new NpcStateTransition(NpcState.Patrol, NpcEvent.SeekTarget), NpcState.Search },
			{ new NpcStateTransition(NpcState.Engage, NpcEvent.SeekTarget), NpcState.Search },
			{ new NpcStateTransition(NpcState.Push, NpcEvent.SeekTarget), NpcState.Search },

			{ new NpcStateTransition(NpcState.Engage, NpcEvent.Disengage), NpcState.Patrol },
			{ new NpcStateTransition(NpcState.Push, NpcEvent.Disengage), NpcState.Patrol },
			{ new NpcStateTransition(NpcState.Search, NpcEvent.Disengage), NpcState.Patrol },
		};

		eventFunc = new Dictionary<NpcEvent, Action<Entity>>
		{
			{ NpcEvent.Enable, obj => SteerUpdate() },
			// Disable is handled specially
			{ NpcEvent.ResumePatrol, obj => SteerUpdate() },
			{ NpcEvent.PausePatrol, obj => SteerUpdate() },
			{ NpcEvent.GetTarget, obj => { SetTarget(obj); SteerUpdate(); } },
			{ NpcEvent.PushTarget, obj => { SetTarget(obj); SteerUpdate(); } },
			{ NpcEvent.SeekTarget, obj => { SetUnknownTarget(obj); SteerUpdate(); } },
			{ NpcEvent.Disengage, obj => { ForgetTarget(); SteerUpdate(); } },

		};
	}

	public NpcState CurrentState { get; protected set; }
	public virtual NpcState GetNext( NpcEvent command )
	{
		NpcStateTransition transition = new NpcStateTransition( CurrentState, command );
		NpcState nextState;

		if ( !transitions.TryGetValue( transition, out nextState ) )
		{
			// throw new Exception( "Invalid transition: " + CurrentState + " -> " + command );
			Log.Warning( ToString() + ": Invalid transition (" + CurrentState + " -> " + command + ")" );
			return CurrentState;
		}
		return nextState;
	}
	public virtual NpcState FireEvent( NpcEvent command, Entity obj )
	{
		TimeSinceState = 0;
		// Handle disable uniquely, since any state can go into it
		if ( command == NpcEvent.Disable )
		{
			CurrentState = NpcState.Inactive;
			Target = default;
			Steer = null;
			return CurrentState;
		}

		var newState = GetNext( command );
		if (newState != CurrentState) {
			CurrentState = newState;
			eventFunc[command]?.Invoke( obj );
		}
		// Log.Info( ToString() + ": FireEvent(" + command.ToString() + "): state = " + CurrentState );
		return CurrentState;
	}

	protected virtual void SteerUpdate()
	{
		TimeSincePathThink = 0;

		if ( CurrentState == NpcState.Search )
		{
			// We lost track of the target
			Speed = (CombatSpeed + PatrolSpeed) / 2;
			if ( LastTargetPosition != Vector3.Zero )
			{
				var tr = Trace.Ray( EyePos, LastTargetPosition )
					.Ignore( Owner )
					.Ignore( this )
					.Run();
				if ( tr.Fraction >= 0.95 )
					LastTargetPosition = Vector3.Zero;
			}

			Vector3? pos;

			if ( LastTargetPosition == Vector3.Zero )
			{
				// We can see the last known position. Let's look around instead
				pos = NavMesh.GetPointWithinRadius( Position, 200f, 400f );
			}
			else if ( TimeSinceTargetVisible < 0 )
			{
				// We need to investigate the last known position directly and now
				pos = LastTargetPosition;
			}
			else
			{
				// Approach last known position
				pos = NavMesh.GetPointWithinRadius( LastTargetPosition, 100f, 200f );
			}

			Steer = new NavSteer();
			if ( !pos.HasValue )
				Steer.Target = LastTargetPosition;
			else
				Steer.Target = (Vector3)pos;

		}
		else if( HasTarget() )
		{
			var ent = Target.Target;
			// If we've seen our target recently, we know where they are;
			// otherwise we can only proceed with last known pos
			var tgtpos = TimeSinceTargetVisible < 2f ? ent.Position : LastTargetPosition;
			var dist = (tgtpos - Position).Length;
			var closer = Position + (tgtpos - Position) * 0.5f;
			if ( CurrentState == NpcState.Engage )
			{
				// Attempt to find the best cover point to use
				NpcPoint best_point = null;
				var best_idealness = 0;
				foreach (var p in NpcPoint.All)
				{
					var pdist = p.GetPosition().Distance( Position );
					if ((!p.IsClaimed() || p.GetClaimant() == this) && pdist <= 1000f)
					{
						var idealness = 0;
						var dist2 = (tgtpos - p.GetPosition()).Length;

						// LoS to target (up to 25 if chest-high cover)
						var vis = p.VisDistTo( tgtpos );
						if ( vis[1] > dist2 && vis[0] > dist2 ) continue;
						if ( vis[0] < dist2 ) idealness += (int)Math.Round( Math.Clamp( 25 - vis[0] / dist2 * 25, 0, 25 ) );

						// Proximity to our location (up to +10)
						idealness += (int)Math.Round( Math.Clamp( 10 - pdist / 50, 0, 10 ) );

						// TODO: Cover from secondary threat (?)

						// Inside ideal range
						if ( dist2 < PushCombatDistance && dist2 > MinCombatDistance )
						{
							idealness += 10;
						}


						if ( best_idealness < idealness )
						{
							best_idealness = idealness;
							best_point = p;
						}
					}
				}

				if ( best_idealness > 0 && best_point != null )
				{
					bool ok = best_point.Claim( this );
					if (ok)
					{
						Speed = CombatSpeed;
						if ( ClaimedPoint != null && ClaimedPoint.IsClaimed() )
							ClaimedPoint.Unclaim( this );
						ClaimedPoint = best_point;
						Sandbox.Nav.WanderPoint wander = new Sandbox.Nav.WanderPoint();
						wander.MinRadius = 10f;
						wander.MaxRadius = 50f;
						wander.Position = best_point.GetPosition();
						Steer = wander;
						//Steer = new NavSteer();
						//Steer.Target = best_point.GetPosition();
					}
				}
				else if ( dist > MaxCombatDistance )
				{
					Speed = CombatSpeed;
					// Try to find a position in between the target and ourselves
					var distmult = Math.Clamp( dist / 400, 1, 3 );
					var pos = NavMesh.GetPointWithinRadius( closer, 100f * distmult, 200f * distmult );
					Steer = new NavSteer();
					if ( !pos.HasValue )
						Steer.Target = tgtpos;
					else
						Steer.Target = (Vector3)pos;
				}
				else
				{
					Speed = CombatSpeed;
					// Wander around
					Sandbox.Nav.Wander wander = new Sandbox.Nav.Wander();
					wander.MinRadius = 40f;
					wander.MaxRadius = 100f;
					Steer = wander;
				}
			}
			else if ( CurrentState == NpcState.Push )
			{
				// We are pushing. Let's get in some trouble and die for the player's amusement!
				Speed = CombatSpeed;

				Vector3? pos;
				if ( dist > PushCombatDistance )
				{
					// If we are not close, try to find a position in between the target and ourselves
					var distmult = Math.Clamp( dist / 250, 1, 3 );
					pos = NavMesh.GetPointWithinRadius( closer, 50f * distmult, 80f * distmult );
				}
				else
				{
					// We are close enough. Let's do some shadow dancing
					pos = NavMesh.GetPointWithinRadius( tgtpos, MinCombatDistance, Math.Max( MinCombatDistance * 1.5f, PushCombatDistance * 0.75f ) );
				}

				Steer = new NavSteer();
				if (!pos.HasValue)
					Steer.Target = tgtpos;
				else
					Steer.Target = (Vector3)pos;

			}
		} else
		{
			Speed = PatrolSpeed;
			Steer = null; // this will also stop the NPC when in idle or disabled state
			if ( CurrentState == NpcState.Patrol )
			{
				if ( Patrol != null)
				{
					Steer = Patrol;
				} else
				{
					Steer = new Sandbox.Nav.Wander(); // patrol normally
				}
			}
				
		}
	}

	protected virtual void StateUpdate()
	{
		var ent = HasTarget() ? Target.Target : default;
		if ( CurrentState == NpcState.Push )
		{
			if ( !HasTarget() || ent.Health <= 0 )
				FireEvent( NpcEvent.Disengage, ent );
			else if ( !HasTarget() || TimeSinceTargetVisible >= 5f )
			{
				// Lost target, hunt them down
				FireEvent( NpcEvent.SeekTarget, ent );
				return;
			}
		} else if ( CurrentState == NpcState.Engage )
		{
			if ( !HasTarget() || ent.Health <= 0 )
				FireEvent( NpcEvent.Disengage, ent );
			else if ( !HasTarget() || TimeSinceTargetVisible >= 10f )
			{
				FireEvent( NpcEvent.SeekTarget, ent );
				return;
			} else if ( TimeSinceTargetVisible <= 2f && ent.Health <= Health * 0.4 )
			{
				// They're weak, let's push them!
				FireEvent( NpcEvent.PushTarget, ent );
			}
		}
		else if ( CurrentState == NpcState.Search )
		{
			if ( TimeSinceState >= 30f )
			{
				// Give up hunting
				FireEvent( NpcEvent.Disengage, ent );
				return;
			} else if ( TimeSinceTargetVisible <= 0 )
			{
				if ( !HasTarget() || ent.Health <= 0 )
					FireEvent( NpcEvent.Disengage, ent );
				else if ( ent.Health <= Health * 0.75 )
					FireEvent( NpcEvent.PushTarget, ent );
				else
					FireEvent( NpcEvent.GetTarget, ent );
			}
		}
		else if ( CurrentState == NpcState.Idle )
		{
			if ( HasTarget() )
            {
				FireEvent( NpcEvent.GetTarget, Target.Target );
			}
			else if ( !npc_gunner_nopatrol )
			{
				// Let's start patrolling!
				FireEvent( NpcEvent.ResumePatrol, null );
				return;
			}
		}
		else if ( CurrentState == NpcState.Patrol )
		{
			if ( HasTarget() )
			{
				FireEvent( NpcEvent.GetTarget, Target.Target );
			}
		}
		DebugOverlay.Text( Position + Vector3.Up * 72f, CurrentState.ToString() );
		// DebugOverlay.ScreenText( ToString() + " " + CurrentState.ToString() + " " + Target.ToString() );
	}

	// --------------------------------------------------
	// Targetting
	// --------------------------------------------------

	protected virtual float CalculateEnmity( NpcTargetInfo info )
	{
		// TODO: target prioritization with enmity
		return info.Enmity;
	}
	protected virtual void EvaluateTargets()
	{
		KeyValuePair<Entity, NpcTargetInfo> target = default;
		var targetEnmity = -1f;

		foreach ( KeyValuePair<Entity, NpcTargetInfo> pair in TargetList )
		{

			if ( !pair.Key.IsValid() || pair.Key.Health <= 0 )
			{
				TargetList.Remove( pair.Key );
				continue;
			}

			var ourEnmity = CalculateEnmity( pair.Value );

			if ( ourEnmity >= targetEnmity )
			{
				target = pair;
				targetEnmity = ourEnmity;
			}
		}

		if ( targetEnmity >= 0 && target.Key != Target.Target )
		{
			var info = target.Value;
			Target = info;
			TimeSinceTargetReappear = -1f; // Delay when switching targets
		}
	}
	protected virtual void ForgetTarget()
	{
		if ( !HasTarget() )
			return;

		var ent = Target.Target;
		if ( ent.IsValid() && TargetList.ContainsKey( ent ) )
		{
			TargetList.Remove( ent );
			if ( Target.Target == ent )
				Target = default;
		}
		if ( TargetList.Count == 0 ) Suspicion = 0;
	}
	protected virtual void SetTarget( Entity ent )
	{
		if (!TargetList.ContainsKey(ent))
		{
			var info = new NpcTargetInfo( ent );
			TargetList.Add( ent, info );
			Target = info;
		} else
		{
			Target = TargetList[ent];
		}
		Cool = false;
	}
	protected virtual void SetUnknownTarget( Entity ent )
	{
		if ( ent != null && ent.IsValid() && (!HasTarget() || Target.Target != ent) && !TargetList.ContainsKey( ent ) )
		{
			var info = new NpcTargetInfo( ent );
			info.TimeSinceVisible = 1f;
			info.TimeSinceReappear = -1f;
			info.LastPosition = ent.Position + Vector3.Random.WithZ( 0 ) * 128f;
			TargetList.Add( ent, info );
		}
	}
	protected virtual bool CheckVisibility( Entity ent, bool reducedVision = false )
	{
		if ( ent == null || !ent.IsValid() || ent.Health <= 0 )
			return false;

		var dist = (ent.Position - Position).Length;
		var angdiff = EyeRot.Distance( Rotation.LookAt( ent.EyePos - EyePos, Vector3.Up ) );
		if ( dist > (reducedVision ? 64f : 96f) && angdiff >= (reducedVision ? 75f : 100f) )
			return false;

		var tr = Trace.Ray( EyePos, ent.EyePos )
			.Ignore( Owner )
			.Ignore( this )
			.Run();

		if ( tr.Entity != ent )
			return false;

		return true;
	}
	protected virtual bool CheckTargetVisiblity()
	{
		if ( Cool && Suspicion < 1f ) return false;

		// TODO: maybe this can trigger less often for performance?
		if ( TimeSinceVisCheck == 0 ) return LastVisCheck;
		LastVisCheck = false;
		var lastTarget = Target;
		var bestVisible = Target;
		var bestEnmity = HasTarget() ? CalculateEnmity( Target ) : -1;
		var bestTimeSince = 0f;

		if ( npc_gunner_vision )
		{
			foreach ( KeyValuePair<Entity, NpcTargetInfo> pair in TargetList )
			{
				var ent = pair.Key;
				var info = pair.Value;
				var visible = CheckVisibility( ent, info.TimeSinceVisible >= 2f );
				if ( visible )
				{

					if ( ent == Target.Target )
						LastVisCheck = true;

					var enmity = CalculateEnmity( info );
					if ( bestVisible.IsValid() || enmity > bestEnmity * 2f )
					{
						bestTimeSince = info.TimeSinceVisible;
						bestVisible = info;
						bestVisible.TimeSinceVisible = 0;
						bestEnmity = enmity;
						LastVisCheck = true;
					}
					info.TimeSinceVisible = 0;
				}
			}
		}

		Target = bestVisible;
		if ( lastTarget.Target != bestVisible.Target )
			Target.TimeSinceReappear = -1 * Math.Clamp( (bestTimeSince - 1) / 3f, 0, 1f );

		// This caches the result for one tick (hopefully)
		TimeSinceVisCheck = 0;
		return LastVisCheck;
	}
	protected virtual void ScanForTargets()
	{
		if ( !npc_gunner_vision ) return;
		TimeSinceScan = 0;
		var acquire = false;
		Suspicion = Math.Max(Suspicion - 0.15f, 0);
		foreach ( Entity ent in Physics.GetEntitiesInSphere( Position, 2000f ) )
		{
			if ( ent is Player && CheckVisibility( ent, true ) )
			{
				if ( !Cool && TargetList.ContainsKey( ent ) )
				{
					var info = TargetList[ent];
					info.TimeSinceVisible = 0;
					info.TimeSinceReappear = 0;
				}
				else if (Cool && Suspicion < 1)
				{
					Suspicion += 0.5f
						* ((1000f - ent.Position.Distance( Position )) / 1000f).Clamp(0.25f, 1)
						* (1 - Math.Abs( EyeRot.Distance( Rotation.LookAt( ent.EyePos - EyePos, Vector3.Up ) )) / 75f).Clamp(0.25f, 1)
						* (((PlayerController)(((Player)ent).Controller)).IsCrouching ? 0.5f : 1f);
				}
				else
				{
					var info = new NpcTargetInfo( ent );
					info.TimeSinceVisible = 0;
					info.TimeSinceReappear = -1f; // first time getting you, let's be lenient
					TargetList.Add( ent, info );
					if ( !acquire && (CurrentState == NpcState.Idle || CurrentState == NpcState.Patrol || CurrentState == NpcState.Search) )
					{
						// if we found one target, go into engage mode
						FireEvent( NpcEvent.GetTarget, ent );
						acquire = true;
					}
				}
			}
		}
		if (Cool && Suspicion > 0) DebugOverlay.Text( Position + Vector3.Up * 32f, (Suspicion * 100f).CeilToInt().ToString(), 0.25f );
	}

	// --------------------------------------------------
	// AI Logic
	// --------------------------------------------------

	public override void Spawn()
	{
		base.Spawn();

		InitializeStates();

		TimeSinceVisCheck = 0;
		TimeSincePathThink = 0;

		Target = default;
		TargetList = new Dictionary<Entity, NpcTargetInfo> { };
		CurrentState = NpcState.Idle;

		// TODO: better way to set NPC weapons
		Weapon = new Pistol();
		Weapon.OnCarryStart( this );
		Weapon.ActiveStart( this );

		Speed = PatrolSpeed;
	}

	protected override void NpcThink()
	{
		var visible = CheckTargetVisiblity();
		if (TimeSinceScan >= 0.2f) ScanForTargets();

		StateUpdate();

		var ent = Target.Target;

		if ( TimeSincePathThink > 3f )
		{
			SteerUpdate();
		}

		// Shoot the target!
		if ( HasTarget() && visible )
		{
			SetAnimInt( "holdtype", Weapon.HoldType );

			if ( Weapon != null ) // we can fire right as they disappear for a second
			{
				var angdiff = Rotation.Distance( Rotation.LookAt( ent.Position - Position, Vector3.Up ) );
				if (angdiff <= 15f && TimeSinceTargetReappear >= 0.5f && TimeSinceTargetVisible <= 1f &&
						Weapon.CanPrimaryAttack() && ( Weapon.Automatic || TimeSinceAttack > ( 1 / WeaponSemiRate ) ) )
				{
					Weapon.AttackPrimary();
					// DebugOverlay.Line( EyePos, EyePos + EyeRot.Forward * 1000, 1 );
					TimeSinceAttack = (Rand.Float() - 0.5f) * (WeaponSemiVariance / WeaponSemiRate);
				}
				else if ( Weapon.CanReload() && Weapon.AmmoClip == 0 )
				{
					Weapon.Reload();
				}
			}
		}
		else if ( !HasTarget() )
		{
			if ( Weapon.CanReload() && Weapon.AmmoClip < Weapon.ClipSize )
			{
				Weapon.Reload(); // never hurts to top it off
			}
			SetAnimInt( "holdtype", Weapon.IsReloading ? Weapon.HoldType : 0 );
		}
	}

	protected override void NpcTurn()
	{
		if ( LastTargetPosition != Vector3.Zero )
		{
			Vector3 vec;
			if ( HasTarget() && TimeSinceTargetReappear >= 0 )
			{
				vec = Target.Target.Position - Position;
				LookDir = Target.Target.EyePos;
				// var dist = vec.Length; // Overcompensate for target velocity
				// vec += Target.Velocity * 0.5f * Math.Clamp( 1 - dist / 800, 0, 1 );
			}
			else
			{
				vec = LastTargetPosition - Position;
				LookDir = LastTargetPosition + (EyePos - Position);
			}

			var TargetRotation = Rotation.LookAt( vec.WithZ( 0 ), Vector3.Up );
			var eyeRotSpeed = 15f;
			var lastRot = Rotation;
			Rotation = Rotation.Lerp( Rotation, TargetRotation, Time.Delta * 5f );
			EyeRot += (Rotation - lastRot); // when our body rotates, so do our eyes

			var tgt = Rotation.LookAt( vec, Vector3.Up );
			if (Target.TimeSinceVisible >= 3f)
			{
				// Add some head movement since we're looking around for the target
				tgt += Rotation.FromYaw( Rotation.Yaw() + MathF.Sin( Time.Now * 2f ) * 75f );
			}
			else
			{
				// Add sway to imitate inperfect aim, otherwise we're just an aimbot!
				tgt.x += MathF.Sin( Time.Now / 2f ) * Time.Delta * 0.8f;
				tgt.y += MathF.Cos( Time.Now / 2f ) * Time.Delta * 0.4f;

				// Slow down when aiming towards far away targets. this gives the player some opportunity to dodge
				eyeRotSpeed *= (1 - 0.5f * Math.Clamp( vec.Length / 1000f, 0f, 1f ));
			}

			EyeRot = Rotation.Lerp( EyeRot, tgt, Time.Delta * eyeRotSpeed );
		}
		else
		{
			var walkVelocity = Velocity.WithZ( 0 );
			var turnSpeed = walkVelocity.Length.LerpInverse( 0, 100, true );
			var targetRotation = Rotation.LookAt( walkVelocity.Normal, Vector3.Up );
			var lastRot = Rotation;
			if ( walkVelocity.Length > 0.5f )
			{
				if ( CurrentState == NpcState.Search )
					Rotation = Rotation.Lerp( Rotation, Rotation.FromYaw( targetRotation.Yaw() + MathF.Cos( Time.Now / 2f ) * 45f ), Time.Delta * 5.0f );
				else
					Rotation = Rotation.Lerp( Rotation, targetRotation, turnSpeed * Time.Delta * 20.0f );
				EyeRot += (Rotation - lastRot); // when our body rotates, so do our eyes
			}
			var tgt = Rotation.FromYaw( Rotation.Yaw() + MathF.Sin( Time.Now / 2f ) * 60f ); // look around slowly
			EyeRot = Rotation.Lerp( EyeRot, tgt, Time.Delta * 10f );
		}
	}

	protected override void NpcAnim()
	{
		SetAnimLookAt( "aim_eyes", EyePos + LookDir * 200 );
		SetAnimLookAt( "aim_head", EyePos + EyeRot.Forward * 200 );
		SetAnimLookAt( "aim_body", Position + Rotation.Forward * 200 );
		SetAnimFloat( "aim_body_weight", 1f );

		SetAnimBool( "b_grounded", GroundEntity != null );
		SetAnimBool( "b_noclip", false );
		SetAnimBool( "b_swim", WaterLevel.Fraction > 0.5f );

		SetAnimFloat( "wishspeed", Velocity.Length * 1.5f );
		SetAnimFloat( "walkspeed_scale", 1.0f / 10.0f * (Speed / 100) );
		SetAnimFloat( "runspeed_scale", 1.0f / 320.0f );
		SetAnimFloat( "duckspeed_scale", 1.0f / 80.0f );

		// Move Speed
		{
			var dir = Velocity;
			var forward = Rotation.Forward.Dot( dir );
			var sideward = Rotation.Right.Dot( dir );

			var angle = MathF.Atan2( sideward, forward ).RadianToDegree().NormalizeDegrees();

			SetAnimFloat( "move_direction", angle );
			SetAnimFloat( "move_speed", Velocity.Length );
			SetAnimFloat( "move_groundspeed", Velocity.WithZ( 0 ).Length );
			SetAnimFloat( "move_y", sideward );
			SetAnimFloat( "move_x", forward );
		}

		// Wish Speed
		{
			var dir = Velocity;
			var forward = Rotation.Forward.Dot( dir );
			var sideward = Rotation.Right.Dot( dir );

			var angle = MathF.Atan2( sideward, forward ).RadianToDegree().NormalizeDegrees();

			SetAnimFloat( "wish_direction", angle );
			SetAnimFloat( "wish_speed", Velocity.Length );
			SetAnimFloat( "wish_groundspeed", Velocity.WithZ( 0 ).Length );
			SetAnimFloat( "wish_y", sideward );
			SetAnimFloat( "wish_x", forward );
		}
	}

	public override void TakeDamage( DamageInfo info )
	{

		if ( CurrentState == NpcState.Idle || CurrentState == NpcState.Patrol )
		{
			// sneak attack!
			info.Damage = info.Damage * 3;
		}

		base.TakeDamage( info );
		var attacker = info.Attacker;

		if ( attacker == null || attacker == this ) return;

		if ( TargetList.ContainsKey(attacker) )
		{
			var targetInfo = TargetList[attacker];
			targetInfo.Enmity += info.Damage;
		}
		if (CurrentState == NpcState.Idle || CurrentState == NpcState.Patrol || CurrentState == NpcState.Search)
		{
			FireEvent( NpcEvent.SeekTarget, attacker );
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox;

enum NpcState // TODO: more specific name for combat NPC enum?
{
	Inactive, // do nothing
	Idle, // stand still, look around
	Patrol, // wander randomly or around a position

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

	GetTarget, // idle/patrol -> engage
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

class NpcGunner : NpcPawn
{

	[ConVar.Replicated]
	public static bool npc_gunner_vision { get; set; } = true;

	[ConVar.Replicated]
	public static bool npc_gunner_nopatrol { get; set; } = false;

	[ServerCmd( "npc_gunner_dm" )] // sets them up to kill each other
	public static void NpcBattleRoyale()
	{
		var arr = Entity.All.OfType<NpcGunner>().ToArray();
		if ( arr.Length <= 1 ) return;

		Random random = new Random();

		foreach ( var npc in arr )
		{
			var tgt = random.Next(0, arr.Length);
			if ( arr[tgt] == npc ) tgt = (tgt + 1) % arr.Length;
			npc.FireEvent( NpcEvent.SeekTarget, arr[tgt] );
		}
	}

	[ServerCmd( "npc_gunner_seekplayer" )] // sets them up to hunt the player
	public static void NpcSeekPlayer()
	{
		var plys = All.OfType<Player>().ToArray();
		Random random = new Random();
		foreach ( var npc in All.OfType<NpcGunner>().ToArray() )
		{
			npc.FireEvent( NpcEvent.SeekTarget, plys[random.Next( 0, plys.Length )] );
		}
	}

	// --------------------------------------------------
	// Variables
	// --------------------------------------------------

	public BaseHeistWeapon Weapon { get; protected set; }

	// The rate at which we will fire semi-automatic weapons.
	protected float WeaponSemiRate = 4f;

	protected float PatrolSpeed = 80f;
	protected float CombatSpeed = 150f;

	// The distance within which we try to back off
	protected float MinCombatDistance = 100f;

	// The distance beyond which we will close in if pushing
	protected float PushCombatDistance = 500f;

	// The distance beyond which we don't try to shoot (not effective)
	protected float MaxCombatDistance = 1000f;

	TimeSince TimeSincePathThink;
	TimeSince TimeSinceVisCheck;
	TimeSince TimeSinceScan;
	bool LastVisCheck;

	public NpcTargetInfo Target;
	public Dictionary<Entity, NpcTargetInfo> TargetList;
	bool HasTarget()
	{
		return Target.IsValid();
	}

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
	public TimeSince TimeSinceTargetVisible
	{
		get
		{
			if ( Target.IsValid() )
				return Target.TimeSinceVisible;
			return 0f;
		}
		protected set
		{
			if ( Target.IsValid() )
				Target.TimeSinceVisible = value;
		}
	}
	public TimeSince TimeSinceTargetReappear
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

			{ new NpcStateTransition(NpcState.Idle, NpcEvent.GetTarget), NpcState.Engage },
			{ new NpcStateTransition(NpcState.Patrol, NpcEvent.GetTarget), NpcState.Engage },

			{ new NpcStateTransition(NpcState.Engage, NpcEvent.PushTarget), NpcState.Push },
			{ new NpcStateTransition(NpcState.Search, NpcEvent.PushTarget), NpcState.Push },

			{ new NpcStateTransition(NpcState.Idle, NpcEvent.SeekTarget), NpcState.Search },
			{ new NpcStateTransition(NpcState.Search, NpcEvent.SeekTarget), NpcState.Search }, // a new unknown target might override our current one
			{ new NpcStateTransition(NpcState.Patrol, NpcEvent.SeekTarget), NpcState.Search },
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
			Log.Warning( "Invalid transition: " + CurrentState + " -> " + command );
			return CurrentState;
		}
		return nextState;
	}
	public virtual NpcState FireEvent( NpcEvent command, Entity obj )
	{
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
		return CurrentState;
	}

	protected virtual void SteerUpdate()
	{
		TimeSincePathThink = 0;
		if ( HasTarget() )
		{
			var ent = Target.Target;
			// If we've seen our target recently, we know where they are;
			// otherwise we can only proceed with last known pos
			var tgtpos = TimeSinceTargetVisible < 2f ? ent.Position : LastTargetPosition;
			var dist = (tgtpos - Position).Length;
			var closer = Position + (tgtpos - Position) * 0.5f;
			if ( CurrentState == NpcState.Engage )
			{
				// We are in engage mode. Speed up and wander nearby (like strafing)
				Speed = CombatSpeed;

				if ( dist > MaxCombatDistance )
				{
					// If we are not close, try to find a position in between the target and ourselves
					var distmult = Math.Clamp( dist / 400, 1, 3 );
					var pos = NavMesh.GetPointWithinRadius( closer, 100f * distmult, 200f * distmult );
					Steer = new NavSteer();
					if ( !pos.HasValue )
						Steer.Target = tgtpos;
					else
						Steer.Target = (Vector3)pos;
				} else
				{
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
					var distmult = Math.Clamp( dist / 300, 1, 3 );
					pos = NavMesh.GetPointWithinRadius( closer, 100f * distmult, 200f * distmult );
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

			} else if ( CurrentState == NpcState.Search )
			{
				// We lost track of the target.
				Speed = (CombatSpeed + PatrolSpeed) / 2;
				if ( LastTargetPosition != Vector3.Zero )
				{
					var tr = Trace.Ray( EyePos, tgtpos )
						.Ignore( Owner )
						.Ignore( this )
						.Run();
					if ( tr.Fraction >= 0.9 )
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
					pos = NavMesh.GetPointWithinRadius( tgtpos, 100f, 200f );
				}

				Steer = new NavSteer();
				if ( !pos.HasValue )
					Steer.Target = tgtpos;
				else
					Steer.Target = (Vector3)pos;

			}
		} else
		{
			Speed = PatrolSpeed;
			Steer = null; // this will also stop the NPC when in idle or disabled state
			if ( CurrentState == NpcState.Patrol )
				Steer = new Sandbox.Nav.Wander(); // patrol normally
		}
	}

	protected virtual void StateUpdate()
	{
		var ent = HasTarget() ? Target.Target : default;
		if ( CurrentState == NpcState.Push )
		{
			if ( !HasTarget() || TimeSinceTargetVisible >= 5f )
			{
				// Lost target, hunt them down
				FireEvent( NpcEvent.SeekTarget, ent );
				return;
			}
		} else if ( CurrentState == NpcState.Engage )
		{
			if ( !HasTarget() || TimeSinceTargetVisible >= 8f )
			{
				// Enter patrol state (we don't want to actively look for them) 
				FireEvent( NpcEvent.Disengage, ent );
				return;
			} else if ( TimeSinceTargetVisible <= 2f && ent.Health <= Health * 0.75 )
			{
				// They're weak, let's push them!
				FireEvent( NpcEvent.PushTarget, ent );
			}
		}
		else if ( CurrentState == NpcState.Search )
		{
			if ( TimeSinceTargetVisible >= 20f )
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
					FireEvent( NpcEvent.SeekTarget, ent );
			}
		}
		else if ( CurrentState == NpcState.Idle )
		{
			if ( HasTarget() )
            {
				FireEvent( NpcEvent.GetTarget, Target.Target );
			}
			else if ( !npc_gunner_nopatrol && TimeSincePathThink > 10f )
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
			else if( TimeSincePathThink > 30f )
			{
				// Let's stop for a bit
				FireEvent( NpcEvent.PausePatrol, null );
				return;
			}
		}
		DebugOverlay.ScreenText( ToString() + " " + CurrentState.ToString() + " " + Target.ToString() );
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
			info.TimeSinceReappear = 0.5f; // Delay when switching targets
			Target = info;
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
	}
	protected virtual void SetTarget( Entity ent )
	{
		if (!TargetList.ContainsKey(ent))
		{
			var info = new NpcTargetInfo( ent );
			TargetList.Add( ent, info );
		}
	}
	protected virtual void SetUnknownTarget( Entity ent )
	{
		if ( ent != null && (!HasTarget() || Target.Target != ent) && !TargetList.ContainsKey( ent ) )
		{
			var info = new NpcTargetInfo( ent );
			info.TimeSinceVisible = -1f;
			info.TimeSinceReappear = -1f;
			TargetList.Add( ent, info );
		}
	}
	protected virtual bool CheckVisibility( Entity ent, bool reducedVision = false )
	{
		if ( ent == null || !ent.IsValid() || ent.Health <= 0 )
			return false;

		var dist = (ent.Position - Position).Length;
		var angdiff = EyeRot.Distance( Rotation.LookAt( ent.EyePos - EyePos, Vector3.Up ) );
		if ( dist > (reducedVision ? 150f : 250f) && angdiff >= (reducedVision ? 75f : 100f) )
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
		// TODO: maybe this can trigger less often for performance?
		if ( TimeSinceVisCheck == 0 ) return LastVisCheck;
		LastVisCheck = false;
		var bestVisible = Target;
		var bestEnmity = HasTarget() ? CalculateEnmity( Target ) : -1;

		using ( Sandbox.Debug.Profile.Scope( "Gunner Visibility" ) )
		{
			foreach ( KeyValuePair<Entity, NpcTargetInfo> pair in TargetList )
			{
				var ent = pair.Key;
				var info = pair.Value;
				var visible = CheckVisibility( ent, info.TimeSinceVisible >= 2f );
				if ( visible )
				{
					if ( info.TimeSinceReappear > 0 ) info.TimeSinceReappear = -1 * Math.Clamp( (info.TimeSinceVisible - 1) / 3f, 0, 1f );
					info.TimeSinceVisible = 0;

					if ( ent == Target.Target )
						LastVisCheck = true;

					var enmity = CalculateEnmity( info );
					if ( bestVisible.IsValid() || enmity > bestEnmity * 2f )
					{
						bestVisible = info;
						bestEnmity = enmity;
						LastVisCheck = true;
					}
				}
			}
		}

		Target = bestVisible;

		// This caches the result for one tick (hopefully)
		TimeSinceVisCheck = 0;
		return LastVisCheck;
	}
	protected virtual void ScanForTargets()
	{
		if ( !npc_gunner_vision ) return;
		TimeSinceScan = 0;
		var acquire = false;
		foreach ( Entity ent in Physics.GetEntitiesInSphere( Position, 2000f ) )
		{
			if ( ent is Player && CheckVisibility( ent, true ) )
			{
				if ( TargetList.ContainsKey( ent ) )
				{
					var info = TargetList[ent];
					info.TimeSinceVisible = 0;
				}
				else
				{
					var info = new NpcTargetInfo( ent );
					info.TimeSinceVisible = 0;
					info.TimeSinceReappear = 1f; // first time getting you, let's be lenient
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
		if (TimeSinceScan >= 1f) ScanForTargets();

		StateUpdate();

		var ent = Target.Target;

		if ( HasTarget() && TimeSincePathThink > 3f )
		{
			SteerUpdate();
		}

		// Shoot the target!
		if ( HasTarget() && visible )
		{
			SetAnimInt( "holdtype", Weapon.HoldType );
			var angdiff = Rotation.Distance( Rotation.LookAt( ent.Position - Position, Vector3.Up ) );
			if ( Weapon != null && angdiff <= 15f && TimeSinceTargetReappear <= 1f ) // we can fire right as they disappear for a second
			{
				if ( Weapon.CanPrimaryAttack() && ( Weapon.Automatic || Weapon.TimeSincePrimaryAttack > ( 1 / WeaponSemiRate ) ) )
				{
					Weapon.AttackPrimary();
					// DebugOverlay.Line( EyePos, EyePos + EyeRot.Forward * 1000, 3 );
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
			if ( HasTarget() && TimeSinceTargetReappear <= 0 )
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
			if (TimeSinceTargetVisible >= 3f)
			{
				// Add some head movement since we're looking around for the target
				tgt += Rotation.FromYaw( Rotation.Yaw() + MathF.Sin( Time.Now * 2f ) * 75f );
			}
			else
			{
				// Add sway to imitate inperfect aim, otherwise we're just an aimbot!
				tgt.x += MathF.Sin( Time.Now / 2f ) * Time.Delta * 0.5f;
				tgt.y += MathF.Cos( Time.Now / 2f ) * Time.Delta * 0.25f;

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

		SetAnimBool( "b_grounded", true );
		SetAnimBool( "b_noclip", false );
		SetAnimBool( "b_swim", false );

		var forward = Vector3.Dot( Rotation.Forward, Velocity.Normal );
		var sideward = Vector3.Dot( Rotation.Right, Velocity.Normal );
		var angle = MathF.Atan2( sideward, forward ).RadianToDegree().NormalizeDegrees();
		SetAnimFloat( "move_direction", angle );

		SetAnimFloat( "wishspeed", Velocity.Length * 1.5f );
		SetAnimFloat( "walkspeed_scale", 1.0f / 10.0f * (Speed / 100) );
		SetAnimFloat( "runspeed_scale", 1.0f / 320.0f );
		SetAnimFloat( "duckspeed_scale", 1.0f / 80.0f );
	}

	public override void TakeDamage( DamageInfo info )
	{

		if ( CurrentState == NpcState.Idle || CurrentState == NpcState.Patrol )
		{
			// sneak attack!
			info.Damage = info.Damage * 2;
		}

		base.TakeDamage( info );
		var attacker = info.Attacker;

		if ( attacker == null || attacker == this ) return;

		if ( TargetList.ContainsKey(attacker) )
		{
			var targetInfo = TargetList[attacker];
			targetInfo.Enmity += info.Damage;
			targetInfo.LastPosition = attacker.Position; // attacking reveals position. kind of unfair?
		} else
		{
			var targetInfo = new NpcTargetInfo( attacker );
			targetInfo.Enmity = info.Damage;
			TargetList.Add( attacker, targetInfo );
		}
		if (CurrentState == NpcState.Idle || CurrentState == NpcState.Patrol || CurrentState == NpcState.Search)
		{
			FireEvent( NpcEvent.SeekTarget, attacker );
		}
	}
}

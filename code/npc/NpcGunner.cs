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

class NpcGunner : NpcPawn //IStateMachine
{

	[ConVar.Replicated]
	public static bool npc_gunner_nopatrol { get; set; }

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

	protected BaseNpcWeapon Weapon;

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
	bool LastVisCheck;

	public Entity Target { get; protected set; }
	public Vector3 LastTargetPosition { get; protected set; }
	TimeSince TimeSinceTargetVisible;
	TimeSince TimeSinceTargetReappear; // Give a small delay if target disappeared for a while. this will be more fair

	// --------------------------------------------------
	// State Machine Stuff
	// --------------------------------------------------

	Dictionary<NpcStateTransition, NpcState> transitions;
	Dictionary<NpcEvent, Action<object>> eventFunc;

	protected virtual void InitializeStates()
	{
		transitions = new Dictionary<NpcStateTransition, NpcState>
		{
			{ new NpcStateTransition(NpcState.Inactive, NpcEvent.Enable), NpcState.Idle },
			{ new NpcStateTransition(NpcState.Idle, NpcEvent.Disable), NpcState.Inactive },

			{ new NpcStateTransition(NpcState.Idle, NpcEvent.ResumePatrol), NpcState.Patrol },
			{ new NpcStateTransition(NpcState.Patrol, NpcEvent.PausePatrol), NpcState.Idle },

			{ new NpcStateTransition(NpcState.Idle, NpcEvent.GetTarget), NpcState.Engage },
			{ new NpcStateTransition(NpcState.Patrol, NpcEvent.GetTarget), NpcState.Engage },

			{ new NpcStateTransition(NpcState.Engage, NpcEvent.PushTarget), NpcState.Push },
			{ new NpcStateTransition(NpcState.Search, NpcEvent.PushTarget), NpcState.Push },

			{ new NpcStateTransition(NpcState.Search, NpcEvent.SeekTarget), NpcState.Search }, // a new unknown target might override our current one
			{ new NpcStateTransition(NpcState.Patrol, NpcEvent.SeekTarget), NpcState.Search },
			{ new NpcStateTransition(NpcState.Push, NpcEvent.SeekTarget), NpcState.Search },

			{ new NpcStateTransition(NpcState.Engage, NpcEvent.Disengage), NpcState.Patrol },
			{ new NpcStateTransition(NpcState.Push, NpcEvent.Disengage), NpcState.Patrol },
			{ new NpcStateTransition(NpcState.Search, NpcEvent.Disengage), NpcState.Patrol },
		};

		eventFunc = new Dictionary<NpcEvent, Action<object>>
		{
			{ NpcEvent.Enable, obj => { SetTarget(null); SteerUpdate(); } },
			// Disable is handled specially
			{ NpcEvent.ResumePatrol, obj => SteerUpdate() },
			{ NpcEvent.PausePatrol, obj => SteerUpdate() },
			{ NpcEvent.GetTarget, obj => { SetTarget(obj); SteerUpdate(); } },
			{ NpcEvent.PushTarget, obj => { SetTarget(obj); SteerUpdate(); } },
			{ NpcEvent.SeekTarget, obj => { SetUnknownTarget(obj); SteerUpdate(); } },
			{ NpcEvent.Disengage, obj => { SetTarget(null); SteerUpdate(); } },

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
			Target = null;
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
		if ( Target != null )
		{
			// If we've seen our target recently, we know where they are;
			// otherwise we can only proceed with last known pos
			var tgtpos = TimeSinceTargetVisible < 2f ? Target.Position : LastTargetPosition;
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
				} else
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
		// TODO: check visual cone for enemies?
		if ( CurrentState == NpcState.Push )
		{
			if ( Target == null || Target.Health <= 0 || TimeSinceTargetVisible >= 5f )
			{
				// Lost target, hunt them down
				FireEvent( NpcEvent.SeekTarget, Target );
				return;
			}
		} else if ( CurrentState == NpcState.Engage )
		{
			if ( Target == null || Target.Health <= 0 || TimeSinceTargetVisible >= 8f )
			{
				// Enter patrol state (we don't want to actively look for them) 
				FireEvent( NpcEvent.Disengage, null );
				return;
			} else if ( TimeSinceTargetVisible <= 2f && Target.Health <= Health * 0.75 )
			{
				// They're weak, let's push them!
				FireEvent( NpcEvent.PushTarget, Target );
			}
		}
		else if ( CurrentState == NpcState.Search )
		{
			if ( TimeSinceTargetVisible >= 20f )
			{
				// Give up hunting
				FireEvent( NpcEvent.Disengage, null );
				return;
			} else if ( TimeSinceTargetVisible <= 0.5f )
			{
				if ( Target == null || Target.Health <= 0 )
					FireEvent( NpcEvent.Disengage, null );
				else if ( Target.Health <= Health * 0.75 )
					FireEvent( NpcEvent.PushTarget, Target );
				else
					FireEvent( NpcEvent.SeekTarget, Target );
			}
		}
		else if ( CurrentState == NpcState.Idle )
		{
			if ( !npc_gunner_nopatrol && TimeSincePathThink > 10f )
			{
				// Let's start patrolling!
				FireEvent( NpcEvent.ResumePatrol, null );
				return;
			}
		}
		else if ( CurrentState == NpcState.Patrol )
		{
			if ( TimeSincePathThink > 30f )
			{
				// Let's stop for a bit
				FireEvent( NpcEvent.PausePatrol, null );
				return;
			}
		}
	}

	protected virtual void SetTarget(object obj)
	{
		// TODO: maybe we can prioritize different threats?
		// TODO: store an array of known targets and their position?
		if ( obj == null )
		{
			Target = null;
			LastTargetPosition = Vector3.Zero;
		}
		else if ( obj is Entity )
		{
			Target = obj as Entity;
			LastTargetPosition = Target.Position;
		}
	}

	protected virtual void SetUnknownTarget( object obj )
	{
		if ( obj != null && Target != obj && obj is Entity )
		{
			Target = obj as Entity;
			TimeSinceTargetVisible = 10f;
			TimeSinceTargetReappear = -1f;
			LastTargetPosition = Target.Position;
		}
	}

	// --------------------------------------------------
	// AI Logic
	// --------------------------------------------------

	protected bool CheckTargetVisiblity()
	{
		// TODO: maybe this can trigger less often for performance?
		if ( TimeSinceVisCheck == 0 ) return LastVisCheck;

		if ( Target == null || Target.Health <= 0 )
		{
			Target = null;
			LastVisCheck = false;
		}
		else
		{
			var dist = (Target.Position - Position).Length;
			var angdiff = EyeRot.Distance( Rotation.LookAt( Target.EyePos - EyePos, Vector3.Up ) );
			if ( dist > (TimeSinceTargetVisible > 2f ? 150f : 250f) && angdiff >= (TimeSinceTargetVisible > 2f ? 75f : 100f) ) {
				// can only see in front; but up close we get some situational awareness
				// our visibility gets a nerf if we haven't seen the target for a bit; this allows them to sneak up on us
				LastVisCheck = false;
			} else
			{
				var tr = Trace.Ray( EyePos, Target.EyePos )
							.UseHitboxes()
							.Ignore( Owner )
							.Ignore( this )
							.Run();
				if ( tr.Entity != Target )
				{
					LastVisCheck = false;
				}
				else // We can see the target!
				{
					// give a slight "reaction delay"
					if ( TimeSinceTargetReappear > 0 ) TimeSinceTargetReappear = -1 * Math.Clamp( (TimeSinceTargetVisible - 1) / 3f, 0, 1f );

					TimeSinceTargetVisible = 0;
					LastTargetPosition = Target.Position;
					LastVisCheck = true;
				}
			}
		}

		// This caches the result for one tick (hopefully)
		TimeSinceVisCheck = 0;
		return LastVisCheck;
	}

	public override void Spawn()
	{
		base.Spawn();

		InitializeStates();

		CurrentState = NpcState.Idle;
		LastTargetPosition = Vector3.Zero;

		// TODO: better way to set NPC weapons
		Weapon = new NpcWeaponPistol();
		Weapon.OnCarryStart( this );
		Speed = PatrolSpeed;
	}

	protected override void NpcThink()
	{
		var visible = CheckTargetVisiblity();

		StateUpdate();

		if ( Target != null && TimeSincePathThink > 3f )
		{
			SteerUpdate();
		}

		// Shoot the target!
		if ( Target != null && visible )
		{
			SetAnimInt( "holdtype", 1 );
			var angdiff = Rotation.Distance( Rotation.LookAt( Target.Position - Position, Vector3.Up ) );
			if ( Weapon != null && angdiff <= 15f && TimeSinceTargetReappear >= 0 )
			{
				if ( Weapon.CanPrimaryAttack() )
				{
					Weapon.AttackPrimary();
				}
				else if ( Weapon.CanReload() && Weapon.AmmoClip == 0 )
				{
					Weapon.Reload();
				}
			}
		}
		else if ( Target == null )
		{
			if ( Weapon.CanReload() && Weapon.AmmoClip < Weapon.ClipSize )
			{
				Weapon.Reload(); // never hurts to top it off
			}
			SetAnimInt( "holdtype", Weapon.IsReloading ? 1 : 0 );
		}
	}

	protected override void NpcTurn()
	{
		var visible = CheckTargetVisiblity();
		if ( LastTargetPosition != Vector3.Zero )
		{
			Vector3 vec;
			if ( visible && TimeSinceTargetReappear >= 0 )
				vec = Target.Position - Position;
			else
				vec = LastTargetPosition - Position;

			var TargetRotation = Rotation.LookAt( vec.WithZ( 0 ), Vector3.Up );
			//var eyeRotSpeed = Math.Clamp( vec.Length / 75f, 2.5f, 7.5f ); // The closer the Target, the harder it is to adjust
			var lastRot = Rotation;
			Rotation = Rotation.Lerp( Rotation, TargetRotation, Time.Delta * 5f );
			EyeRot += (Rotation - lastRot); // when our body rotates, so do our eyes

			var tgt = Rotation.LookAt( vec, Vector3.Up );
			if (TimeSinceTargetVisible >= 3f)
				tgt += Rotation.FromYaw( Rotation.Yaw() + MathF.Sin( Time.Now * 2f ) * 75f );

			EyeRot = Rotation.Lerp( EyeRot, tgt, Time.Delta * 15f );
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
		var visible = CheckTargetVisiblity();
		var lookPos = EyePos + EyeRot.Forward * 200; //visible ? Target.EyePos : (LastTargetPosition == Vector3.Zero ? EyePos + EyeRot.Forward * 200 : LastTargetPosition);

		SetAnimLookAt( "aim_eyes", visible ? Target.EyePos : (LastTargetPosition == Vector3.Zero ? EyePos + EyeRot.Forward * 200 : LastTargetPosition) );
		SetAnimLookAt( "aim_head", lookPos );
		// SetAnimLookAt( "aim_body", EyePos + EyeRot.Forward * 200 );
		SetAnimLookAt( "aim_body", Position + Rotation.Forward * 200 );
		SetAnimFloat( "aim_body_weight", 0.5f );

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
		base.TakeDamage( info );
		if (CurrentState == NpcState.Idle || CurrentState == NpcState.Patrol || CurrentState == NpcState.Search)
		{
			FireEvent( NpcEvent.SeekTarget, info.Attacker );
		}
	}
}

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
	SeekTarget, // push/patrol -> search
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

	// --------------------------------------------------
	// Variables
	// --------------------------------------------------

	protected BaseNpcWeapon Weapon;

	protected float PatrolSpeed = 80f;
	protected float CombatSpeed = 150f;

	// The distance within which we try to back off
	protected float MinCombatDistance = 50f;

	// The distance beyond which we will close in if pushing
	protected float PushCombatDistance = 200f;

	// The distance beyond which we don't try to shoot (not effective)
	protected float MaxCombatDistance = 400f;

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

			if ( CurrentState == NpcState.Engage )
			{
				// TODO close up to target if beyond max dist
				// We are in engage mode. Speed up and wander nearby (like strafing)
				Speed = CombatSpeed;
				Sandbox.Nav.Wander wander = new Sandbox.Nav.Wander();
				wander.MinRadius = 25f;
				wander.MaxRadius = 75f;
				Steer = wander;
			}
			else if ( CurrentState == NpcState.Push )
			{
				// We are pushing. Let's get in some trouble and die for the player's amusement!
				Speed = CombatSpeed;
				var dist = (tgtpos - Position).Length;
				Vector3? pos;
				if ( dist > PushCombatDistance )
				{
					// If we are not close, try to find a position in between the target and ourselves
					var closer = Position + (tgtpos - Position) * 0.5f;
					pos = NavMesh.GetPointWithinRadius( closer, 150f, 300f );
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
				// We lost track of the target. Carefully approach area of last known position
				Speed = PatrolSpeed;
				var pos = NavMesh.GetPointWithinRadius( tgtpos, 200f, 400f );
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
			if ( TimeSinceTargetVisible >= 5f )
			{
				// Lost target, hunt them down
				FireEvent( NpcEvent.SeekTarget, Target );
				return;
			}
		} else if ( CurrentState == NpcState.Engage )
		{
			if ( TimeSinceTargetVisible >= 10f )
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
			if ( TimeSinceTargetVisible >= 15f )
			{
				// Give up hunting
				FireEvent( NpcEvent.Disengage, null );
				return;
			} else if ( TimeSinceTargetVisible == 0 )
			{
				if ( Target.Health <= Health * 0.75 )
					FireEvent( NpcEvent.PushTarget, Target );
				else
					FireEvent( NpcEvent.SeekTarget, Target );
			}
		}
		else if ( CurrentState == NpcState.Idle )
		{
			if ( TimeSincePathThink > 15f )
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
			if ( dist > 150f && angdiff >= 90f ) {
				LastVisCheck = false; // can only see in front; but up close we get some situational awareness
			} else
			{
				var tr = Trace.Ray( EyePos, Target.EyePos )
							.UseHitboxes()
							.Ignore( Owner )
							.Ignore( this )
							.Size( 4 )
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

		if ( Target != null && TimeSincePathThink > 5f )
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

		/*
		var shouldshoot = VisCheck();

		if ( Steer == null )
		{
			if ( Target != null && Target.Health > 0 )
			{
				Speed = CombatSpeed;
				Steer = new NavSteer();
				Steer.Target = Target.Position;
				SetAnimInt( "holdtype", 1 );
			} else
			{
				Target = null;
				Speed = PatrolSpeed;
				Steer = new Sandbox.Nav.Wander();
				SetAnimInt( "holdtype", 0 );
			}
			TimeSincePathThink = 0;
		} else if ( Target != null )
		{
			var angdiff = Rotation.Distance( Rotation.LookAt( Target.Position - Position, Vector3.Up ) );
			if ( Weapon != null && shouldshoot && angdiff <= 15f )
			{
				if ( Weapon.CanPrimaryAttack() )
				{
					Weapon.AttackPrimary();
				} else if ( Weapon.CanReload() && Weapon.AmmoClip == 0 )
				{
					Weapon.Reload();
				}
			}

			if ( TimeSincePathThink > 0.25f )
			{
				TimeSincePathThink = 0;

				var vec = Target.Position - Position;
				var norm = vec.Normal;
				var dist = vec.Length;

				if ( shouldshoot )
				{
					var rand = new Random();
					Steer.Target = Target.Position + norm.Cross( Vector3.Up ) * ((float)rand.NextDouble() * 1000f - 500f);
					if ( dist > 3000 )
					{
						Steer.Target += norm * 500;
					}
					else if ( dist <= 200 )
					{
						Steer.Target += norm * -200;
					}
				} else
				{
					Steer.Target = Target.Position;
				}
			}
		} else
		{
			if ( TimeSincePathThink > 5f )
			{
				var rand = new Random();
				TimeSincePathThink = (float)rand.NextDouble() * 3f + 1;
				LookDir = Steer.Target + (Vector3.Random * 200f).WithZ(EyePos.z);
			}
		}
		*/
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
			Rotation = Rotation.Lerp( Rotation, TargetRotation, Time.Delta * 5f );
			var eyeRotSpeed = 10f; // Math.Clamp( vec.Length / 75f, 2.5f, 7.5f ); // The closer the Target, the harder it is to adjust
			EyeRot = Rotation.Lerp( EyeRot, Rotation.LookAt( vec, Vector3.Up ), Time.Delta * eyeRotSpeed );
		}
		else
		{
			var walkVelocity = Velocity.WithZ( 0 );
			if ( walkVelocity.Length > 0.5f )
			{
				var turnSpeed = walkVelocity.Length.LerpInverse( 0, 100, true );
				var targetRotation = Rotation.LookAt( walkVelocity.Normal, Vector3.Up );
				Rotation = Rotation.Lerp( Rotation, targetRotation, turnSpeed * Time.Delta * 20.0f );
			}
		}
	}

	protected override void NpcAnim()
	{
		var visible = CheckTargetVisiblity();
		var lookPos = visible ? Target.EyePos : LastTargetPosition;

		SetAnimLookAt( "aim_eyes", lookPos );
		SetAnimLookAt( "aim_head", lookPos );
		SetAnimLookAt( "aim_body", EyePos + EyeRot.Forward * 200 );
		SetAnimFloat( "aim_body_weight", 0.5f );

		SetAnimBool( "b_grounded", true );
		SetAnimBool( "b_noclip", false );
		SetAnimBool( "b_swim", false );

		var forward = Vector3.Dot( Rotation.Forward, Velocity.Normal );
		var sideward = Vector3.Dot( Rotation.Right, Velocity.Normal );
		var angle = MathF.Atan2( sideward, forward ).RadianToDegree().NormalizeDegrees();
		SetAnimFloat( "move_direction", angle );

		SetAnimFloat( "wishspeed", Velocity.Length * 1.5f );
		SetAnimFloat( "walkspeed_scale", 1.0f / 10.0f );
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

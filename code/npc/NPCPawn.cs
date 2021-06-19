using Sandbox;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public partial class NpcPawn : AnimEntity
{

	[ConVar.Replicated]
	public static bool nav_drawpath { get; set; }

	[ServerCmd( "npc_clear" )]
	public static void NpcClear()
	{
		foreach ( var npc in Entity.All.OfType<NpcPawn>().ToArray() )
			npc.Delete();
	}

	protected float Speed;

	protected NavPath Path = new NavPath();
	public NavSteer Steer;

	protected Vector3 InputVelocity;
	protected Vector3 LookDir;

	public override void Spawn()
	{
		base.Spawn();

		SetModel( "models/citizen/citizen.vmdl" );
		EyePos = Position + Vector3.Up * 64;
		CollisionGroup = CollisionGroup.Player;
		SetupPhysicsFromCapsule( PhysicsMotionType.Keyframed, Capsule.FromHeightAndRadius( 72, 8 ) );

		EnableHitboxes = true;

		this.SetMaterialGroup( Rand.Int( 0, 3 ) );

		new ModelEntity( "models/citizen_clothes/trousers/trousers.smart.vmdl", this );
		new ModelEntity( "models/citizen_clothes/jacket/labcoat.vmdl", this );
		new ModelEntity( "models/citizen_clothes/shirt/shirt_longsleeve.scientist.vmdl", this );

		if ( Rand.Int( 3 ) == 1 )
		{
			new ModelEntity( "models/citizen_clothes/hair/hair_femalebun.black.vmdl", this );
		}
		else if ( Rand.Int( 10 ) == 1 )
		{
			new ModelEntity( "models/citizen_clothes/hat/hat_hardhat.vmdl", this );
		}

		SetBodyGroup( 1, 0 );

		Speed = 100f;
		Health = 100;
	}

	protected virtual void NpcThink()
	{
		if (Steer == null)
		{
			Steer = new Sandbox.Nav.Wander();
		}
	}

	protected virtual void NpcTurn()
	{
		var walkVelocity = Velocity.WithZ( 0 );
		if ( walkVelocity.Length > 0.5f )
		{
			var turnSpeed = walkVelocity.Length.LerpInverse( 0, 100, true );
			var targetRotation = Rotation.LookAt( walkVelocity.Normal, Vector3.Up );
			Rotation = Rotation.Lerp( Rotation, targetRotation, turnSpeed * Time.Delta * 20.0f );
		}
	}

	protected virtual void NpcAnim()
	{
		using ( Sandbox.Debug.Profile.Scope( "Set Anim Vars" ) )
		{
			LookDir = Vector3.Lerp( LookDir, InputVelocity.WithZ( 0 ) * 1000, Time.Delta * 1.0f );
			//SetAnimLookAt( "lookat_pos", Steer.Target );
			//SetAnimLookAt( "aimat_pos", Steer.Target );
			SetAnimLookAt( "aim_eyes", Steer.Target );
			SetAnimLookAt( "aim_head", Steer.Target );
			SetAnimLookAt( "aim_body", Rotation.Forward );
			//SetAnimFloat( "aimat_weight", 0.5f );
			SetAnimFloat( "aim_body_weight", 0.5f );
		}

		using ( Sandbox.Debug.Profile.Scope( "Set Anim Vars" ) )
		{
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
	}

	[Event.Tick.Server]
	public void Tick()
	{

		NpcThink();

		using var _a = Sandbox.Debug.Profile.Scope( "NpcTest::Tick" );

		InputVelocity = 0;

		if ( Steer != null )
		{
			using var _b = Sandbox.Debug.Profile.Scope( "Steer" );

			Steer.Tick( Position );

			if ( !Steer.Output.Finished )
			{
				InputVelocity = Steer.Output.Direction.Normal;
				Velocity = Velocity.AddClamped( InputVelocity * Time.Delta * 500, Speed );
			}

			if ( nav_drawpath )
			{
				Steer.DebugDrawPath();
			}
		}

		using ( Sandbox.Debug.Profile.Scope( "Move" ) )
		{
			Move( Time.Delta );
		}

		NpcTurn();
		NpcAnim();
	}

	protected virtual void Move( float timeDelta )
	{
		var bbox = BBox.FromHeightAndRadius( 64, 4 );
		//DebugOverlay.Box( Position, bbox.Mins, bbox.Maxs, Color.Green );

		MoveHelper move = new( Position, Velocity );
		move.MaxStandableAngle = 50;
		move.Trace = move.Trace.Ignore( this ).Size( bbox );

		if ( !Velocity.IsNearlyZero( 0.001f ) )
		{
			//	Sandbox.Debug.Draw.Once
			//						.WithColor( Color.Red )
			//						.IgnoreDepth()
			//						.Arrow( Position, Position + Velocity * 2, Vector3.Up, 2.0f );

			using ( Sandbox.Debug.Profile.Scope( "TryUnstuck" ) )
				move.TryUnstuck();

			using ( Sandbox.Debug.Profile.Scope( "TryMoveWithStep" ) )
				move.TryMoveWithStep( timeDelta, 30 );
		}

		using ( Sandbox.Debug.Profile.Scope( "Ground Checks" ) )
		{
			var tr = move.TraceDirection( Vector3.Down * 10.0f );

			if ( move.IsFloor( tr ) )
			{
				GroundEntity = tr.Entity;

				if ( !tr.StartedSolid )
				{
					move.Position = tr.EndPos;
				}

				if ( InputVelocity.Length > 0 )
				{
					var movement = move.Velocity.Dot( InputVelocity.Normal );
					move.Velocity = move.Velocity - movement * InputVelocity.Normal;
					move.ApplyFriction( tr.Surface.Friction * 10.0f, timeDelta );
					move.Velocity += movement * InputVelocity.Normal;

				}
				else
				{
					move.ApplyFriction( tr.Surface.Friction * 10.0f, timeDelta );
				}
			}
			else
			{
				GroundEntity = null;
				move.Velocity += Vector3.Down * 900 * timeDelta;
				Sandbox.Debug.Draw.Once.WithColor( Color.Red ).Circle( Position, Vector3.Up, 10.0f );
			}
		}

		Position = move.Position;
		Velocity = move.Velocity;
	}


	[ClientRpc]
	void BecomeRagdollOnClient( Vector3 force, int forceBone )
	{
		// TODO - lets not make everyone write this shit out all the time
		// maybe a CreateRagdoll<T>() on ModelEntity?
		var ent = new ModelEntity();
		ent.Position = Position;
		ent.Rotation = Rotation;
		ent.MoveType = MoveType.Physics;
		ent.UsePhysicsCollision = true;
		ent.SetInteractsAs( CollisionLayer.Debris );
		ent.SetInteractsWith( CollisionLayer.WORLD_GEOMETRY );
		ent.SetInteractsExclude( CollisionLayer.Player | CollisionLayer.Debris );

		ent.SetModel( GetModelName() );
		ent.CopyBonesFrom( this );
		ent.TakeDecalsFrom( this );
		ent.SetRagdollVelocityFrom( this );
		ent.DeleteAsync( 5f );

		// Copy the skin color
		ent.SetMaterialGroup( GetMaterialGroup() );

		// Copy the clothes over
		foreach ( var child in Children )
		{
			if ( child is ModelEntity e )
			{
				var model = e.GetModelName();
				if ( model != null && !model.Contains( "clothes" ) ) // Uck we 're better than this, entity tags, entity type or something?
					continue;

				var clothing = new ModelEntity();
				clothing.SetModel( model );
				clothing.SetParent( ent, true );
			}
		}

		ent.PhysicsGroup.AddVelocity( force );

		if ( forceBone >= 0 )
		{
			var body = ent.GetBonePhysicsBody( forceBone );
			if ( body != null )
			{
				body.ApplyForce( force * 1000 );
			}
			else
			{
				ent.PhysicsGroup.AddVelocity( force );
			}
		}
	}

	public override void OnKilled()
	{
		base.OnKilled();

		BecomeRagdollOnClient( LastDamage.Force, GetHitboxBone( LastDamage.HitboxIndex ) );
	}

	DamageInfo LastDamage;

	public override void TakeDamage( DamageInfo info )
	{
		LastDamage = info;

		// hack - hitbox 0 is head
		// we should be able to get this from somewhere
		if ( info.HitboxIndex == 0 )
		{
			info.Damage *= 2.0f;
		}

		base.TakeDamage( info );

		if ( info.Attacker is HeistPlayer attacker )
		{
			// Note - sending this only to the attacker!
			attacker.DidDamage( To.Single( attacker ), info.Position, info.Damage, Health.LerpInverse( 100, 0 ) );
		}
	}
}

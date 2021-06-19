using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox;
class NpcGunner : NpcPawn
{
	protected Entity target { get; set; }
	protected BaseNpcWeapon Weapon;
	float PatrolSpeed = 80f;
	float CombatSpeed = 150f;

	TimeSince TimeSincePathThink;

	protected bool VisCheck()
	{
		if (target == null || target.Health <= 0) return false;
		var tr = Trace.Ray( EyePos, target.EyePos )
					.UseHitboxes()
					.Ignore( Owner )
					.Ignore( this )
					.Size( 4 )
					.Run();
		if ( tr.Entity != target ) return false;
		return true;
	}

	public override void Spawn()
	{
		base.Spawn();

		Weapon = new NpcWeaponPistol();
		Weapon.OnCarryStart( this );
		Speed = PatrolSpeed;
	}
	protected override void NpcThink()
	{

		var shouldshoot = VisCheck();

		if ( Steer == null )
		{
			if ( target != null && target.Health > 0 )
			{
				Speed = CombatSpeed;
				Steer = new NavSteer();
				Steer.Target = target.Position;
				SetAnimInt( "holdtype", 1 );
			} else
			{
				target = null;
				Speed = PatrolSpeed;
				Steer = new Sandbox.Nav.Wander();
				SetAnimInt( "holdtype", 0 );
			}
			TimeSincePathThink = 0;
		} else if ( target != null )
		{
			var angdiff = Rotation.Distance( Rotation.LookAt( target.Position - Position, Vector3.Up ) );
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

				var vec = target.Position - Position;
				var norm = vec.Normal;
				var dist = vec.Length;

				if ( shouldshoot )
				{
					var rand = new Random();
					Steer.Target = target.Position + norm.Cross( Vector3.Up ) * ((float)rand.NextDouble() * 1000f - 500f);
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
					Steer.Target = target.Position;
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
	}

	protected override void NpcTurn()
	{
		if ( target != null )
		{
			var vec = target.Position - Position;
			var targetRotation = Rotation.LookAt( vec.WithZ( 0 ), Vector3.Up );
			Rotation = Rotation.Lerp( Rotation, targetRotation, Time.Delta * 5f );
			var eyeRotSpeed = 10f; // Math.Clamp( vec.Length / 75f, 2.5f, 7.5f ); // The closer the target, the harder it is to adjust
			EyeRot = Rotation.Lerp( EyeRot, Rotation.LookAt( target.EyePos - EyePos, Vector3.Up ), Time.Delta * eyeRotSpeed );
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
		using ( Sandbox.Debug.Profile.Scope( "Set Anim Vars" ) )
		{
			var lookPos = (target != null) ? target.EyePos : LookDir;
			//SetAnimLookAt( "lookat_pos", EyePos + EyeRot.Forward * 200 );
			//SetAnimLookAt( "aimat_pos", EyePos + EyeRot.Forward * 200 );
			SetAnimLookAt( "aim_eyes", lookPos );
			SetAnimLookAt( "aim_head", lookPos );
			SetAnimLookAt( "aim_body", EyePos + EyeRot.Forward * 200 );
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

	public override void TakeDamage( DamageInfo info )
	{
		base.TakeDamage( info );
		if (target == null && info.Attacker is HeistPlayer)
		{
			target = info.Attacker;
			Steer = null;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox;

partial class BaseNpcWeapon : AnimEntity
{
	public virtual int ClipSize => 16;
	public virtual float PrimaryRate => 5.0f;
	public virtual float SecondaryRate => 15.0f;
	public virtual float ReloadTime => 3.0f;

	public override void Spawn()
	{
		base.Spawn();
		AmmoClip = ClipSize;

		CollisionGroup = CollisionGroup.Weapon; // so players touch it as a trigger but not as a solid
		SetInteractsAs( CollisionLayer.Debris ); // so player movement doesn't walk into it
	}
	[Net, Predicted] public int AmmoClip { get; set; }
	[Net, Predicted] public TimeSince TimeSinceReload { get; set; }
	[Net, Predicted] public bool IsReloading { get; set; }

	[Net, Predicted]
	public TimeSince TimeSincePrimaryAttack { get; set; }

	[Net, Predicted]
	public TimeSince TimeSinceSecondaryAttack { get; set; }

	public bool CanPrimaryAttack()
	{
		if ( !Owner.IsValid() || IsReloading ) return false;

		var rate = PrimaryRate;
		if ( rate <= 0 ) return true;

		return TimeSincePrimaryAttack > (1 / rate);
	}
	public virtual void AttackPrimary()
	{

	}

	public virtual bool CanSecondaryAttack()
	{
		if ( !Owner.IsValid() || IsReloading ) return false;

		var rate = SecondaryRate;
		if ( rate <= 0 ) return true;

		return TimeSinceSecondaryAttack > (1 / rate);
	}
	public virtual void AttackSecondary()
	{

	}

	public bool CanReload()
	{
		if ( !Owner.IsValid() || IsReloading ) return false;
		return true;
	}
	public void Reload()
	{
		TimeSinceReload = 0;
		IsReloading = true;
		(Owner as AnimEntity).SetAnimBool( "b_reload", true );
	}

	[Event.Tick.Server]
	public void Tick()
	{
		if ( IsReloading && TimeSinceReload > ReloadTime )
		{
			IsReloading = false;
			AmmoClip = ClipSize;
		}
	}

	public override void OnCarryStart( Entity carrier )
	{
		SetParent( carrier, true );
		Owner = carrier;
		MoveType = MoveType.None;
		EnableAllCollisions = false;
		base.ActiveStart( carrier );
		EnableDrawing = true;
	}

	[ClientRpc]
	protected virtual void ShootEffects()
	{
		Host.AssertClient();
		Particles.Create( "particles/pistol_muzzleflash.vpcf", this, "muzzle" );
	}
	public bool TakeAmmo( int amount )
	{
		if ( AmmoClip < amount )
			return false;

		AmmoClip -= amount;
		return true;
	}

	public virtual IEnumerable<TraceResult> TraceBullet( Vector3 start, Vector3 end, float radius = 2.0f )
	{
		bool InWater = Physics.TestPointContents( start, CollisionLayer.Water );

		var tr = Trace.Ray( start, end )
				.UseHitboxes()
				.HitLayer( CollisionLayer.Water, !InWater )
				.Ignore( Owner )
				.Ignore( this )
				.Size( radius )
				.Run();

		yield return tr;

		//
		// Another trace, bullet going through thin material, penetrating water surface?
		//
	}

	public virtual void ShootBullet( float spread, float force, float damage, float bulletSize )
	{
		var forward = Owner.EyeRot.Forward;
		forward += (Vector3.Random + Vector3.Random + Vector3.Random + Vector3.Random) * spread * 0.25f;
		forward = forward.Normal;

		//
		// ShootBullet is coded in a way where we can have bullets pass through shit
		// or bounce off shit, in which case it'll return multiple results
		//
		foreach ( var tr in TraceBullet( Owner.EyePos, Owner.EyePos + forward * 5000, bulletSize ) )
		{
			tr.Surface.DoBulletImpact( tr );

			if ( !IsServer ) continue;
			if ( !tr.Entity.IsValid() ) continue;

			//
			// We turn predictiuon off for this, so any exploding effects don't get culled etc
			//
			using ( Prediction.Off() )
			{
				var damageInfo = DamageInfo.FromBullet( tr.EndPos, forward * 100 * force, damage )
					.UsingTraceResult( tr )
					.WithAttacker( Owner )
					.WithWeapon( this );

				tr.Entity.TakeDamage( damageInfo );
			}
		}
	}
}

using Sandbox;
using Sandbox.UI;
using Sandbox.UI.Construct;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

partial class BaseHeistWeapon : BaseWeapon, IRespawnableEntity
{
	public virtual AmmoType AmmoType => AmmoType.Pistol;
	public virtual int ClipSize => 16;
	public virtual float ReloadTime => 3.0f;
	public virtual float ReloadFinishTime => 0.0f;
	public virtual int Bucket => 1;
	public virtual int BucketWeight => 100;
	public virtual float SprintOutTime => 0.5f;
	public virtual bool Automatic => true;
	public virtual int HoldType => 1; // this is shit indeed

	[Net, Predicted] public int AmmoClip { get; set; }
	[Net, Predicted] public TimeSince TimeSinceReload { get; set; }
	[Net, Predicted] public bool IsReloading { get; set; }
	
	public PlayerController Controller => (Owner as HeistPlayer)?.Controller as PlayerController;

	[Net] public bool IsAiming => Controller?.IsAiming == true;
	[Net] public bool IsInSprint => Controller?.IsInSprint == true;
	[Net] public bool IsLeaning => Controller?.IsLeaning == true;
	[Net, Predicted] public TimeSince TimeSinceDeployed { get; set; }
	[Net, Predicted] public TimeSince TimeSinceSprint { get; set; }

	public PickupTrigger PickupTrigger { get; protected set; }

	public virtual float BobScale => 1f;
	public virtual float SwayScale => 1f;

	public int AvailableAmmo()
	{
		if (Owner is NpcPawn)
		{
			return 10000;
		}
		var owner = Owner as HeistPlayer;
		if ( owner == null ) return 0;
		return owner.AmmoCount( AmmoType );
	}

	public override void ActiveStart( Entity ent )
	{
		base.ActiveStart( ent );
		TimeSinceDeployed = 0;
	}

	public override string ViewModelPath => "weapons/rust_pistol/v_rust_pistol.vmdl";

	public override void Spawn()
	{
		base.Spawn();

		SetModel( "weapons/rust_pistol/rust_pistol.vmdl" );
		AmmoClip = ClipSize;

		PickupTrigger = new PickupTrigger();
		PickupTrigger.Parent = this;
		PickupTrigger.Position = Position;
	}

	public override bool CanReload()
	{
		if ( !Owner.IsValid() || (Owner is Player && !Input.Down( InputButton.Reload )) || (Owner is NpcPawn && IsReloading) ) return false;
		return true;
	}

	public override void Reload()
	{
		if ( IsReloading )
			return;

		if ( AmmoClip >= ClipSize )
			return;

		TimeSinceReload = 0;

		if ( Owner is HeistPlayer player )
		{
			if ( player.AmmoCount( AmmoType ) <= 0 )
				return;

			StartReloadEffects();
		} else if (Owner is NpcPawn)
		{
			AsyncReload();
		}

		IsReloading = true;

		(Owner as AnimEntity).SetAnimBool( "b_reload", true );

		// StartReloadEffects();
	}

	public override void Simulate( Client owner ) 
	{
		if ( TimeSinceDeployed < 0.6f )
			return;

		if ( !IsReloading )
		{
			base.Simulate( owner );
		}

		if ( IsReloading && TimeSinceReload > ReloadTime )
		{
			OnReloadFinish();
		}

		if ( IsReloading && TimeSinceReload > MathF.Max( ReloadFinishTime, ReloadTime ) )
		{
			IsReloading = false;
		}

		if (CrosshairPanel != null) {
			CrosshairPanel.Style.Opacity = !IsAiming ? 1f : 0f;
		}
	}

	public virtual void OnReloadFinish()
	{
		if ( Owner is HeistPlayer player )
		{
			var ammo = player.TakeAmmo( AmmoType, ClipSize - AmmoClip );
			if ( ammo == 0 )
				return;

			AmmoClip += ammo;
		} else if ( Owner is NpcPawn )
		{
			AmmoClip = ClipSize;
		}
	}

	// Used on NPC weapons, who do not call Simulate(cl)
	public virtual async void AsyncReload()
	{
		await Task.DelaySeconds( ReloadTime );
		OnReloadFinish();
		var diff = ReloadFinishTime - ReloadTime;
		if (diff > 0)
			await Task.DelaySeconds( diff );
		IsReloading = false;
	}

	[ClientRpc]
	public virtual void StartReloadEffects()
	{
		ViewModelEntity?.SetAnimBool( "reload", true );

		// TODO - player third person model reload
	}

	public override bool CanPrimaryAttack()
	{
		if ( !Owner.IsValid() || (Owner is Player && ((Automatic && !Input.Down( InputButton.Attack1 ) || (!Automatic && !Input.Pressed( InputButton.Attack1 ))))) || (Owner is NpcPawn && IsReloading) ) return false;

		var rate = PrimaryRate;
		if ( rate <= 0 ) return true;

		return TimeSincePrimaryAttack > (1 / rate);
	}
	public override void AttackPrimary()
	{
		TimeSincePrimaryAttack = 0;
		TimeSinceSecondaryAttack = 0;

		//
		// Tell the clients to play the shoot effects
		//
		ShootEffects();

		//
		// ShootBullet is coded in a way where we can have bullets pass through shit
		// or bounce off shit, in which case it'll return multiple results
		//
		foreach ( var tr in TraceBullet( Owner.EyePos, Owner.EyePos + Owner.EyeRot.Forward * 5000 ) )
		{
			tr.Surface.DoBulletImpact( tr );

			if ( !IsServer ) continue;
			if ( !tr.Entity.IsValid() ) continue;

			// We turn predictiuon off for this, so aany exploding effects don't get culled etc
			//
			using ( Prediction.Off() )
			{
				var damage = DamageInfo.FromBullet( tr.EndPos, Owner.EyeRot.Forward * 100, 15 )
					.UsingTraceResult( tr )
					.WithAttacker( Owner )
					.WithWeapon( this );

				tr.Entity.TakeDamage( damage );
			}
		}
	}

	[ClientRpc]
	protected virtual void ShootEffects()
	{
		Host.AssertClient();

		Particles.Create( "particles/pistol_muzzleflash.vpcf", EffectEntity, "muzzle" );

		if ( IsLocalPawn )
		{
			new Sandbox.ScreenShake.Perlin();
		}

		DoShootAnims();
	}

	protected virtual void DoShootAnims () {
		ViewModelEntity?.SetAnimBool( "fire", true );

		(ViewModelEntity as HeistViewModel)?.ApplyImpulse(Vector3.Forward * -30.5f + Vector3.Left * -1.5f + Vector3.Up * -2f);
		
		CrosshairPanel?.OnEvent( "fire" );
	}

	/// <summary>
	/// Shoot a single bullet
	/// </summary>
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

	public bool TakeAmmo( int amount )
	{
		if ( AmmoClip < amount )
			return false;

		AmmoClip -= amount;
		return true;
	}

	[ClientRpc]
	public virtual void DryFire()
	{
		// CLICK
	}

	public override void CreateViewModel()
	{
		Host.AssertClient();

		if ( string.IsNullOrEmpty( ViewModelPath ) )
			return;

		var vm = new HeistViewModel
		{
			Weapon = this,
			Position = Position,
			Owner = Owner,
			EnableViewmodelRendering = true
		};
		vm.SetModel( ViewModelPath );

		ViewModelEntity = vm;
	}

	public override void CreateHudElements()
	{
		if ( Local.Hud == null ) return;

		CrosshairPanel = new HeistCrosshair(); //new Crosshair();
		CrosshairPanel.Parent = Local.Hud;
		CrosshairPanel.AddClass( "crosshair_default" );
	}

	public bool IsUsable()
	{
		if ( AmmoClip > 0 ) return true;
		return AvailableAmmo() > 0;
	}

	public override void OnCarryStart( Entity carrier )
	{
		base.OnCarryStart( carrier );

		if ( PickupTrigger.IsValid() )
		{
			PickupTrigger.EnableTouch = false;
		}
	}
	public override void OnCarryDrop( Entity dropper )
	{
		base.OnCarryDrop( dropper );

		if ( PickupTrigger.IsValid() )
		{
			PickupTrigger.EnableTouch = true;
		}
	}

	public override void SimulateAnimator( PawnAnimator anim )
	{
		anim.SetParam( "holdtype", HoldType );
		anim.SetParam( "aimat_weight", 1.0f );
	}
}

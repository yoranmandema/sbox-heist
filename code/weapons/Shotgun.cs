using Sandbox;


[Library( "heist_shotgun", Title = "Shotgun" )]
partial class Shotgun : BaseHeistWeapon
{ 
	public override string ViewModelPath => "weapons/rust_pumpshotgun/v_rust_pumpshotgun.vmdl";
	public override float PrimaryRate => 1;
	public override float SecondaryRate => 1;
	public override AmmoType AmmoType => AmmoType.Buckshot;
	public override int ClipSize => 4;
	public override float ReloadTime => 0.5f;
	public override int Bucket => 2;
	public override int HoldType => 2;

	public override void Spawn()
	{
		base.Spawn();

		SetModel( "weapons/rust_pumpshotgun/rust_pumpshotgun.vmdl" );  
		AmmoClip = ClipSize;
	}

	public override void CreateViewModel () {
		base.CreateViewModel();

		(ViewModelEntity as HeistViewModel).AimOffset = new Vector3( -4f, 18.9f, 2.8f );
	}

	public override FiringParams FiringParams => new FiringParams( 12f, 4f, 0f, 800f, 0.15f, 0.07f, 0.05f, 10, 0.3f );

	public override string FiringSound => "rust_pumpshotgun.shoot";

	public override void AttackSecondary()
	{
		return;
		/*
		TimeSincePrimaryAttack = -0.5f;
		TimeSinceSecondaryAttack = -0.5f;

		if ( !TakeAmmo( 2 ) )
		{
			DryFire();
			return;
		}

		(Owner as AnimEntity).SetAnimBool( "b_attack", true );

		//
		// Tell the clients to play the shoot effects
		//
		DoubleShootEffects();
		PlaySound( "rust_pumpshotgun.shootdouble" );

		//
		// Shoot the bullets
		//
		for ( int i = 0; i < 20; i++ )
		{
			ShootBullet( 0.4f, 0.3f, 8.0f, 3.0f );
		}
		*/
	}

	[ClientRpc]
	protected override void ShootEffects()
	{
		Host.AssertClient();

		Particles.Create( "particles/pistol_muzzleflash.vpcf", EffectEntity, "muzzle" );
		Particles.Create( "particles/pistol_ejectbrass.vpcf", EffectEntity, "ejection_point" );

		ViewModelEntity?.SetAnimBool( "fire", true );

		if ( IsLocalPawn )
		{
			new Sandbox.ScreenShake.Perlin(1.0f, 1.5f, 2.0f);
		}

		(ViewModelEntity as HeistViewModel)?.ApplyImpulse(Vector3.Forward * -50.5f + Vector3.Left * -1.5f + Vector3.Up * 2f);

		CrosshairPanel?.CreateEvent( "Attack" );
	}

	[ClientRpc]
	protected virtual void DoubleShootEffects()
	{
		Host.AssertClient();

		Particles.Create( "particles/pistol_muzzleflash.vpcf", EffectEntity, "muzzle" );

		ViewModelEntity?.SetAnimBool( "fire_double", true );
			
		(ViewModelEntity as HeistViewModel)?.ApplyImpulse(Vector3.Forward * -50.5f + Vector3.Left * -1.5f + Vector3.Up * -10f);

		CrosshairPanel?.CreateEvent( "Attack" );

		if ( IsLocalPawn )
		{
			new Sandbox.ScreenShake.Perlin(3.0f, 3.0f, 3.0f);
		}
	}

	public override void OnReloadFinish()
	{
		IsReloading = false;

		TimeSincePrimaryAttack = 0;
		TimeSinceSecondaryAttack = 0;

		if ( AmmoClip >= ClipSize )
			return;

		if ( Owner is HeistPlayer player )
		{
			var ammo = player.TakeAmmo( AmmoType, 1 );
			if ( ammo == 0 )
				return;

			AmmoClip += ammo;

			if ( AmmoClip < ClipSize )
			{
				Reload();
			}
			else
			{
				FinishReload();
			}
		} else if ( Owner is NpcPawn )
		{
			AmmoClip++;
			if ( AmmoClip < ClipSize )
				Reload();
		}
	}

	[ClientRpc]
	protected virtual void FinishReload()
	{
		ViewModelEntity?.SetAnimBool( "reload_finished", true );
	}

	public override void SimulateAnimator( PawnAnimator anim )
	{
		anim.SetParam( "holdtype", 2 ); // TODO this is shit
		anim.SetParam( "aimat_weight", 1.0f );
	}
}

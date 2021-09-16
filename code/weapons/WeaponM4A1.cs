using Sandbox;
using System;

[Library( "heist_m4a1", Title = "M4A1" )]
partial class WeaponM4A1 : BaseHeistWeapon
{ 
	public override string ViewModelPath => "weapons/css_m4a1/css_v_m4a1.vmdl_c";

	public override AmmoType AmmoType => AmmoType.Crossbow;

	public override float BobScale => 0.5f;
	public override float SwayScale => 0.25f;

	public override float PrimaryRate => 13.3f;
	public override float SecondaryRate => 1.0f;
	public override int ClipSize => 30;
	public override float ReloadTime => 2.0f;

	public override float ReloadFinishTime => 3.5f;
	public override int Bucket => 2;
	public override int HoldType => 2;

	public override void Spawn()
	{
		base.Spawn();
		SetModel( "weapons/css_m4a1/css_w_m4a1.vmdl_c" );
		AmmoClip = this.ClipSize;
	}

	public override void CreateViewModel () {
		base.CreateViewModel();

        var vm = (ViewModelEntity as HeistViewModel);

		vm.AimOffset = new Vector3( 2f, 15f, -6f );
		vm.OverallOffset = new Vector3( -5f, -10.3f, 7f );
	}

	public override void AttackPrimary()
	{
		TimeSincePrimaryAttack = 0;
		TimeSinceSecondaryAttack = 0;

		if ( !TakeAmmo( 1 ) )
		{
			DryFire();
			return;
		}

		(Owner as AnimEntity).SetAnimBool( "b_attack", true );

		//
		// Tell the clients to play the shoot effects
		//
		ShootEffects();
		PlaySound( "rust_smg.shoot" ); // holy shit this is loud

		// PlaySound( "css_m4a1.fire" ); // holy shit this is loud

		//
		// Shoot the bullets
		//
		ShootBullet( 0.1f, 1.5f, 5.0f, 3.0f );

	}

	public override void AttackSecondary()
	{
		// Grenade lob
	}

	[ClientRpc]
	protected override void ShootEffects()
	{
		Host.AssertClient();

		Particles.Create( "particles/pistol_muzzleflash.vpcf", EffectEntity, "muzzle" );
		Particles.Create( "particles/pistol_ejectbrass.vpcf", EffectEntity, "ejection_point" );

		if ( Owner == Local.Pawn )
		{
			new Sandbox.ScreenShake.Perlin(0.5f, 4.0f, 1.0f, 0.5f);
		}

		ViewModelEntity?.SetAnimBool( "fire", true );
				
		(ViewModelEntity as HeistViewModel)?.ApplyImpulse(Vector3.Forward * -0.5f + Vector3.Left * -0.5f + Vector3.Up * 0.5f);

		CrosshairPanel?.CreateEvent( "Attack" );
		
	}

	public override void SimulateAnimator( PawnAnimator anim )
	{
		anim.SetParam( "holdtype", 2 ); // TODO this is shit
		anim.SetParam( "aimat_weight", 1.0f );
	}

}

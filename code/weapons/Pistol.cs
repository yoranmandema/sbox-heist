using Sandbox;


[Library( "heist_pistol", Title = "Pistol" )]
partial class Pistol : BaseHeistWeapon
{ 
	public override string ViewModelPath => "weapons/rust_pistol/v_rust_pistol.vmdl";

	public override float PrimaryRate => 15.0f;
	public override float SecondaryRate => 1.0f;
	public override float ReloadTime => 3.0f;

	public override bool Automatic => false;

	public override FiringParams FiringParams => new FiringParams(20f, 8f, 200f, 1000f, 0.01f, 0.04f, 0.02f);

	public override string FiringSound => "rust_pistol.shoot";

	public override int Bucket => 1;

	public override void Spawn()
	{
		base.Spawn();
		SetModel( "weapons/rust_pistol/rust_pistol.vmdl" );
	}

	/*
	public override void AttackPrimary()
	{
		TimeSincePrimaryAttack = 0;
		TimeSinceSecondaryAttack = 0;

		if ( !TakeAmmo( 1 ) )
		{
			if ( CanReload() )
			{
				Reload();
			} else
			{
				DryFire();
			}
			return;
		}

		//
		// Tell the clients to play the shoot effects
		//
		ShootEffects();
		PlaySound( "rust_pistol.shoot" );

		//
		// Shoot the bullets
		//
		ShootBullet( 0.05f, 1.5f, 25.0f, 3.0f );

	}
	*/
}

using Sandbox;

[Library( "heist_grenade", Title = "Grenade" )]
partial class Grenade : BaseHeistWeapon
{
	public override string ViewModelPath => "weapons/rust_pistol/v_rust_pistol.vmdl";

	public override float PrimaryRate => 1;
	public override int Bucket => 1;
	public override AmmoType AmmoType => AmmoType.Grenade;

	public override void Spawn()
	{
		base.Spawn();

		AmmoClip = 1;
		SetModel( "weapons/rust_pistol/rust_pistol.vmdl" );
	}

	public override void AttackPrimary()
	{
		if ( !TakeAmmo( 1 ) )
		{
			DryFire();
			return;
		}

		ShootEffects();

		if ( IsServer )
			using ( Prediction.Off() )
			{
				var grenade = new GrenadeProjectile
				{
					Position = Owner.EyePos,
					Rotation = Owner.EyeRot,
					Owner = Owner
				};

				grenade.Shoot( Owner.EyeRot.Forward, Owner.Velocity );
			}
	}

	[ClientRpc]
	protected override void ShootEffects()
	{
		Host.AssertClient();

		// if (Owner == Local.Pawn)
		// {
		//     new Sandbox.ScreenShake.Perlin(0.5f, 4.0f, 1.0f, 0.5f);
		// }

		ViewModelEntity?.SetAnimBool( "fire", true );

		(ViewModelEntity as HeistViewModel)?.ApplyImpulse( Vector3.Forward * -50.5f );

		CrosshairPanel?.OnEvent( "fire" );
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

class NpcWeaponPistol : BaseNpcWeapon
{
	public override float PrimaryRate => 3.0f;
	public override float SecondaryRate => 1.0f;
	public override float ReloadTime => 2.5f;


	public override void Spawn()
	{
		base.Spawn();
		SetModel( "weapons/rust_pistol/rust_pistol.vmdl" );
	}

	public override void AttackPrimary()
	{
		TimeSincePrimaryAttack = 0;
		TimeSinceSecondaryAttack = 0;

		if ( !TakeAmmo( 1 ) )
		{
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
		ShootBullet( 0.1f, 1.5f, 5.0f, 2.0f );

	}
}

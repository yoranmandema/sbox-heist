using Sandbox;

[Library( "heist_crossbow", Title = "Crossbow" )]
partial class Crossbow : BaseHeistWeapon
{ 
	public override string ViewModelPath => "weapons/rust_crossbow/v_rust_crossbow.vmdl";

	public override int ClipSize => 1;
	public override float PrimaryRate => 1;
	public override int Bucket => 3;
	public override AmmoType AmmoType => AmmoType.Crossbow;

	[Net]
	public bool Zoomed { get; set; }

	public override void Spawn()
	{
		base.Spawn();

		SetModel( "weapons/rust_crossbow/rust_crossbow.vmdl" );
	}

	public override void CreateViewModel()
	{
		base.CreateViewModel();

		var vm = (ViewModelEntity as HeistViewModel);

		vm.AimOffset = new Vector3( -5f, 13f, 6f );
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
			var bolt = new CrossbowBolt();
			bolt.Position = Owner.EyePos;
			bolt.Rotation = Owner.EyeRot;
			bolt.Owner = Owner;
			bolt.Velocity = Owner.EyeRot.Forward * 100;
		}
	}
	/*
public override void Simulate( Client cl )
{
	base.Simulate( cl );

	Zoomed = Input.Down( InputButton.Attack2 );
}

public override void PostCameraSetup( ref CameraSetup camSetup )
{
	base.PostCameraSetup( ref camSetup );

	if ( Zoomed )
	{
		camSetup.FieldOfView = 20;
	}
}

public override void BuildInput( InputBuilder owner ) 
{
	if ( Zoomed )
	{
		owner.ViewAngles = Angles.Lerp( owner.OriginalViewAngles, owner.ViewAngles, 0.2f );
	}
}
*/

	[ClientRpc]
	protected override void ShootEffects()
	{
		Host.AssertClient();

		if ( Owner == Local.Pawn )
		{
			new Sandbox.ScreenShake.Perlin( 0.5f, 4.0f, 1.0f, 0.5f );
		}

		ViewModelEntity?.SetAnimBool( "fire", true );
			
		(ViewModelEntity as HeistViewModel)?.ApplyImpulse(Vector3.Forward * -50.5f);

		CrosshairPanel?.CreateEvent( "Attack" );
	}
}

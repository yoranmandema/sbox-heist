using Sandbox;

[Library( "heist_npcpointmaker", Title = "NPC Point Maker" )]
partial class NpcPointMaker : BaseHeistWeapon
{
	public override string ViewModelPath => "weapons/rust_pistol/v_rust_pistol.vmdl";

	public override float PrimaryRate => 5;
	public override int Bucket => 3;
	public override AmmoType AmmoType => AmmoType.Grenade;

	public override void Spawn()
	{
		base.Spawn();

		AmmoClip = 100;
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
				var startPos = Owner.EyePos;
				var dir = Owner.EyeRot.Forward;
				var tr = Trace.Ray( startPos, startPos + dir * 10000 )
					.WorldOnly()
					.Run();
				if ( tr.Hit )
				{
					var point = NpcPoint.CreatePoint( tr.EndPos );
				}
			}
	}

	public override void Reload()
	{
		NpcPoint.All.Clear();
	}

	public override void Simulate( Client owner )
	{
		base.Simulate(owner);

		if ( NpcPoint.nav_drawpoints )
		{
			using ( Sandbox.Debug.Profile.Scope( "Draw Points" ) )
			{
				var arr = NpcPoint.All;
				foreach ( var p in arr )
				{
					p.DebugDraw( 0.1f, 0.5f );
				}
			}
		}
	}

	[ClientRpc]
	protected override void ShootEffects()
	{
		Host.AssertClient();

		ViewModelEntity?.SetAnimBool( "fire", true );

		(ViewModelEntity as HeistViewModel)?.ApplyImpulse( Vector3.Forward * -50.5f );

		CrosshairPanel?.OnEvent( "fire" );
	}
}

using Sandbox;

[Library( "heist_npcspawner", Title = "NPC Spawner" )]
partial class NPCSpawner : BaseHeistWeapon
{
	public override string ViewModelPath => "weapons/rust_pistol/v_rust_pistol.vmdl";

	public override float PrimaryRate => 1;
	public override int Bucket => 1;
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
					.Ignore( Owner )
					.Run();
				if (tr.Hit)
				{
					var pawn = new NPCPawn
					{
						Position = tr.EndPos,
						Rotation = Owner.Rotation,
					};
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

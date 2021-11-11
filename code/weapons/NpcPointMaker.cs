using Sandbox;
using System.Collections.Generic;
using System;

[Library( "heist_npcpointmaker", Title = "NPC Point Maker" )]
partial class NpcPointMaker : BaseHeistWeapon
{
	public override string ViewModelPath => "weapons/rust_pistol/v_rust_pistol.vmdl";

	public override float PrimaryRate => 5;
	public override float SecondaryRate => 5;
	public override bool Automatic => false;
	public override int Bucket => 3;
	public override AmmoType AmmoType => AmmoType.Grenade;
	public override bool AllowAim => false;

	public override void Spawn()
	{
		base.Spawn();

		AmmoClip = 100;
		SetModel( "weapons/rust_pistol/rust_pistol.vmdl" );
	}

	public override void AttackPrimary()
	{
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

	public override void AttackSecondary()
	{
		ShootEffects();

		if ( IsServer )
			using ( Prediction.Off() )
			{

				var startPos = Owner.EyePos;
				var dir = Owner.EyeRot.Forward;
				var tr = Trace.Ray( startPos, startPos + dir * 10000 )
					.Ignore( Owner )
					.Run();
				if ( tr.Hit )
				{
					List<NpcPoint> remove = new List<NpcPoint>();
					foreach (var p in NpcPoint.All)
					{
						if (p.GetPosition().Distance(tr.EndPos) <= 16)
						{
							remove.Add( p );
						}
					}
					foreach (var p in remove)
					{
						NpcPoint.All.Remove( p );
					}
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
	}

	[ClientRpc]
	protected override void ShootEffects()
	{
		Host.AssertClient();

		ViewModelEntity?.SetAnimBool( "fire", true );

		(ViewModelEntity as HeistViewModel)?.ApplyImpulse( Vector3.Forward * -50.5f );

		CrosshairPanel?.CreateEvent( "Attack" );
	}
}

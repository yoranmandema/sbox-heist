using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox;
class NpcGunner : NpcPawn
{
	protected Entity target { get; set; }
	float PatrolSpeed = 80f;
	float CombatSpeed = 200f;

	public override void Spawn()
	{
		base.Spawn();

		Speed = PatrolSpeed;
	}
	protected override void NpcThink()
	{
		if ( Steer == null )
		{
			if ( target != null )
			{
				Steer = new NavSteer();
				Steer.Target = target.Position;
			} else
			{
				Steer = new Sandbox.Nav.Wander();
			}
		} else
		{
			if ( target != null )
			{
				Steer.Target = target.Position;
			}
		}
	}

	public override void TakeDamage( DamageInfo info )
	{
		base.TakeDamage( info );
		if (target == null && info.Attacker is HeistPlayer)
		{
			target = info.Attacker;
			Steer = new NavSteer();
			Steer.Target = target.Position;
		}
	}
}

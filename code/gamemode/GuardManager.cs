using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


class GuardManager
{
	public List<NpcPawn> Guards = new List<NpcPawn>();


	[ServerCmd( "heist_populate" )]
	public static void Populate(int amt)
	{
		for (int i = 0; i < amt; i++ )
		{
			var point = NpcPoint.All[Rand.Int( 0, NpcPoint.All.Count - 1 )];
			var pos = NavMesh.GetPointWithinRadius( point.GetPosition(), 0, 128f );
			var pos2 = NavMesh.GetPointWithinRadius( point.GetPosition(), 128f, 512f );
			if (pos.HasValue && pos2.HasValue)
			{
				var pawn = new NpcGunner
				{
					Position = (Vector3)pos,
					Rotation = Rotation.FromYaw(Rand.Float(360f)),
				};
				var path = new Sandbox.Nav.Patrol();
				path.PatrolStart = (Vector3)pos;
				path.PatrolEnd = (Vector3)pos2;
				path.PatrolDelay = Rand.Float(10f) + 20f;
				pawn.Patrol = path;	
			}
		}
	}
}


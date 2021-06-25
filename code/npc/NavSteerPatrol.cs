
namespace Sandbox.Nav
{

	public class Patrol : NavSteer
	{

		public Vector3 PatrolStart;
		public Vector3 PatrolEnd;
		public float PatrolDelay;

		bool PatrolPaused;
		TimeSince TimeSincePatrolStop;

		public Patrol()
		{

		}

		public override void Tick( Vector3 position )
		{

			if ( !PatrolPaused )
				base.Tick(position);


			if ( Path.IsEmpty && !PatrolPaused )
            {
				// Start taking a break from patrolling.
				PatrolPaused = true;
				TimeSincePatrolStop = 0;
            } else if ( PatrolPaused && TimeSincePatrolStop > PatrolDelay )
			{
				// Break time's over lad!
				PatrolPaused = false;
				var dist1 = position.Distance(PatrolStart);
				var dist2 = position.Distance(PatrolEnd);
				if (dist1 <= dist2)
				{
					// we're at the start. go to the end
					var t = NavMesh.GetClosestPoint(PatrolEnd);
					if (t.HasValue)
						Target = t.Value;
				}
				else
				{
					// vice versa
					var t = NavMesh.GetClosestPoint(PatrolStart);
					if (t.HasValue)
						Target = t.Value;
				}
			}
		}
	}

}

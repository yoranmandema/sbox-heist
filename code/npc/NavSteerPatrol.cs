
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

			Path.DebugDraw( 0.1f, 0.1f );

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

			/*
			var draw = Sandbox.Debug.Draw.ForSeconds( 0.1f );
			var lift = Vector3.Up * 2;
			draw.WithColor( Color.White.WithAlpha( 0.1f ) ).Circle( lift + PatrolStart, Vector3.Up, 20.0f );
			draw.WithColor( Color.White.WithAlpha( 0.1f ) ).Circle( lift + PatrolEnd, Vector3.Up, 20.0f );

			int i = 0;
			var lastPoint = Vector3.Zero;
			foreach ( var point in this.Path.Points )
			{
				if ( i > 0 )
				{
					draw.WithColor( i == 1 ? Color.Green.WithAlpha( 0.1f ) : Color.Cyan.WithAlpha( 0.1f ) ).Arrow( lastPoint + lift, point + lift, Vector3.Up, 5.0f );
				}

				lastPoint = point;
				i++;
			}
			*/
		}
	}

}

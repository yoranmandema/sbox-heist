
namespace Sandbox.Nav
{

	public class WanderPoint : NavSteer
	{
		public Vector3 Position { get; set; }
		public float MinRadius { get; set; } = 200;
		public float MaxRadius { get; set; } = 500;

		public WanderPoint()
		{

		}

		public override void Tick( Vector3 position )
		{
			base.Tick( position );

			if ( Path.IsEmpty )
			{
				FindNewTarget();
			}
		}

		public virtual bool FindNewTarget()
		{
			var t = NavMesh.GetPointWithinRadius( Position, MinRadius, MaxRadius );
			if ( t.HasValue )
			{
				Target = t.Value;
			}

			return t.HasValue;
		}

	}

}

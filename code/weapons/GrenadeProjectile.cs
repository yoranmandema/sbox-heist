using System.Collections.Generic;
using System.Linq;
using Sandbox;


[Library( "grenade_projectile" )]
partial class GrenadeProjectile : ModelEntity
{

	public float Size => 3f;
	public float Bounce => 1f;

	public float BounceThreshold => 250f;
	public float RollDrag = 1.5f;

	public float FuseTime => 5f;
	public float InitialVelocity => 1000f;
	public float Damage => 250f;
	public float UpForce => 0.1f;

	public float ExplosionRadius => 150f;
	public float ExplosionForce = 2500f;

	[Net] public bool IsRolling { get; set; }

	[Net] public Vector3 SimVelocity { get; set; }

	[Net] public Vector3 Direction { get; set; } = Vector3.Forward;
	[Net] public bool IsSimulating { get; set; } = true;

	private Vector3 currentPosition;
	private Vector3 lastPosition;

	private float time;

	public override void Spawn()
	{
		base.Spawn();

		PhysicsEnabled = false;

		SetModel( "models/ball/ball.vmdl" );

		Scale = 0.2f;
	}

	public void Shoot( Vector3 direction, Vector3 ownerVelocity )
	{
		Direction = direction + Vector3.Up * UpForce;

		Direction = Direction.Normal;

		SimVelocity = Direction * InitialVelocity + ownerVelocity;

		lastPosition = Position;

		time = 0;
	}

	[Event.Tick.Server]
	public virtual void Tick()
	{
		if ( !IsServer )
			return;

		if ( IsSimulating && !IsRolling )
		{
			SimVelocity += PhysicsWorld.Gravity * Time.Delta;
		}

		currentPosition = Position;

		DebugOverlay.Line( lastPosition, currentPosition, Color.Yellow, 5f );
			
		if ( IsRolling )
		{
			DoRoll();
		}

		var start = Position;
		var end = start + SimVelocity * Time.Delta;


		var tr = Trace.Ray( start, end )
				.UseHitboxes()
				//.HitLayer( CollisionLayer.Water, !InWater )
				.Ignore( Owner )
				.Ignore( this )
				.Size( Size * 0.5f )
				.Run();


		if ( tr.Hit )
		{
			DebugOverlay.Text(tr.EndPos, $"{tr.Surface.Elasticity}", Color.Cyan, 5f);

			DebugOverlay.Line( tr.EndPos, tr.EndPos + Vector3.Reflect( SimVelocity.Normal, tr.Normal ) * 10f, Color.Cyan, 5f );

			bool canRoll = SimVelocity.Length < System.MathF.Max(tr.Surface.BounceThreshold, BounceThreshold);
			canRoll = canRoll || (Vector3.Reflect( SimVelocity.Normal, tr.Normal ).Dot(tr.Normal) < 0.35f && tr.Normal.z > 0.1f);


			if ( canRoll && !IsRolling )
			{
				IsRolling = true;

				var projectedVel = Vector3.VectorPlaneProject(SimVelocity, tr.Normal);

				SimVelocity = projectedVel * projectedVel.Normal.Dot(SimVelocity.Normal);
			} else {
				DoBounce( tr );

				DebugOverlay.Line( tr.EndPos, tr.EndPos + tr.Normal * 10f, Color.Red, 5f );
			}
		}
		else
		{
			Position = end;
		}

		if ( time >= FuseTime )
		{
			DoExplosion( Position );
		}

		time += Time.Delta;

		lastPosition = currentPosition;
	}

	private void ApplyDamping( ref Vector3 value, float damping )
	{
		var magnitude = value.Length;

		if ( magnitude != 0 )
		{
			var drop = magnitude * damping * Time.Delta;
			value *= System.MathF.Max( magnitude - drop, 0 ) / magnitude;
		}
	}

	private void DoBounce( TraceResult tr )
	{
		SimVelocity = Vector3.Reflect( SimVelocity, tr.Normal ) * ((Bounce + tr.Surface.Elasticity)/ 2f);

		Position = tr.EndPos + tr.Normal * Size * 0.5f ;
	}

	private void DoRoll()
	{
		var groundTrace = Trace.Ray( Position, Position + Vector3.Down * Size * 0.5f )
			.UseHitboxes()
			//.HitLayer( CollisionLayer.Water, !InWater )
			.Ignore( Owner )
			.Ignore( this )
			.Size( Size * 0.25f )
			.Run();

		if (!groundTrace.Hit) {
			IsRolling = false;

			return;
		}

		DebugOverlay.Line( groundTrace.EndPos, groundTrace.EndPos + groundTrace.Normal * 10f, Color.Green, 5f );
		DebugOverlay.Line( groundTrace.StartPos, groundTrace.EndPos, Color.Green * 0.5f, 5f );

		SimVelocity = Vector3.VectorPlaneProject(SimVelocity, groundTrace.Normal);

		SimVelocity += Vector3.VectorPlaneProject(PhysicsWorld.Gravity, groundTrace.Normal) * Time.Delta;

		var simVel = SimVelocity;

		ApplyDamping(ref simVel, RollDrag * groundTrace.Surface.Friction);

		SimVelocity = simVel;

		DebugOverlay.Line( groundTrace.EndPos, groundTrace.EndPos + SimVelocity, Color.Orange, 0 );

		DebugOverlay.Text(groundTrace.EndPos, $"{groundTrace.Surface.Friction}", Color.Orange, 5f);

	}

	public void DoExplosion( Vector3 position )
	{
		Explosion.Create( this, position, ExplosionRadius, Damage, ExplosionForce );

		Delete();
	}
}

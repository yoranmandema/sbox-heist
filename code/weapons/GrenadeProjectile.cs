using System.Collections.Generic;
using System.Linq;
using Sandbox;


[Library( "grenade_projectile" )]
partial class GrenadeProjectile : ModelEntity
{

	public float Size => 3f;
	public float Bounce => 0.1f;

	public float BounceThreshold => 250f;

	public float PlayerBounce => 0.1f;

	public float RollDrag = 1.5f;

	public float FuseTime => 3f;
	public float InitialVelocity => 1000f;
	public float Damage => 100f;
	public float UpForce => 0.1f;

	public float ImpactDamage => 5f;
	public float ImpactDamageThreshold => 350f;

	public float ExplosionRadius => 150f;
	public float ExplosionForce => 1000f;

	[Net] public bool IsRolling { get; set; }

	// [Net] public Vector3 Velocity { get; set; }

	[Net] public Vector3 Direction { get; set; } = Vector3.Forward;
	[Net] public bool IsSimulating { get; set; } = true;
	[Net] public Entity WeaponEntity { get; set; }

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

	public void Shoot(Entity weaponEnt, Vector3 direction, Vector3 ownerVelocity )
	{
		WeaponEntity = weaponEnt;

		Direction = direction + Vector3.Up * UpForce;

		Direction = Direction.Normal;

		Velocity = Direction * InitialVelocity + ownerVelocity;

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
			Velocity += PhysicsWorld.Gravity * Time.Delta;
		}

		currentPosition = Position;
			
		if ( IsRolling )
		{
			DoRoll();
		}

		var start = Position;
		var end = start + Velocity * Time.Delta;

		var tr = Trace.Ray( start, end )
				.UseHitboxes()
				//.HitLayer( CollisionLayer.Water, !InWater )
				.Ignore( Owner )
				.Ignore( this )
				.Size( Size * 0.5f )
				.Run();


		if ( tr.Hit )
		{
			bool canRoll = Velocity.Length < System.MathF.Max(tr.Surface.BounceThreshold, BounceThreshold);
			canRoll = canRoll || (Vector3.Reflect( Velocity.Normal, tr.Normal ).Dot(tr.Normal) < 0.35f && tr.Normal.z > 0.1f);


			if ( canRoll && !IsRolling )
			{
				IsRolling = true;

				var projectedVel = Vector3.VectorPlaneProject(Velocity, tr.Normal);

				Velocity = projectedVel * projectedVel.Normal.Dot(Velocity.Normal);
			} else {

				DoImpactDamage ( tr );

				DoBounce( tr );
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

	private void DoImpactDamage ( TraceResult tr ) {
		if (!tr.Entity.IsValid()) return;
		if (Velocity.Length < ImpactDamageThreshold) return;

		var damageInfo = DamageInfo.Generic(ImpactDamage)
			.UsingTraceResult( tr )
			.WithFlag(DamageFlags.PhysicsImpact)
			.WithAttacker( Owner )
			.WithWeapon( WeaponEntity );

		tr.Entity.TakeDamage(damageInfo);
	}

	private void DoBounce( TraceResult tr )
	{
		float appliedBounce = ((Bounce + tr.Surface.Elasticity)/ 2f);

		if (tr.Entity is Player player) {
			appliedBounce = PlayerBounce;
		}

		Velocity = Vector3.Reflect( Velocity, tr.Normal ) * appliedBounce;

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


		Velocity = Vector3.VectorPlaneProject(Velocity, groundTrace.Normal);

		Velocity += Vector3.VectorPlaneProject(PhysicsWorld.Gravity, groundTrace.Normal) * Time.Delta;

		var simVel = Velocity;

		ApplyDamping(ref simVel, RollDrag * groundTrace.Surface.Friction);

		Velocity = simVel;
	}

	public void DoExplosion( Vector3 position )
	{
		Explosion.Create(WeaponEntity)
			.At(position)
			.WithDamage(Damage)
			.WithRadius(ExplosionRadius)
			.WithForce(ExplosionForce)
			.Explode();

		Delete();
	}
}

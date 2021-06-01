using Sandbox;


[Library( "grenade_projectile" )]
partial class GrenadeProjectile : ModelEntity
{

	public float Size => 3f;
	public float Bounce => 0.5f;
	public float FuseTime => 3f;
	public float InitialVelocity => 1000f;
	public float Damage => 250f;

	public float UpForce => 0.1f;

	[Net] public Vector3 SimVelocity {get; set;}

	[Net] public Vector3 Direction {get; set;} = Vector3.Forward;
	[Net] public bool IsSimulating {get; set;} = true;

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

	public void Shoot (Vector3 direction, Vector3 ownerVelocity) {
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

		if (IsSimulating) { 
			SimVelocity += PhysicsWorld.Gravity * Time.Delta;
		}

		currentPosition = Position;

		DebugOverlay.Line(lastPosition, currentPosition, Color.Yellow, 5f);

		var start = Position;
		var end = start + SimVelocity * Time.Delta;

		var tr = Trace.Ray( start, end )
				.UseHitboxes()
				//.HitLayer( CollisionLayer.Water, !InWater )
				.Ignore( Owner )
				.Ignore( this )
				.Size( Size )
				.Run();

		if ( tr.Hit )
		{
			//
			// Surface impact effect
			//
			// tr.Normal = Rotation.Forward * -1;
			tr.Surface.DoBulletImpact( tr );	

			if (-SimVelocity.Length < tr.Surface.BounceThreshold) {
				SimVelocity = Vector3.Reflect(SimVelocity, tr.Normal) * Bounce;

				if (SimVelocity.Length < 25f) {
					IsSimulating = false;

					SimVelocity = Vector3.Zero;
				}
			} else {
				DebugOverlay.Line(tr.EndPos, tr.EndPos + tr.Normal * 10f, Color.Red, 5f);
			}

			Position = tr.EndPos + tr.Normal * Size * 0.5f * Bounce;


		}
		else
		{
			Position = end;
		}

		DebugOverlay.ScreenText(new Vector2(100,100), 0, Color.White, $"{time}");

		if (time >= FuseTime) {
			DoExplosion(tr);
		}

		time += Time.Delta;

		lastPosition = currentPosition;
	}

	public void DoExplosion (TraceResult tr) {
		var damage = DamageInfo.Explosion(Position, Vector3.One * 10000f, Damage)
				.UsingTraceResult( tr )
				.WithAttacker( Owner )
				.WithWeapon( this );

		Particles.Create( "particles/explosion_fireball.vpcf", Position);
		Particles.Create( "particles/explosion_flare.vpcf", Position);
		Particles.Create( "particles/explosion_smoke.vpcf", Position);

		Log.Info("" + damage.Damage);

		Delete();
	}
}

using Sandbox;
using System.Linq;
using System.Collections.Generic;

partial class Explosion : AnimEntity
{
	[Net] public Entity Instigator {get; set;}
	[Net] public float BaseDamage {get; set;} = 50f;
	[Net] public float Force {get; set;} = 250f;
	[Net] public float ShakeAmount {get; set;} = 3f;
	[Net] public float ShakeDuration {get; set;} = 1f;
	[Net] public float ShakeDistance {get; set;} = 800f;
	[Net] public float Radius {get; set;} = 150f;
	[Net] public Vector3 TargetPosition {get; set;}


	public Explosion () {

	}

    public static Explosion Create (Entity instigator) {
		Host.AssertServer();

		var explosion = new Explosion();

		explosion.Instigator = instigator;

		return explosion;        
    }

	public Explosion At (Vector3 position) {
		Position = position;

		return this;
	}

	public Explosion WithRadius (float radius) {
		Radius = radius;

		return this;
	}

	public Explosion WithDamage (float damage) {
		BaseDamage = damage;

		return this;
	}

	public Explosion WithForce (float force) {
		Force = force;

		return this;
	}

	public Explosion WithShake (float amount, float distance = 400f, float duration = 1f) {
		ShakeAmount = amount;
		ShakeDistance = distance;
		ShakeDuration = duration;

		return this;
	}

    private TraceResult DoExplosionTrace (Vector3 start, Vector3 end) {
		return Trace.Ray( start, end )
			.UseHitboxes()
			// .Ignore( Instigator )
			.Run();
	}

	[ServerCmd]
	public void Explode () {
		Host.AssertServer();

		var ents = new List<Entity>(Physics.GetEntitiesInSphere(Position, Radius));

		var affectedEnts = new List<Entity>(ents.Count);

		foreach (var ent in ents) {
			// if (ent == Instigator) continue;
			if (ent.IsWorld) continue;

			TraceResult tr = default;

			Entity root = ent.Root ?? ent;

			// Trace to EyePos if entity is Player
			if (root is Player playerEnt) {
				tr = DoExplosionTrace(Position, playerEnt.EyePos);
			}
			// Apply force if entity has physics body
			else if (root is ModelEntity modelEnt && modelEnt.PhysicsBody.IsValid()) {
				tr = DoExplosionTrace(Position, modelEnt.PhysicsBody.MassCenter);
			} 
			else {
				tr = DoExplosionTrace(Position, root.EyePos );
			}

			if (!tr.Entity.IsValid()) continue;

			var damageInfo = DamageInfo.Explosion(tr.EndPos, tr.Direction * Force, BaseDamage * (1f - tr.Fraction))
				.UsingTraceResult( tr )
				.WithAttacker( Instigator.Owner )
				.WithWeapon( Instigator );

			tr.Entity.TakeDamage( damageInfo );

			affectedEnts.Add(root);
		}

		DoEffects(Position);

		DeleteAsync(10f);
	}

	[ClientRpc]
	protected virtual void DoEffects(Vector3 position)
	{
		Host.AssertClient();
		
		// Particles.Create( "particles/explosion_fireball.vpcf", position);
		Particles.Create( "particles/explosion_flare.vpcf", position);
		Particles.Create( "particles/explosion_smoke.vpcf", position);

		var distance = position.Distance(Local.Client.Pawn.EyePos);

		if (distance < ShakeDistance) {
			var shake = new Sandbox.ScreenShake.Perlin(ShakeDuration, 2f, ShakeAmount * (1 - distance / ShakeDistance) * 4f, 0.5f);
		}
	}
}
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

	public virtual IEnumerable<TraceResult> DoExplosionTrace( Vector3 end)
	{
		var startPos = Position;
		var maxIterations = 3f;
		var i = 0;
		var realEndPos = startPos + (end - startPos).Normal * Radius;

		TraceResult tr = default;
		Entity lastEnt = null;

		while (startPos != realEndPos && i < maxIterations) {
			tr = Trace.Ray( startPos, realEndPos )
					.UseHitboxes()
					.Ignore(lastEnt)
					.Run();

			var isValid = tr.Entity.IsValid();
			
			// Ignore trace  if we did not hit an entity or if we hit the world
			if (!tr.Hit || !isValid || (isValid && tr.Entity.IsWorld)) {					
				break;
			}

			lastEnt = tr.Entity;
			startPos = tr.EndPos;
			i++;

			yield return tr;
		}
	}

	public static Vector3 NearestPointOnLine(Vector3 start, Vector3 end, Vector3 point)
	{
		var lineDir = (end - start).Normal;
		var v = point - start;
		var d = System.Math.Clamp(lineDir.Dot(v), 0, start.Distance(end));
		var linePos = start + lineDir * d;

		return linePos;
	}

	[ServerCmd]
	public void Explode () {
		Host.AssertServer();

		var ents = new List<Entity>(Physics.GetEntitiesInSphere(Position, Radius));

		var affectedEnts = new List<Entity>(ents.Count);

		foreach (var ent in ents) {
			if (ent == Instigator || ent.IsWorld) continue;

			Vector3 traceEndPos = ent.EyePos;

			// Trace to closest point along player's torso/head region if entity is player
			if (ent is Player playerEnt) {
				traceEndPos = NearestPointOnLine(playerEnt.GetBoneTransform(playerEnt.GetBoneIndex("pelvis")).Position, playerEnt.GetBoneTransform(playerEnt.GetBoneIndex("head")).Position, Position);
			}
			// Trace to mass center if entity has a physics body
			else if (ent is ModelEntity modelEnt && modelEnt.PhysicsBody.IsValid()) {
				traceEndPos = modelEnt.PhysicsBody.MassCenter;
			} 

			foreach (var trace in DoExplosionTrace(traceEndPos)) {

				// Skip entity if we already affected it
				if (affectedEnts.Contains(trace.Entity)) continue;

				var fraction = (Radius - trace.EndPos.Distance(Position)) / Radius;

				var damageInfo = DamageInfo.Explosion(Position, trace.Direction * Force, BaseDamage * fraction)
					.UsingTraceResult( trace )
					.WithAttacker( Instigator.Owner )
					.WithWeapon( Instigator );

				trace.Entity.TakeDamage( damageInfo );

				// Apply force to player
				if (trace.Entity is Player ply) {
					ply.Velocity += trace.Direction * fraction * Force / ply.PhysicsBody.Mass * 1000f;
				}

				affectedEnts.Add(trace.Entity);
			}
		}

		DoEffects(Position);

		DeleteAsync(1f);
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
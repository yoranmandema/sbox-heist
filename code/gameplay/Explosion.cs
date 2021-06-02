using Sandbox;
using System.Linq;
using System.Collections.Generic;

partial class Explosion 
{
    private static TraceResult DoExplosionTrace (Entity instigator, Vector3 start, Vector3 end) {
		return Trace.Ray( start, end )
						.UseHitboxes()
						.Ignore( instigator )
						.Run();
	}

    public static void Create (Entity instigator, Vector3 position, float radius, float baseDamage, float force) {
        var ents = new List<Entity>(Physics.GetEntitiesInSphere(position, radius));
		var affectedEnts = new List<Entity>(ents.Count);

		foreach (var ent in ents) {
			if (ent == instigator) continue;
			if (ent.IsWorld) continue;

			TraceResult tr = default;

			Entity root = ent.Root ?? ent;

			// Trace to EyePos if entity is Player
			if (root is Player playerEnt) {
				tr = DoExplosionTrace(instigator, position, playerEnt.EyePos );
			}
			// Trace to root bone if entity is AnimEntity
			else if (root is AnimEntity animEnt) {
				tr = DoExplosionTrace(instigator, position, animEnt.Root.Position );
			} 
			// Apply force if entity has physics body
			else if (root is ModelEntity modelEnt && modelEnt.PhysicsBody.IsValid()) {
				tr = DoExplosionTrace(instigator, position, modelEnt.PhysicsBody.MassCenter);
			} 
			else {
				tr = DoExplosionTrace(instigator, position, root.EyePos );
			}

			if (!tr.Entity.IsValid()) continue;

			var damageInfo = DamageInfo.Explosion(tr.EndPos, tr.Direction * force, baseDamage * (1f - tr.Fraction))
				.UsingTraceResult( tr )
				.WithAttacker( instigator.Owner )
				.WithWeapon( instigator );

			tr.Entity.TakeDamage( damageInfo );

			affectedEnts.Add(root);
		}

		Particles.Create( "particles/explosion_fireball.vpcf", position);
		Particles.Create( "particles/explosion_flare.vpcf", position);
		Particles.Create( "particles/explosion_smoke.vpcf", position);
    }
}
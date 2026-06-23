using Godot;
using Kuros.Core.Events;

namespace Kuros.Core
{
    public static class DamageDispatcher
    {
        public static bool DealDamage(Node target, float damage,
            Vector2? origin = null, GameActor? attacker = null,
            DamageSource source = DamageSource.DirectAttack)
        {
            Node? current = target;
            while (current != null)
            {
                if (current.IsInGroup("player"))
                {
                    current = current.GetParentOrNull<Node>();
                    continue;
                }

                if (current is GameActor actor)
                {
                    if (attacker == null) { current = current.GetParentOrNull<Node>(); continue; }
                    if (IsSameFaction(attacker, actor)) { current = current.GetParentOrNull<Node>(); continue; }
                    DealToGameActor(actor, damage, origin, attacker, source);
                    return true;
                }

                if (current.HasMethod("TakeDamage"))
                {
                    DealViaCall(current, damage, attacker);
                    return true;
                }

                current = current.GetParentOrNull<Node>();
            }

            return false;
        }

        private static void DealToGameActor(GameActor actor, float damage,
            Vector2? origin, GameActor? attacker, DamageSource source)
        {
            int intDamage = Mathf.RoundToInt(damage);
            if (intDamage <= 0) return;
            actor.TakeDamage(intDamage, origin, attacker, source);
        }

        private static void DealViaCall(Node target, float damage, GameActor? attacker)
        {
            if (damage <= 0f) return;
            int intDamage = Mathf.RoundToInt(damage);
            target.Call("TakeDamage", damage);
            GameActor.BroadcastDamage(null, attacker, intDamage);
        }

        public static void DealDamageFromArea(Area2D area, float damage, GameActor? attacker)
        {
            var shapeNode = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (shapeNode?.Shape == null) return;

            var spaceState = area.GetWorld2D()?.DirectSpaceState;
            if (spaceState == null) return;

            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = shapeNode.Shape,
                Transform = shapeNode.GlobalTransform,
                CollisionMask = area.CollisionMask == 0 ? uint.MaxValue : area.CollisionMask,
                CollideWithAreas = true,
                CollideWithBodies = true
            };
            var results = spaceState.IntersectShape(query, 32);

            foreach (var result in results)
            {
                var collider = result["collider"].As<Node>();
                if (collider == null) continue;

                if (BelongsToActor(collider, attacker)) continue;

                if (DealDamage(collider, damage, area.GlobalPosition, attacker, DamageSource.DirectAttack))
                    return;
            }
        }

        private static bool BelongsToActor(Node node, GameActor? actor)
        {
            if (actor == null) return false;

            Node? current = node;
            while (current != null)
            {
                if (current == actor) return true;
                current = current.GetParentOrNull<Node>();
            }
            return false;
        }

        private static bool IsSameFaction(GameActor a, GameActor b)
        {
            bool aIsEnemy = a.IsInGroup("enemies");
            bool bIsEnemy = b.IsInGroup("enemies");
            bool aIsPlayer = a.IsInGroup("player");
            bool bIsPlayer = b.IsInGroup("player");
            return (aIsEnemy && bIsEnemy) || (aIsPlayer && bIsPlayer);
        }
    }
}

using Godot;
using Kuros.Core.Events;

namespace Kuros.Core
{
    [System.Flags]
    public enum TargetableFactions
    {
        None = 0,
        Player = 1 << 0,
        Enemy = 1 << 1,
        WorldItem = 1 << 2,
        All = Player | Enemy | WorldItem
    }

    public static class DamageDispatcher
    {
        public static bool DealDamage(Node target, float damage,
            Vector2? origin = null, GameActor? attacker = null,
            DamageSource source = DamageSource.DirectAttack,
            TargetableFactions allowedFactions = TargetableFactions.All)
        {
            Node? current = target;
            while (current != null)
            {
                var faction = GetFaction(current);
                if (faction != TargetableFactions.None && !allowedFactions.HasFlag(faction))
                {
                    current = current.GetParentOrNull<Node>();
                    continue;
                }

                if (current is GameActor actor)
                {
                    if (attacker == null) { current = current.GetParentOrNull<Node>(); continue; }
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

        public static void DealDamageFromArea(Area2D area, float damage, GameActor? attacker,
            TargetableFactions allowedFactions = TargetableFactions.All)
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

            var damaged = new System.Collections.Generic.HashSet<ulong>();
            foreach (var result in results)
            {
                var collider = result["collider"].As<Node>();
                if (collider == null) continue;

                if (BelongsToActor(collider, attacker)) continue;

                var root = ResolveDamageReceiver(collider, allowedFactions);
                if (root == null || !damaged.Add(root.GetInstanceId())) continue;

                DealDamage(collider, damage, area.GlobalPosition, attacker,
                    DamageSource.DirectAttack, allowedFactions);
            }
        }

        public static Node? ResolveDamageReceiver(Node target, TargetableFactions allowedFactions)
        {
            Node? current = target;
            while (current != null)
            {
                var faction = GetFaction(current);
                if (faction != TargetableFactions.None && !allowedFactions.HasFlag(faction))
                {
                    current = current.GetParentOrNull<Node>();
                    continue;
                }

                if (current is GameActor) return current;
                if (current.HasMethod("TakeDamage")) return current;

                current = current.GetParentOrNull<Node>();
            }
            return null;
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

        private static TargetableFactions GetFaction(Node node)
        {
            if (node.IsInGroup("player")) return TargetableFactions.Player;
            if (node.IsInGroup("enemies")) return TargetableFactions.Enemy;
            if (node.IsInGroup("world_items")) return TargetableFactions.WorldItem;
            return TargetableFactions.None;
        }
    }
}

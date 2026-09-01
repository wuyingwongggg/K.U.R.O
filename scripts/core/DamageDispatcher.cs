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
            TargetableFactions allowedFactions = TargetableFactions.All,
            bool allowSelfDamage = false,
            Area2D? attackerArea = null,
            Vector2? attackDirection = null,
            bool bypassDirectionCheck = false)
        {
            Node? current = target;
            while (current != null)
            {
                var faction = GetFaction(current);
                if (faction != TargetableFactions.None && !allowedFactions.HasFlag(faction) && !AcceptsAnyAttack(current))
                {
                    current = current.GetParentOrNull<Node>();
                    continue;
                }

                // 目标侧方向过滤：非接收方向的攻击拒绝（返回 false → 攻击方视为未命中 → 子弹/光束穿透）。
                // 优先用攻击方向向量（命中点可能已深入目标内部，位置差丢失来源侧信息）；无则回退 origin 位置差。
                // bypassDirectionCheck：全方位区域效果（爆炸）无视方向性屏障的方向限制
                if (!bypassDirectionCheck && current is IDirectionalDamageReceiver dirReceiver)
                {
                    bool accepted = attackDirection.HasValue
                        ? dirReceiver.AcceptsAttackFromDirection(attackDirection.Value)
                        : origin.HasValue && dirReceiver.AcceptsAttackFrom(origin.Value);
                    if (!accepted)
                    {
                        return false;
                    }
                }

                if (current is GameActor actor)
                {
                    if (attacker == null) { current = current.GetParentOrNull<Node>(); continue; }
                    if (!allowSelfDamage && BelongsToActor(current, attacker)) { current = current.GetParentOrNull<Node>(); continue; }
                    if (attackerArea != null && !actor.IsHitByArea(attackerArea)) return false;
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
            TargetableFactions allowedFactions = TargetableFactions.All,
            bool allowSelfDamage = false)
        {
            var damaged = new System.Collections.Generic.HashSet<ulong>();

            // 玩家：直接用 area.OverlapsArea(HitArea) 精确判断
            if (allowedFactions.HasFlag(TargetableFactions.Player))
            {
                var playerNode = area.GetTree()?.GetFirstNodeInGroup("player");
                if (playerNode is GameActor player
                    && player.IsHitByArea(area)
                    && (allowSelfDamage || !BelongsToActor(player, attacker))
                    && damaged.Add(player.GetInstanceId()))
                {
                    DealToGameActor(player, damage, area.GlobalPosition, attacker, DamageSource.DirectAttack);
                }
            }

            // 其他阵营：IntersectShape 扫描
            // 形状查找兜底：默认名 "CollisionShape2D" 找不到时遍历子节点（场景里形状可能命名为
            // CollisionShape2D2 等——如 guard1 的 OnePunchAttackArea——否则扫描直接 return 打不到 WorldItem/Enemy）
            var shapeNode = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (shapeNode?.Shape == null)
                shapeNode = FindFirstCollisionShape(area);
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

                if (!allowSelfDamage && BelongsToActor(collider, attacker)) continue;

                var root = ResolveDamageReceiver(collider, allowedFactions);
                if (root == null || !damaged.Add(root.GetInstanceId())) continue;

                DealDamage(collider, damage, area.GlobalPosition, attacker,
                    DamageSource.DirectAttack, allowedFactions, allowSelfDamage, area);
            }
        }

        public static Node? ResolveDamageReceiver(Node target, TargetableFactions allowedFactions)
        {
            Node? current = target;
            while (current != null)
            {
                var faction = GetFaction(current);
                if (faction != TargetableFactions.None && !allowedFactions.HasFlag(faction) && !AcceptsAnyAttack(current))
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

        public static bool BelongsToActor(Node node, GameActor? actor)
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

        /// <summary>目标侧声明（damage_receivable 组）：无视攻击方分类过滤，接收任何攻击方的伤害（可破坏屏障等）。</summary>
        private static bool AcceptsAnyAttack(Node node) => node.IsInGroup("damage_receivable");

        /// <summary>遍历节点子节点找第一个 CollisionShape2D（默认名查找失败的兜底）。</summary>
        private static CollisionShape2D? FindFirstCollisionShape(Node node)
        {
            foreach (Node child in node.GetChildren())
            {
                if (child is CollisionShape2D shape) return shape;
            }
            return null;
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

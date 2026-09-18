using System.Collections.Generic;
using Godot;
using Kuros.Items.World;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 冲刺清障工具:销毁冲刺方向前方探测盒内的一次性投掷道具(家具类,天然家具与乱码块件不限来源)。
    /// 走实体完整销毁链(RequestDestroy:OnThrowDestroy 表现 + Destroyed 广播,含 A_004 冲击投放爆炸联动);
    /// 飞行/回弹/销毁中的家具由实体侧自行忽略(玩家在途投掷物不受影响);投掷武器(IsFurniture=false)不清。
    /// 供各冲刺技能在自己 dash 期间逐物理帧调用(参数暴露在各攻击节点的 "Dash Prop Clear" 分组)。
    /// </summary>
    public static class EnemyDashPropClearer
    {
        private static RectangleShape2D? _probeShape;

        /// <param name="enemy">冲刺敌人(查询掩码取 Enemy.CollisionMask:能挡路的才在清障范围内)。</param>
        /// <param name="direction">冲刺方向(自动归一化)。</param>
        /// <param name="length">探测盒长度(世界像素):自敌人中心沿冲刺方向向前延伸。</param>
        /// <param name="height">探测盒高度(世界像素):以敌人原点垂直居中。</param>
        /// <returns>本帧销毁的家具件数。</returns>
        public static int ClearAhead(SampleEnemy? enemy, Vector2 direction, float length, float height)
        {
            if (enemy == null || !GodotObject.IsInstanceValid(enemy)) return 0;
            if (length <= 0f || height <= 0f || direction == Vector2.Zero) return 0;
            direction = direction.Normalized();
            if (direction == Vector2.Zero) return 0;

            var space = enemy.GetWorld2D()?.DirectSpaceState;
            if (space == null) return 0;

            _probeShape ??= new RectangleShape2D();
            _probeShape.Size = new Vector2(length, height);

            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = _probeShape,
                Transform = new Transform2D(0f, enemy.GlobalPosition + direction * (length * 0.5f)),
                CollisionMask = enemy.CollisionMask,
                CollideWithAreas = false,
                CollideWithBodies = true,
            };

            int cleared = 0;
            var seen = new HashSet<ulong>();
            foreach (var result in space.IntersectShape(query))
            {
                if (!result.TryGetValue("collider", out var collider)) continue;
                if (collider.As<GodotObject>() is not Node hitNode) continue;

                // 命中体可能家具子节点(RigidBody2D / 导航源 StaticBody2D)→ 父链解析到实体根
                RigidBodyWorldItemEntity? entity = null;
                for (Node? node = hitNode; node != null; node = node.GetParent())
                {
                    if (node is RigidBodyWorldItemEntity found)
                    {
                        entity = found;
                        break;
                    }
                }
                if (entity == null || !seen.Add(entity.GetInstanceId())) continue;
                if (entity.ItemDefinition?.IsFurniture != true) continue;

                entity.RequestDestroy();
                cleared++;
            }

            return cleared;
        }
    }
}

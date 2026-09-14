using Godot;
using Kuros.Core.Effects;
using Kuros.Items;
using Kuros.Items.World;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 连锁响应（BuildThrow_B_010）：**投掷出去的一次性道具被销毁时**，在其**销毁点**随机打出
    /// 快捷栏中一件投掷武器的效果——**不占原武器 CD、不扣数量、武器副本不飞行也不落地**：
    /// 副本在该点生成后立刻走"投掷销毁"链，把该武器自己的 OnThrowDestroy 效果打在那里
    /// （回旋镖=镖出去、手雷=就地爆/放烟等），相当于一件一次性投掷武器。
    /// 判定口径：
    ///   · 只认**自己投出的**件（LastDroppedBy 是自己 + ThrowCountUsed&gt;0）；
    ///   · 只认一次性道具（IsFurniture；投掷武器本体不算）；
    ///   · A_008 分裂件 / 放置件不触发（ThrowCountUsed == 0）——一次投掷只连锁一次武器，
    ///     否则「进程分叉 + 本卡」会一次投掷打出多把武器；
    ///   · 位置取内部 RigidBody2D（飞行/落点期间 wrapper 根位置停在投掷起点，不同步），与 A_004 / B_007 同款取法。
    /// 事件源 RigidBodyWorldItemEntity.Destroyed（QueueFree 之前触发，与 A_004 / B_007 同源）。
    /// </summary>
    [GlobalClass]
    public partial class ThrowChainResponseEffect : ActorEffect
    {
        protected override void OnApply()
        {
            RigidBodyWorldItemEntity.Destroyed += OnPieceDestroyed;
        }

        public override void OnRemoved()
        {
            RigidBodyWorldItemEntity.Destroyed -= OnPieceDestroyed;
            base.OnRemoved();
        }

        private void OnPieceDestroyed(RigidBodyWorldItemEntity piece)
        {
            if (Actor == null || !GodotObject.IsInstanceValid(piece)) return;
            if (piece.LastDroppedBy != Actor) return;
            if (piece.ItemDefinition?.IsFurniture != true) return;
            if (piece.ThrowCountUsed <= 0) return; // 必须是"投出的件"（放置件/分裂件不算）

            ThrowWeaponLauncher.TriggerWeaponEffectAt(Actor, ResolvePiecePosition(piece));
        }

        /// <summary>取件的实际位置：飞行/落点期间 wrapper 根位置不同步（停在投掷起点），
        /// 内部 RigidBody2D 才是真实位置（同 A_004 / B_007 的取法）。</summary>
        private static Vector2 ResolvePiecePosition(RigidBodyWorldItemEntity piece)
        {
            var body = piece.GetNodeOrNull<RigidBody2D>("RigidBody2D");
            if (body == null)
            {
                foreach (var child in piece.GetChildren())
                {
                    if (child is RigidBody2D rb) { body = rb; break; }
                }
            }
            return body != null && GodotObject.IsInstanceValid(body)
                ? body.GlobalPosition
                : piece.GlobalPosition;
        }
    }
}

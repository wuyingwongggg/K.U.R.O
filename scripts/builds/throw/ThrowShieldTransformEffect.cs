using System;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Items.World;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 对象转型（BuildThrow_A_009）：举起乱码块时短按核心技能,把手中件转化为次数盾,
    /// 护盾按该件的投掷等级(ThrowTier 1/2/3)抵挡 1/2/3 次伤害。
    /// 抵挡语义 = 1 秒免疫窗口(首次受击开启,窗口结束才消耗一次);
    /// 次数/窗口/视觉由 <see cref="ChargeShieldController"/> 统一管理——与 P2 护盾**互相覆盖**:
    /// 后施加者覆盖前者(不叠加),因此本卡只负责"转化"入口与参数注入。
    /// </summary>
    [GlobalClass]
    public partial class ThrowShieldTransformEffect : ActorEffect
    {
        /// <summary>护盾视觉场景(ShieldRingEffect 等;为空则只有格挡无视觉)。</summary>
        [Export] public PackedScene? ShieldVisualScene { get; set; }

        /// <summary>每次抵挡的免疫窗口时长(秒):窗口内所有伤害全免,结束才消耗一次次数。</summary>
        [Export(PropertyHint.Range, "0.1,3,0.1")] public float ImmunitySeconds { get; set; } = 1f;

        /// <summary>按件等级(1/2/3)的护盾配色:RGB=颜色,A=整体透明度。</summary>
        [Export] public Color[] TierShieldColors { get; set; } =
        {
            new(0.55f, 0.80f, 1.00f, 0.45f), // 层1:浅、透(1 次)
            new(0.30f, 0.60f, 0.95f, 0.70f), // 层2(2 次)
            new(0.12f, 0.35f, 0.85f, 0.95f), // 层3:深、实(3 次)
        };

        /// <summary>受击反馈:闪色(默认淡红)。</summary>
        [Export] public Color BlockFlashColor { get; set; } = new(1f, 0.42f, 0.42f, 1f);
        /// <summary>受击反馈:闪光时长(秒,0=关闭)。</summary>
        [Export(PropertyHint.Range, "0,1,0.01")] public float BlockFlashDuration { get; set; } = 0.16f;
        /// <summary>受击反馈:弹跳峰值缩放(1=无弹跳)。</summary>
        [Export(PropertyHint.Range, "1,1.5,0.01")] public float BlockPopScale { get; set; } = 1.12f;

        private ThrowCoreEffect? _core;
        private Func<bool>? _handler;

        protected override void OnApply()
        {
            _handler = TryTransformHeldPiece;
            _core = Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            if (_core != null)
                _core.HeldPieceCoreSkillHandler = _handler;
        }

        public override void OnRemoved()
        {
            if (_core != null && ReferenceEquals(_core.HeldPieceCoreSkillHandler, _handler))
                _core.HeldPieceCoreSkillHandler = null;
            _core = null;
            _handler = null;

            // 卡移除:只清掉"属于自己的"护盾(若已被 P2 覆盖则保留对方)
            if (Actor != null)
                ChargeShieldController.Find(Actor)?.ClearIfOwned(this);
            base.OnRemoved();
        }

        // ── 核心技能接管:手持件 → 护盾 ────────────────────────────

        private bool TryTransformHeldPiece()
        {
            if (Actor is not SamplePlayer player) return false;
            var inventory = player.InventoryComponent;
            var stack = inventory?.FurnitureSlotStack;
            if (stack == null || stack.IsEmpty) return false;

            // 仅乱码块(件):拾取时写在家具槽栈上的身份标记
            bool isPiece = stack.RuntimeSourceTag == RigidBodyWorldItemEntity.ThrowCorePieceTag
                        || stack.RuntimeIsThrowCoreCopy;
            if (!isPiece) return false;

            int charges = Mathf.Clamp(stack.Item.ThrowTier, 1, 3); // 投掷等级 → 抵挡 1/2/3 次
            inventory!.ClearFurnitureSlot(Actor); // 消耗手中件(手部视觉经 FurnitureSlotChanged 自动清)

            // 覆盖语义:新护盾按当前件等级重设次数(不累加),连同视觉/配色一起覆盖
            ChargeShieldController.GetOrCreate(Actor).Apply(
                this, charges, ShieldVisualScene, TierShieldColors,
                ImmunitySeconds, BlockFlashColor, BlockFlashDuration, BlockPopScale);
            return true;
        }
    }
}

using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 进程锁定（BuildThrow_B_005）：持有投掷道具(holding)期间获得霸体状态与伤害减免。
    /// 霸体 = ForcedMovement|Stun 免疫 + IgnoreHitStateOnDamage(受击不进入硬直,同时抑制击退标记,
    /// 与 MachineGearJointEffect 同模式);减伤 = IncomingDamageMultiplier 按层压缩(层1 15%/层2 30%)。
    /// 持有判定 = 家具槽或当前快捷栏选中物为投掷物且未在投掷冷却(与 IdleHolding/RunHolding 同源)。
    /// 原值暂存,离手/效果移除时还原。
    /// </summary>
    [GlobalClass]
    public partial class ThrowProcessLockEffect : ActorEffect
    {
        /// <summary>各层伤害减免百分比(B_005 注入 [15, 30])。</summary>
        [Export] public float[] TierValues { get; set; } = { 15f, 30f };

        private int _tier = 1;
        private bool _applied;
        private ImmunityFlags _storedImmunities;
        private bool _storedIgnoreHitState;
        private float _storedDamageMultiplier = 1f;

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
            // 持有时升级:立即按新层重算减伤
            if (_applied && Actor != null)
                Actor.IncomingDamageMultiplier = _storedDamageMultiplier * ReductionFactor;
        }

        protected override void OnTick(double delta)
        {
            if (Actor == null) return;

            bool holding = IsHoldingThrowable();
            if (holding && !_applied) Apply();
            else if (!holding && _applied) Restore();
        }

        /// <summary>当前减伤倍率(1 = 无减免)。</summary>
        private float ReductionFactor
        {
            get
            {
                if (TierValues.Length == 0) return 1f;
                float percent = TierValues[Mathf.Clamp(_tier, 1, TierValues.Length) - 1];
                return 1f - Mathf.Clamp(percent, 0f, 100f) / 100f;
            }
        }

        /// <summary>持有投掷道具:家具槽优先(举起的一次性投掷物),否则当前选中快捷栏位;
        /// 投掷冷却中(武器已出手)不算持有——与持物状态机判定同源。</summary>
        private bool IsHoldingThrowable()
        {
            if (Actor is not SamplePlayer player) return false;
            var inventory = player.InventoryComponent;
            if (inventory == null) return false;

            var stack = inventory.HasFurnitureItem
                ? inventory.FurnitureSlotStack
                : inventory.GetSelectedQuickBarStack();
            if (stack == null || stack.IsEmpty || stack.IsThrowOnCooldown) return false;
            return stack.Item.IsThrowable;
        }

        private void Apply()
        {
            if (Actor == null || _applied) return;
            _storedImmunities = Actor.ActiveImmunities;
            _storedIgnoreHitState = Actor.IgnoreHitStateOnDamage;
            _storedDamageMultiplier = Actor.IncomingDamageMultiplier;
            Actor.ActiveImmunities |= ImmunityFlags.ForcedMovement | ImmunityFlags.Stun;
            Actor.IgnoreHitStateOnDamage = true;
            Actor.IncomingDamageMultiplier = _storedDamageMultiplier * ReductionFactor;
            _applied = true;
        }

        private void Restore()
        {
            if (Actor == null || !_applied) return;
            Actor.ActiveImmunities = _storedImmunities;
            Actor.IgnoreHitStateOnDamage = _storedIgnoreHitState;
            Actor.IncomingDamageMultiplier = _storedDamageMultiplier;
            _applied = false;
        }

        public override void OnRemoved()
        {
            Restore(); // 效果被移除(死亡清理/换核心)时兜底还原
            base.OnRemoved();
        }
    }
}

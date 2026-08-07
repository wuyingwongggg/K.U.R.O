using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 死亡余温：释放核心技能后的 buff 期间，场上每有一名敌人死亡，
    /// 使释放泄热速度（HeatDrainRate）归零，持续 DurationValues[tier] 秒。
    /// 通过 MachineCoreEffect 修改器注册（-100% → 泄热为 0），多次死亡会刷新时长。
    /// 泄热归零期间同时冻结热量获取（FreezeHeatGain），防止热量在零衰减窗口内上涨。
    /// </summary>
    [GlobalClass]
    public partial class MachineDeathEmberEffect : ActorEffect
    {
        [Export] public float[] DurationValues { get; set; } = { 1f, 2f, 3f };

        private MachineCoreEffect? _core;
        private int _tier;
        private bool _emberActive;
        private float _holdRemaining;

        private float CurrentHoldDuration => _tier < DurationValues.Length ? DurationValues[_tier] : DurationValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            GameActor.AnyDamageTaken += OnAnyDamageTaken;
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, DurationValues.Length - 1);
        }

        protected override void OnTick(double delta)
        {
            if (!_emberActive) return;
            _holdRemaining -= (float)delta;
            if (_holdRemaining <= 0f)
                ClearEmber();
        }

        public override void OnRemoved()
        {
            GameActor.AnyDamageTaken -= OnAnyDamageTaken;
            ClearEmber();
            base.OnRemoved();
        }

        private void OnAnyDamageTaken(GameActor target, GameActor? attacker, int damage)
        {
            if (_core == null || !_core.IsBuffActive) return;
            if (!target.IsInGroup("enemies")) return;
            if (target.CurrentHealth > 0) return; // 非致命伤害不算死亡
            TriggerEmber();
        }

        /// <summary>泄热归零并刷新持续时长（多次死亡重置计时）；期间冻结热量获取。</summary>
        private void TriggerEmber()
        {
            if (_core == null) return;
            if (!_emberActive)
            {
                _emberActive = true;
                _core.SetStatModifier(MachineCoreEffect.HeatStat.HeatDrainRate, EffectId, -100f);
                _core.FreezeHeatGain = true;
            }
            _holdRemaining = CurrentHoldDuration;
        }

        private void ClearEmber()
        {
            if (!_emberActive) return;
            _emberActive = false;
            if (_core != null)
            {
                _core.RemoveStatModifier(MachineCoreEffect.HeatStat.HeatDrainRate, EffectId);
                _core.FreezeHeatGain = false;
            }
        }
    }
}

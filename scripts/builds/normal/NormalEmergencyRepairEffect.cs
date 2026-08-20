using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 应急修复（BuildNormal_A_003）：受到伤害后若生命值低于阈值（最大生命的 TierValues 百分比），
    /// 在 RepairDuration 秒内缓回最大生命值的对应百分比。
    /// 修复进行中忽略新的低血受击（防刷）；一次修复结束后可再次触发。
    /// </summary>
    [GlobalClass]
    public partial class NormalEmergencyRepairEffect : ActorEffect
    {
        /// <summary>低血触发阈值（最大生命的百分比，随层变化）。</summary>
        [Export] public float[] TierValues { get; set; } = { 15f, 30f };
        /// <summary>恢复量：最大生命的固定百分比（不随层变化）。</summary>
        [Export(PropertyHint.Range, "1,100,1")] public float RepairPercent { get; set; } = 20f;
        /// <summary>缓回总时长（秒）。</summary>
        [Export(PropertyHint.Range, "1,10,0.5")] public float RepairDuration { get; set; } = 5f;
        /// <summary>触发冷却（秒）：触发修复后冷却期内不再响应低血受击（修复期间与冷却期合并计算）。</summary>
        [Export(PropertyHint.Range, "0,60,1")] public float CooldownSeconds { get; set; } = 10f;

        private int _tier;
        private bool _repairing;
        private float _healAccumulator;
        private float _totalRepair;
        private float _repairTimer;
        private float _cooldownTimer;

        private float CurrentPercent => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _repairing = false;
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
        }

        protected override void OnTick(double delta)
        {
            if (Actor == null || Actor.IsDeadOrDying) return;

            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= (float)delta;
            }

            if (_repairing)
            {
                _repairTimer += (float)delta;
                float perSecond = _totalRepair / Mathf.Max(0.1f, RepairDuration);
                _healAccumulator += perSecond * (float)delta;
                int heal = Mathf.FloorToInt(_healAccumulator);
                if (heal > 0)
                {
                    _healAccumulator -= heal;
                    Actor.RestoreHealth(Actor.CurrentHealth + heal);
                }

                // RepairDuration 到时或满血时结束本次修复，随后才开始冷却计时
                if (_repairTimer >= RepairDuration || Actor.CurrentHealth >= Actor.MaxHealth)
                {
                    _repairing = false;
                    _cooldownTimer = CooldownSeconds;
                }
                return;
            }

            // 低血轮询（替代受击事件）：任何时刻血量低于阈值即触发——覆盖非受击低血
            // （毒伤/燃烧/先前战斗残留），性能损耗约等于一次比较（OnTick 本就在每帧执行）
            if (_cooldownTimer > 0f)
            {
                return;
            }

            float thresholdPercent = CurrentPercent;
            if (Actor.CurrentHealth <= Actor.MaxHealth * thresholdPercent / 100f)
            {
                _totalRepair = Actor.MaxHealth * RepairPercent / 100f;
                _healAccumulator = 0f;
                _repairTimer = 0f;
                _repairing = true;
            }
        }
    }
}

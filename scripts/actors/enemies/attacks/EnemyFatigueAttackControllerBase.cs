using Godot;
using System.Collections.Generic;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 疲劳权重攻击控制器基类：基于场景配置的 attack_weight 做概率选择——
    /// 连续使用同一攻击时每次降低其权重（疲劳），切回其他攻击后恢复原始权重。
    /// 效果：攻击组合多样化，避免连续重复（概率倾斜，非硬计数）。
    /// 子类继承并保留各自攻击名默认值/特殊逻辑（如血量阈值触发终极技）。
    /// </summary>
    public abstract partial class EnemyFatigueAttackControllerBase : EnemyAttackController
    {
        [Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";

        /// <summary>技能攻击名（供动画控制器区分"近战/技能"动画；空 = 不区分）。</summary>
        [Export] public string SkillAttackName { get; set; } = "";

        /// <summary>连续使用相同攻击时每次额外降低的权重百分比。</summary>
        [Export(PropertyHint.Range, "0,50,1")]
        public int FatiguePercentPerUse = 10;

        /// <summary>疲劳惩罚后的最小权重百分比（不低于原始权重的此比例）。</summary>
        [Export(PropertyHint.Range, "1,100,1")]
        public int MinWeightPercent = 10;

        public string CurrentAttackName { get; protected set; } = string.Empty;

        protected readonly Dictionary<string, float> _originalWeights = new();
        protected string _lastAttackName = string.Empty;
        protected int _consecutiveSameCount;
        /// <summary>排队超时疲劳独立计数（按攻击名）：超时疲劳与使用疲劳分账，
        /// 不污染 _lastAttackName/_consecutiveSameCount——连续使用判定只认真正启动的攻击。
        /// 攻击成功启动或权重被还原时清零对应条目。</summary>
        private readonly Dictionary<string, int> _timeoutStreaks = new();

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
            CacheOriginalWeights();
        }

        // 在 base.Initialize 填充条目后，记录各攻击的原始权重（读场景配置的 attack_weight）
        private void CacheOriginalWeights()
        {
            _originalWeights.Clear();
            foreach (var child in GetChildren())
            {
                if (child is EnemyAttackTemplate template && child.HasMeta("attack_weight"))
                {
                    float w = (float)child.GetMeta("attack_weight");
                    _originalWeights[template.Name] = w;
                }
            }
        }

        protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
            base.OnChildAttackStarted(attack);
            CurrentAttackName = attack.Name;

            // 攻击真正启动：清掉该攻击的超时疲劳计数（启动即视为"到达"，超时账本作废）
            _timeoutStreaks.Remove(attack.Name);

            if (attack.Name == _lastAttackName)
            {
                // 连续使用相同攻击：疲劳累计，权重递减（不低于原始权重 × MinWeightPercent%）
                _consecutiveSameCount++;
                int reduction = _consecutiveSameCount * FatiguePercentPerUse;
                float originalWeight = GetOriginalWeight(attack.Name);
                float floor = originalWeight * MinWeightPercent / 100f;
                float newWeight = Mathf.Max(originalWeight * (100 - reduction) / 100f, floor);
                TrySetAttackWeight(attack.Name, newWeight);
            }
            else
            {
                // 切换攻击：恢复上一次攻击的原始权重，重置疲劳计数
                if (!string.IsNullOrEmpty(_lastAttackName))
                    RestoreAttackWeight(_lastAttackName);

                _consecutiveSameCount = 1;
                _lastAttackName = attack.Name;
            }
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            CurrentAttackName = string.Empty;
        }

        /// <summary>
        /// 排队攻击超时 = 视为一次"尝试"：只施加疲劳降权（独立计数，连续超时逐次降低，
        /// 不低于原权重 × MinWeightPercent%），**不修改 _lastAttackName/_consecutiveSameCount**。
        /// 原因：若把超时攻击记为"上次使用"，会污染连续使用判定——后续该攻击启动被误判为
        /// 连续（提前吃到疲劳），其他攻击启动被误判为切换（永不疲劳 + 顺手还原上一个，
        /// 实测导致 DashSlashAttackPro 权重恒 30）。
        /// </summary>
        protected override void OnQueuedAttackTimeout(EnemyAttackTemplate attack)
        {
            _timeoutStreaks.TryGetValue(attack.Name, out int streak);
            streak++;
            _timeoutStreaks[attack.Name] = streak;

            int reduction = streak * FatiguePercentPerUse;
            float originalWeight = GetOriginalWeight(attack.Name);
            float floor = originalWeight * MinWeightPercent / 100f;
            // 基于"当前权重"而非原始权重计算：武装态（如终极技锁定，权重被强制为 1、
            // 原始为 0）超时时若按原始计算会打回 0——其余攻击已被清零，且重新武装只在
            // 子攻击启动/结束时运行，而全 0 权重选不中任何攻击 → 全部权重归零死锁。
            // 基于当前权重几何衰减（下限保护照旧）永不归零，武装态可自愈。
            float currentWeight = GetAttackWeights().TryGetValue(attack.Name, out float w)
                ? w
                : originalWeight;
            float newWeight = Mathf.Max(currentWeight * (100 - reduction) / 100f, floor);
            TrySetAttackWeight(attack.Name, newWeight);
        }

        protected float GetOriginalWeight(string attackName)
        {
            return _originalWeights.TryGetValue(attackName, out float w) ? w : 0f;
        }

        protected void RestoreAttackWeight(string attackName)
        {
            // 权重还原到原始值时，该攻击的超时疲劳账本同步作废
            _timeoutStreaks.Remove(attackName);
            if (_originalWeights.TryGetValue(attackName, out float original))
                TrySetAttackWeight(attackName, original);
        }
    }
}

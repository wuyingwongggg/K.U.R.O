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
        /// 排队攻击超时 = 视为一次使用：与该攻击连续使用时的疲劳降权一致——
        /// 连续超时的攻击权重逐次降低（不低于原权重 × MinWeightPercent%），其他攻击（如突刺）相对更容易被选中。
        /// </summary>
        protected override void OnQueuedAttackTimeout(EnemyAttackTemplate attack)
        {
            if (attack.Name == _lastAttackName)
                _consecutiveSameCount++;
            else
                _consecutiveSameCount = 1;
            _lastAttackName = attack.Name;

            int reduction = _consecutiveSameCount * FatiguePercentPerUse;
            float originalWeight = GetOriginalWeight(attack.Name);
            float floor = originalWeight * MinWeightPercent / 100f;
            float newWeight = Mathf.Max(originalWeight * (100 - reduction) / 100f, floor);
            TrySetAttackWeight(attack.Name, newWeight);
        }

        protected float GetOriginalWeight(string attackName)
        {
            return _originalWeights.TryGetValue(attackName, out float w) ? w : 0f;
        }

        protected void RestoreAttackWeight(string attackName)
        {
            if (_originalWeights.TryGetValue(attackName, out float original))
                TrySetAttackWeight(attackName, original);
        }
    }
}

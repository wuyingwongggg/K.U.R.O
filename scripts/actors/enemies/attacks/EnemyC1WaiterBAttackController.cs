using Godot;
using System;
using System.Collections.Generic;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_C1_waiterB 攻击控制器：疲劳权重法（继承基类）——近战/技能连续同攻击降权、切攻击恢复；
    /// 保留血量阈值触发终极技的强制机制（优先级最高）。
    /// </summary>
    public partial class EnemyC1WaiterBAttackController : EnemyFatigueAttackControllerBase
    {
        /// <summary>兼容旧动画控制器（EnemyC1WaiterBSpineAnimationController）的 Skill1AttackName 引用：同步到基类 SkillAttackName。</summary>
        [Export] public string Skill1AttackName
        {
            get => SkillAttackName;
            set => SkillAttackName = value;
        }

        [Export] public string UltimateAttackName { get; set; } = "UltimateBeamAttack";

        [ExportCategory("Ultimate Attack")]
        /// <summary>
        /// 触发终极技的血量百分比阈值列表（0~1）。
        /// 每个阈值只触发一次，按从高到低顺序依次检测，参考 EnemySpawnState 逻辑。
        /// </summary>
        [Export] public float[] UltimateHealthThresholds = new float[] { 0.5f };

        /// <summary>每次子攻击启动时自增，供动画控制器区分连续同类攻击的不同执行次。</summary>
        public int AttackRunId { get; private set; } = 0;

        private float[] _sortedUltimateThresholds = Array.Empty<float>();
        private int _triggeredUltimateIndex;

        public EnemyC1WaiterBAttackController()
        {
            Skill1AttackName = "DashSlashAttack";
        }

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
            _triggeredUltimateIndex = 0;
            RefreshThresholdCache();
        }

        protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
            base.OnChildAttackStarted(attack); // 疲劳逻辑（连续同攻击降权/切攻击恢复）
            AttackRunId++;

            if (IsAttack(attack.Name, UltimateAttackName))
            {
                // 终极技真正开始时才消耗触发器
                ConsumeUltimateTrigger();
            }

            // 血量阈值优先：触发时强制终极；否则维持疲劳权重（基类已维护近战/技能权重）
            ConfigureNextAttack();
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            // 重查血量：攻击期间可能跌破阈值，确保及时触发终极技
            ConfigureNextAttack();
        }

        private void ConfigureNextAttack()
        {
            if (ShouldTriggerUltimate())
            {
                TrySetAttackWeight(UltimateAttackName, 1f);
                TrySetAttackWeight(Skill1AttackName, 0f);
                TrySetAttackWeight(MeleeAttackName, 0f);
                return;
            }

            // 普通循环：终极技权重清零，近战/技能按疲劳权重（基类维护）
            TrySetAttackWeight(UltimateAttackName, 0f);
        }

        private bool ShouldTriggerUltimate()
        {
            if (_sortedUltimateThresholds.Length == 0) return false;
            if (_triggeredUltimateIndex >= _sortedUltimateThresholds.Length) return false;
            if (Enemy == null || Enemy.MaxHealth <= 0) return false;

            float healthRatio = (float)Enemy.CurrentHealth / Enemy.MaxHealth;
            return healthRatio <= _sortedUltimateThresholds[_triggeredUltimateIndex];
        }

        private void ConsumeUltimateTrigger()
        {
            _triggeredUltimateIndex++;
        }

        private void RefreshThresholdCache()
        {
            var list = new List<float>();
            if (UltimateHealthThresholds != null)
            {
                foreach (float t in UltimateHealthThresholds)
                {
                    float clamped = Mathf.Clamp(t, 0.01f, 1.0f);
                    if (!list.Contains(clamped))
                        list.Add(clamped);
                }
            }
            // 降序排列，从最高阈值开始依次触发
            list.Sort((a, b) => b.CompareTo(a));
            _sortedUltimateThresholds = list.ToArray();
        }

        private static bool IsAttack(string attackName, string expectedName)
        {
            return attackName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 终极激光束攻击进行中不允许玩家跳出检测区域而中断攻击。
        /// </summary>
        protected override bool ShouldInterruptOnPlayerExit()
        {
            return !IsAttack(CurrentAttackName, UltimateAttackName);
        }
    }
}

using Godot;
using System;
using System.Collections.Generic;
using Kuros.Core;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_D1_netAdmin 攻击控制器：疲劳权重法（继承基类）——连续同攻击降权、切攻击恢复；
    /// 保留血量阈值终极技强制机制 + HP 阈值 SimpleMelee → MultiMelee 升级。
    /// 召唤禁用已移除：由 EnemyAttackTemplate 的 AttackEffectEntry.UniqueGroup + BlockedByFxGroup 实现
    /// （场上已有同组特效 → 攻击不可选中），不再需要控制器内 CountNearbyEnemies 检测。
    /// </summary>
    public partial class EnemyD1NetAdminAttackController : EnemyFatigueAttackControllerBase
    {
        /// <summary>兼容动画控制器（EnemyD1NetAdminSpineAnimationController）：多段近战攻击名。</summary>
        [Export] public string MultiMeleeAttackName { get; set; } = "MultiMeleeAttack";

        /// <summary>兼容动画控制器：召唤攻击名。</summary>
        [Export] public string SummonAttackName { get; set; } = "SummonAttack";

        [Export] public string UltimateAttackName { get; set; } = "UltimateAttack";

        [ExportCategory("Ultimate Attack")]
        /// <summary>
        /// 触发终极技的血量百分比阈值列表（0~1）。
        /// 每个阈值只触发一次，按从高到低顺序依次检测。
        /// </summary>
        [Export] public float[] UltimateHealthThresholds = new float[] { 0.5f };

        /// <summary>HP 低于此阈值时 SimpleMelee → MultiMelee 升级。</summary>
        [Export(PropertyHint.Range, "0,1,0.01")] public float MeleeUpgradeHPThreshold = 0.5f;

        private float[] _sortedUltimateThresholds = Array.Empty<float>();
        private int _triggeredUltimateIndex;
        /// <summary>触发终极技时其余攻击权重被清零；终极技启动后一次性还原（之后由基类疲劳逻辑维护）。</summary>
        private bool _pendingWeightRestore;
        /// <summary>Melee 升级状态缓存（只在切换时重设权重，避免每次循环抹掉疲劳降权）。</summary>
        private bool _meleeUpgraded;

        public EnemyD1NetAdminAttackController()
        {
            SkillAttackName = "MultiMeleeAttack";
        }

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
            _triggeredUltimateIndex = 0;
            _meleeUpgraded = false;
            RefreshThresholdCache();
        }

        protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
            base.OnChildAttackStarted(attack); // 疲劳逻辑（连续同攻击降权/切攻击恢复）

            if (IsAttack(attack.Name, UltimateAttackName))
                ConsumeUltimateTrigger();

            ConfigureNextAttack();
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            ConfigureNextAttack(); // 重查血量：攻击期间可能跌破阈值
        }

        /// <summary>
        /// 外部（如 Frozen 结束时）强制重新评估终极技触发条件。
        /// </summary>
        public void ForceEvaluateUltimate()
        {
            ConfigureNextAttack();
        }

        private void ConfigureNextAttack()
        {
            // 血量阈值优先：触发时强制终极（其余攻击清零，终极启动后一次性还原）
            if (ShouldTriggerUltimate())
            {
                TrySetAttackWeight(UltimateAttackName, 1f);
                TrySetAttackWeight(MeleeAttackName, 0f);
                TrySetAttackWeight(MultiMeleeAttackName, 0f);
                TrySetAttackWeight(SummonAttackName, 0f);
                _pendingWeightRestore = true;
                return;
            }

            // 普通循环：终极技权重清零
            TrySetAttackWeight(UltimateAttackName, 0f);

            // 终极技启动后（触发器已消费）：一次性还原被清零的攻击权重，之后交还基类疲劳逻辑
            if (_pendingWeightRestore)
            {
                RestoreAttackWeight(MeleeAttackName);
                RestoreAttackWeight(MultiMeleeAttackName);
                RestoreAttackWeight(SummonAttackName);
                _pendingWeightRestore = false;
            }

            // HP 阈值升级：SimpleMelee → MultiMelee（升级值写入缓存，疲劳逻辑按缓存还原）
            float hpRatio = Enemy != null && Enemy.MaxHealth > 0
                ? (float)Enemy.CurrentHealth / Enemy.MaxHealth
                : 1f;
            bool shouldUpgrade = hpRatio <= MeleeUpgradeHPThreshold;
            if (shouldUpgrade != _meleeUpgraded)
            {
                if (shouldUpgrade)
                {
                    _originalWeights[MultiMeleeAttackName] = GetOriginalWeight(MeleeAttackName);
                    TrySetAttackWeight(MultiMeleeAttackName, GetOriginalWeight(MeleeAttackName));
                    TrySetAttackWeight(MeleeAttackName, 0f);
                }
                else
                {
                    TrySetAttackWeight(MultiMeleeAttackName, 0f);
                    RestoreAttackWeight(MeleeAttackName);
                }
                _meleeUpgraded = shouldUpgrade;
            }
            else if (shouldUpgrade)
            {
                // 已处于升级态：只防基类切攻击还原复活 SimpleMelee
                TrySetAttackWeight(MeleeAttackName, 0f);
            }
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
            // 跳过 HP 已经低于的后续阈值，避免连发多次 Ultimate
            while (_triggeredUltimateIndex < _sortedUltimateThresholds.Length
                && Enemy != null
                && (float)Enemy.CurrentHealth / Enemy.MaxHealth <= _sortedUltimateThresholds[_triggeredUltimateIndex])
            {
                _triggeredUltimateIndex++;
            }
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
            list.Sort((a, b) => b.CompareTo(a));
            _sortedUltimateThresholds = list.ToArray();
        }

        private static bool IsAttack(string attackName, string expectedName)
        {
            return attackName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
        }
    }
}

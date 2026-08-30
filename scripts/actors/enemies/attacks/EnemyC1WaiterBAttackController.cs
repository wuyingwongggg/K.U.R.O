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

        /// <summary>DashSlash 强化版攻击名（半血时取代 DashSlashAttack）。</summary>
        [Export] public string DashSlashAttackProName { get; set; } = "DashSlashAttackPro";

        /// <summary>HP 低于此阈值时 DashSlashAttack → DashSlashAttackPro 升级（参考 netAdmin MultiMelee 取代的逻辑，取代对象是技能而非近战）。</summary>
        [Export(PropertyHint.Range, "0,1,0.01")] public float DashSlashUpgradeHPThreshold { get; set; } = 0.5f;

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
        /// <summary>触发终极技时近战/技能权重被清零；终极技启动后一次性还原（仅还原一次，
        /// 之后继续由基类疲劳逻辑维护——若每次普通循环都还原会破坏疲劳降权）。</summary>
        private bool _pendingWeightRestore;
        /// <summary>半血升级是否已生效（状态缓存：只在切换时重设权重，避免每次循环抹掉疲劳降权）。</summary>
        private bool _dashUpgraded;
        /// <summary>Pro 的场景原始权重（attack_weight 元数据值，降级时恢复）。</summary>
        private float _proSceneWeight;

        public EnemyC1WaiterBAttackController()
        {
            Skill1AttackName = "DashSlashAttack";
        }

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
            _triggeredUltimateIndex = 0;
            _proSceneWeight = GetOriginalWeight(DashSlashAttackProName);
            _dashUpgraded = false;
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
                TrySetAttackWeight(DashSlashAttackProName, 0f);
                _pendingWeightRestore = true;
                return;
            }

            // 普通循环：终极技权重清零（基类切攻击还原会恢复终极技原始权重，这里每次重新清零）
            TrySetAttackWeight(UltimateAttackName, 0f);

            // 终极技启动后（触发器已消费，进入普通分支）：一次性还原被清零的近战/技能权重。
            // 基类 OnChildAttackStarted 只还原 _lastAttackName（上一个攻击），
            // 被清零的另一个技能会永远留在 0——这里补上，之后权重交还基类疲劳逻辑。
            if (_pendingWeightRestore)
            {
                RestoreAttackWeight(Skill1AttackName);
                RestoreAttackWeight(MeleeAttackName);
                RestoreAttackWeight(DashSlashAttackProName);
                _pendingWeightRestore = false;
            }

            // 半血升级：DashSlashAttackPro 取代 DashSlashAttack（Pro 是技能 DashSlash 的进化版，
            // 取代对象是技能而非近战——SimpleMelee 保持原权重不受影响）。
            // 关键：升级时把 Skill1 的原始权重写入 Pro 的 _originalWeights 缓存——
            // 基类的所有还原路径（切攻击还原、大招后 pending 还原）都按缓存还原 Pro，
            // 天然得到升级值，无需每次循环重设 Pro 权重（那会抹掉疲劳降权）。
            float hpRatio = Enemy != null && Enemy.MaxHealth > 0
                ? (float)Enemy.CurrentHealth / Enemy.MaxHealth
                : 1f;
            bool shouldUpgrade = hpRatio <= DashSlashUpgradeHPThreshold;
            if (shouldUpgrade != _dashUpgraded)
            {
                if (shouldUpgrade)
                {
                    _originalWeights[DashSlashAttackProName] = GetOriginalWeight(Skill1AttackName);
                    TrySetAttackWeight(DashSlashAttackProName, GetOriginalWeight(Skill1AttackName));
                    TrySetAttackWeight(Skill1AttackName, 0f);
                }
                else
                {
                    _originalWeights[DashSlashAttackProName] = _proSceneWeight;
                    TrySetAttackWeight(DashSlashAttackProName, 0f);
                    RestoreAttackWeight(Skill1AttackName);
                }
                _dashUpgraded = shouldUpgrade;
            }
            else if (shouldUpgrade)
            {
                // 已处于升级态：只防基类切攻击还原复活 Skill1；Pro 权重由疲劳逻辑维护
                TrySetAttackWeight(Skill1AttackName, 0f);
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

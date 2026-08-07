using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Builds.BuildCore
{
    /// <summary>
    /// Machine 核心机制：热量表。
    /// 攻击和移动累积热量，按下核心技能键消耗全部热量换取临时增伤。
    /// 可被百分比修改器修改的属性：外部效果通过 SetStatModifier/RemoveStatModifier 注册，
    /// 最终值 = 基础值 × (1 + Σ修改器%/100)。多个效果加减对称，互不漂移。
    /// </summary>
    [GlobalClass]
    public partial class MachineCoreEffect : ActorEffect
    {
        public enum HeatStat { MaxHeat, MoveHeatRate, DecayRate, HeatDrainRate, DecayDelay }

        [ExportCategory("Heat")]
        [Export(PropertyHint.Range, "0,500,1")] public float MaxHeat = 100f; // 最大热量
        [Export(PropertyHint.Range, "0,50,0.5")] public float MoveHeatRate = 3f;  // 移动时每秒热量累积速度（基础值，速度越快累积越快）
        [Export] public bool EnableAttackHeatGain = false; // 攻击命中时增加的热量
        [Export(PropertyHint.Range, "0,50,0.5")] public float AttackHeatGain = 2f; // 攻击命中时增加的热量（每次命中）
        [Export(PropertyHint.Range, "0,50,0.5")] public float DecayRate = 6f; // 每秒热量衰减速度（非移动时）
        [Export(PropertyHint.Range, "0,50,0.1")] public float DecayDelay = 0.5f;  // 非移动时热量衰减前的延迟时间

        [ExportCategory("Release")]
        [Export(PropertyHint.Range, "0,5,0.01")] public float DamagePerHeat = 0.01f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float MaxDamageBonus = 1.5f;
        [Export(PropertyHint.Range, "1,200,1")] public float HeatDrainRate = 33f;
        [Export(PropertyHint.Range, "0,10,0.1")] public float ReleaseCooldown = 1f;
        [Export] public bool DisableHeatGainDuringBuff = true; // Buff 期间是否禁用热量获取（移动/攻击命中）
        [Export(PropertyHint.Range, "0,5,0.1")] public float BuffHeatGainMultiplier = 1f; // Buff 期间热量获取倍率

        [ExportCategory("Overflow")]
        [Export] public bool AllowHeatOverflow = false; // 允许热量突破 MaxHeat（异常超频）
        [Export(PropertyHint.Range, "0,500,1")] public float MaxOverflowHeat = 50f; // 热量最多突破 MaxHeat 的上限值
        [Export(PropertyHint.Range, "0,50,0.5")] public float OverflowSpeedPercentPerHeat = 2f; // 每溢出 1 点热量的移动速度增益百分比
        [Export(PropertyHint.Range, "0,50,0.5")] public float OverflowAttackSpeedPercentPerHeat = 2f; // 每溢出 1 点热量的攻击速度增益百分比
        [Export(PropertyHint.Range, "0,50,0.5")] public float OverflowDamageTakenPercentPerHeat = 2f; // 每溢出 1 点热量的受伤增益百分比

        /// <summary>当前热量 (0-MaxHeat)，HUD 绑定读取。</summary>
        public float Heat { get; private set; }
        public float HeatRatio => MaxHeat > 0f ? Heat / MaxHeat : 0f;
        public bool IsReleasing { get; private set; }
        /// <summary>Buff 期间热量正在消耗中。</summary>
        public bool IsBuffActive => _buffActive;
        /// <summary>热量保底值：被动衰减不会低于此值。由外部效果设置。</summary>
        public float MinHeat { get; set; }
        /// <summary>冻结热量获取：期间禁止一切热量增加（移动/攻击/外部 AddHeat）。由外部效果（如死亡余温）设置。</summary>
        public bool FreezeHeatGain { get; set; }
        /// <summary>释放攻击力加成的倍率（1 = 原始加成）。由外部效果（燃烧效率）在 buff 期间动态衰减控制。</summary>
        public float ReleaseBonusMultiplier { get; set; } = 1f;
        /// <summary>本 buff 周期释放时消耗的热量快照，供外部效果计算衰减进度。</summary>
        public float ConsumedHeat => _consumedHeat;
        /// <summary>释放核心技能时同步触发（ReleaseHeat 内部）。外部效果（如燃烧效率）借此在释放瞬间立即接管加成，
        /// 避免命中触发的释放（爆发推力）在加成接管前读取到未放大的补偿值。</summary>
        public event Action? ReleaseStarted;

        private float _releaseCooldownRemaining;
        private float _consumedHeat;
        private float _originalAttackDamage;
        private float _decayTimer;
        private bool _buffActive;
        private bool _wasMovingLastFrame;

        private readonly Dictionary<HeatStat, float> _baseValues = new();
        private readonly Dictionary<HeatStat, Dictionary<string, float>> _modifiers = new();

        private float _baseSpeed;
        private float _baseAttackSpeed = 1f;
        private float _baseIncomingDamage = 1f;
        private bool _overflowApplied;
        /// <summary>每帧缓存的基础攻击力（OnTick 在正常帧流程运行，不会命中检测的临时 DamageOverride 覆盖）。</summary>
        private float _cachedAttackDamage;

        /// <summary>获取某属性的基础值（场景导出值，运行时不修改）。</summary>
        public float GetBaseValue(HeatStat stat)
        {
            return _baseValues.TryGetValue(stat, out float v) ? v : GetStatValue(stat);
        }

        /// <summary>注册百分比修改器（±%），同一效果 ID 重复调用为覆盖。</summary>
        public void SetStatModifier(HeatStat stat, string effectId, float percent)
        {
            if (!_modifiers.TryGetValue(stat, out var dict))
            {
                dict = new Dictionary<string, float>();
                _modifiers[stat] = dict;
            }
            dict[effectId] = percent;
            RecalculateStat(stat);
        }

        /// <summary>注销百分比修改器。</summary>
        public void RemoveStatModifier(HeatStat stat, string effectId)
        {
            if (_modifiers.TryGetValue(stat, out var dict) && dict.Remove(effectId))
                RecalculateStat(stat);
        }

        private void RecalculateStat(HeatStat stat)
        {
            float baseVal = _baseValues.TryGetValue(stat, out float b) ? b : GetStatValue(stat);

            float sum = 0f;
            if (_modifiers.TryGetValue(stat, out var dict))
            {
                foreach (float percent in dict.Values)
                    sum += percent;
            }

            // 钳制最终值 ≥ 0：修改器总和低于 -100%（如缓速放热 -75% + 死亡余温 -100%）
            // 时速率会变负（泄热变加热），钳制后最差为 0（冻结），杜绝负速率隐患
            SetStatValue(stat, Mathf.Max(baseVal * (1f + sum / 100f), 0f));

            // 容量缩小（减容效果）后热量回落：非超频时不允许 Heat 超过新上限，
            // 避免 HUD 将"容量变化"误判为爆表
            if (stat == HeatStat.MaxHeat && !AllowHeatOverflow && Heat > MaxHeat)
                Heat = MaxHeat;
        }

        private float GetStatValue(HeatStat stat) => stat switch
        {
            HeatStat.MaxHeat => MaxHeat,
            HeatStat.MoveHeatRate => MoveHeatRate,
            HeatStat.DecayRate => DecayRate,
            HeatStat.HeatDrainRate => HeatDrainRate,
            HeatStat.DecayDelay => DecayDelay,
            _ => 0f
        };

        private void SetStatValue(HeatStat stat, float value)
        {
            switch (stat)
            {
                case HeatStat.MaxHeat: MaxHeat = value; break;
                case HeatStat.MoveHeatRate: MoveHeatRate = value; break;
                case HeatStat.DecayRate: DecayRate = value; break;
                case HeatStat.HeatDrainRate: HeatDrainRate = value; break;
                case HeatStat.DecayDelay: DecayDelay = value; break;
            }
        }

        protected override void OnApply()
        {
            Heat = 0f;
            IsReleasing = false;
            _releaseCooldownRemaining = 0f;
            _consumedHeat = 0f;
            _buffActive = false;

            // 保存基础值（此刻无任何修改器注册，导出值 = 基础值）
            _baseValues.Clear();
            _modifiers.Clear();
            foreach (HeatStat stat in System.Enum.GetValues<HeatStat>())
                _baseValues[stat] = GetStatValue(stat);

            // 注意：Speed/攻速/易伤的基础值不在此处采样，
            // 而是在首次进入超频时采样（此时可能已被其他效果合法修改）
            if (Actor != null)
                _cachedAttackDamage = Actor.AttackDamage;

            DamageEventBus.Subscribe(OnDamageDealt);
        }

        protected override void OnTick(double delta)
        {
            float dt = (float)delta;
            if (Actor == null) return;

            // 每帧缓存基础攻击力：供 ReleaseHeat 捕获 _originalAttackDamage，
            // 避免命中检测临时把 AttackDamage 设为 DamageOverride 时采样到污染值
            _cachedAttackDamage = Actor.AttackDamage;

            // CD 计时
            if (_releaseCooldownRemaining > 0f)
            {
                _releaseCooldownRemaining -= dt;
                if (_releaseCooldownRemaining <= 0f)
                    IsReleasing = false;
            }

            // Buff 期间以固定速度消耗热量，热量归零时 Buff 结束
            if (_buffActive)
            {
                Heat = Mathf.Max(Heat - HeatDrainRate * dt, 0f);

                if (Heat <= 0f)
                {
                    Actor.AttackDamage = _originalAttackDamage;
                    _buffActive = false;
                }
            }

            // 超频加成每帧更新（放在 buff 早退之前，避免 buff 期间倍率冻结在旧值）
            ApplyOverflowBuff();

            // 热量累积：移动，速度越快积攒越快
            // Buff 期间是否禁用热量获取
            if (DisableHeatGainDuringBuff && _buffActive)
            {
                _wasMovingLastFrame = false;
                return;
            }

            float speed = Actor.Velocity.Length();
            bool moving = speed > 10f;
            if (moving && !FreezeHeatGain)
            {
                _decayTimer = DecayDelay;
                float speedMultiplier = 1.0f + Mathf.Max(speed - 500f, 0f) * 0.002f;
                float gainMult = _buffActive ? BuffHeatGainMultiplier : 1f;
                Heat = Mathf.Min(Heat + MoveHeatRate * speedMultiplier * gainMult * dt, GetHeatCap());
            }
            else if (Heat > MinHeat && !IsReleasing && !_buffActive)
            {
                _decayTimer -= dt;
                if (_decayTimer <= 0f)
                    Heat = Mathf.Max(Heat - DecayRate * dt, MinHeat);
            }
            _wasMovingLastFrame = moving;
        }

        private float GetHeatCap()
        {
            return AllowHeatOverflow ? MaxHeat + MaxOverflowHeat : MaxHeat;
        }

        /// <summary>
        /// 每溢出 1 点热量：移动速度、攻击速度、受到的伤害 +OverflowBuffPercentPerHeat%。
        /// 只在超频期间接管这三个属性：进入时采样当前基础值（可能已被其他效果合法修改），
        /// 退出超频的那一帧还原一次，避免每帧覆盖其他效果。
        /// </summary>
        private void ApplyOverflowBuff()
        {
            if (Actor == null) return;
            float overflow = Mathf.Max(Heat - MaxHeat, 0f);

            if (!AllowHeatOverflow || overflow <= 0f)
            {
                // 只在"刚退出超频"的那一帧还原一次，其余帧不再写这三个属性
                if (_overflowApplied)
                {
                    Actor.Speed = _baseSpeed;
                    Actor.AttackSpeedMultiplier = _baseAttackSpeed;
                    Actor.IncomingDamageMultiplier = _baseIncomingDamage;
                    _overflowApplied = false;
                }
                return;
            }

            float speedMult = 1f + overflow * OverflowSpeedPercentPerHeat / 100f;
            float attackSpeedMult = 1f + overflow * OverflowAttackSpeedPercentPerHeat / 100f;
            float damageTakenMult = 1f + overflow * OverflowDamageTakenPercentPerHeat / 100f;
            if (!_overflowApplied)
            {
                // 进入超频时重新采样当前基础值（含其他效果的合法修改）
                _baseSpeed = Actor.Speed;
                _baseAttackSpeed = Actor.AttackSpeedMultiplier;
                _baseIncomingDamage = Actor.IncomingDamageMultiplier;
                _overflowApplied = true;
            }
            Actor.Speed = _baseSpeed * speedMult;
            Actor.AttackSpeedMultiplier = _baseAttackSpeed * attackSpeedMult;
            Actor.IncomingDamageMultiplier = _baseIncomingDamage * damageTakenMult;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!IsInstanceValid(this) || Actor == null) return;
            if (_releaseCooldownRemaining > 0f) return;
            if (!@event.IsActionPressed(InputActions.CoreSkill) || @event.IsEcho()) return;

            ReleaseHeat();
            GetViewport()?.SetInputAsHandled();
        }

        /// <summary>攻击命中时由外部调用。</summary>
        public void OnAttackLanded()
        {
            if (IsReleasing) return;
            AddHeat(AttackHeatGain);
        }

        /// <summary>由外部效果增加热量（默认不超过 MaxHeat，超频开启时无上限）。冻结期间忽略。</summary>
        public void AddHeat(float amount)
        {
            if (amount <= 0f) return;
            if (FreezeHeatGain) return;
            Heat = Mathf.Min(Heat + amount, GetHeatCap());
        }

        /// <summary>消耗热量（外部效果用，如热能闪避）。buff 期间消耗会加速 buff 结束。</summary>
        public void ConsumeHeat(float amount)
        {
            if (amount <= 0f) return;
            Heat = Mathf.Max(Heat - amount, 0f);
        }

        /// <summary>供外部效果自动触发释放核心技能（受释放 CD 与热量约束）。</summary>
        public void TryReleaseCoreSkill()
        {
            if (_releaseCooldownRemaining > 0f) return;
            ReleaseHeat();
        }

        /// <summary>
        /// 当前释放 buff 的攻击力加成量（_originalAttackDamage × min(消耗热量×DamagePerHeat, MaxDamageBonus)）。
        /// Buff 未激活时为 0。供命中触发的释放效果把本段错过的加成补回。
        /// </summary>
        public float CurrentReleaseDamageBonus =>
            _buffActive ? _originalAttackDamage * Mathf.Min(_consumedHeat * DamagePerHeat, MaxDamageBonus) * ReleaseBonusMultiplier : 0f;

        /// <summary>
        /// 重新应用当前 buff 的攻击力加成。命中检测（PerformDefaultHitDetection）的 finally
        /// 会把 AttackDamage 还原为检测开始值，可能抹掉释放加成；命中触发的释放应在帧末调用此方法恢复，
        /// 确保下一次攻击的 DamageOverride 捕获到加成后的攻击力。
        /// </summary>
        public void RefreshReleaseDamageBonus()
        {
            if (!_buffActive || Actor == null) return;
            Actor.AttackDamage = _originalAttackDamage + CurrentReleaseDamageBonus;
        }

        private void ReleaseHeat()
        {
            if (Heat <= 0f) return;

            _consumedHeat = Heat;
            IsReleasing = true;
            _releaseCooldownRemaining = ReleaseCooldown;

            // 应用临时增伤 Buff（基于释放瞬间的热量），持续到热量归零
            if (!_buffActive)
            {
                // 用每帧缓存的基础攻击力，而非当前值（命中检测期间可能被临时覆盖）
                _originalAttackDamage = _cachedAttackDamage;
                _buffActive = true;
            }
            float bonus = _originalAttackDamage * Mathf.Min(_consumedHeat * DamagePerHeat, MaxDamageBonus) * ReleaseBonusMultiplier;
            Actor.AttackDamage = _originalAttackDamage + bonus;

            // 同步通知外部效果接管（在 AttackDamage 写入之后、调用方返回之前，
            // 保证命中触发的释放（爆发推力）读取 CurrentReleaseDamageBonus 时倍率已生效）
            ReleaseStarted?.Invoke();
        }

        public override void OnRemoved()
        {
            DamageEventBus.Unsubscribe(OnDamageDealt);
            if (_buffActive && Actor != null)
                Actor.AttackDamage = _originalAttackDamage;
            if (Actor != null && _overflowApplied)
            {
                // 仅在超频接管期间被移除时还原，非超频状态不碰这三个属性
                Actor.Speed = _baseSpeed;
                Actor.AttackSpeedMultiplier = _baseAttackSpeed;
                Actor.IncomingDamageMultiplier = _baseIncomingDamage;
            }
            base.OnRemoved();
        }

        private void OnDamageDealt(GameActor attacker, GameActor target, int damage)
        {
            if (!EnableAttackHeatGain || IsReleasing) return;
            if (attacker != Actor) return;
            if (DisableHeatGainDuringBuff && _buffActive) return;
            if (FreezeHeatGain) return;
            _decayTimer = DecayDelay;
            float gainMult = _buffActive ? BuffHeatGainMultiplier : 1f;
            Heat = Mathf.Min(Heat + AttackHeatGain * gainMult, GetHeatCap());
        }
    }
}

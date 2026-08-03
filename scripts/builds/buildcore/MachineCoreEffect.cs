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
        [Export(PropertyHint.Range, "0,50,0.5")] public float OverflowBuffPercentPerHeat = 2f; // 每溢出 1 点热量的增益百分比（移动/攻速/受伤）

        /// <summary>当前热量 (0-MaxHeat)，HUD 绑定读取。</summary>
        public float Heat { get; private set; }
        public float HeatRatio => MaxHeat > 0f ? Heat / MaxHeat : 0f;
        public bool IsReleasing { get; private set; }
        /// <summary>Buff 期间热量正在消耗中。</summary>
        public bool IsBuffActive => _buffActive;
        /// <summary>热量保底值：被动衰减不会低于此值。由外部效果设置。</summary>
        public float MinHeat { get; set; }

        private float _releaseCooldownRemaining;
        private float _consumedHeat;
        private float _originalAttackDamage;
        private float _decayTimer;
        private bool _buffActive;
        private bool _wasMovingLastFrame;

        private readonly Dictionary<HeatStat, float> _baseValues = new();
        private readonly Dictionary<HeatStat, Dictionary<string, float>> _modifiers = new();

        private float _baseSpeed;
        private float _baseAttackCooldown;

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

            SetStatValue(stat, baseVal * (1f + sum / 100f));
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

            if (Actor != null)
            {
                _baseSpeed = Actor.Speed;
                _baseAttackCooldown = Actor.AttackCooldown;
            }

            DamageEventBus.Subscribe(OnDamageDealt);
        }

        protected override void OnTick(double delta)
        {
            float dt = (float)delta;
            if (Actor == null) return;

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

            // 热量累积：移动，速度越快积攒越快
            // Buff 期间是否禁用热量获取
            if (DisableHeatGainDuringBuff && _buffActive)
            {
                _wasMovingLastFrame = false;
                return;
            }

            float speed = Actor.Velocity.Length();
            bool moving = speed > 10f;
            if (moving)
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

            ApplyOverflowBuff();
        }

        private float GetHeatCap()
        {
            return AllowHeatOverflow ? float.MaxValue : MaxHeat;
        }

        /// <summary>每溢出 1 点热量：移动速度、攻击速度、受到的伤害 +OverflowBuffPercentPerHeat%。</summary>
        private void ApplyOverflowBuff()
        {
            if (Actor == null) return;
            float overflow = Mathf.Max(Heat - MaxHeat, 0f);

            if (!AllowHeatOverflow || overflow <= 0f)
            {
                Actor.Speed = _baseSpeed;
                Actor.AttackCooldown = _baseAttackCooldown;
                Actor.IncomingDamageMultiplier = 1f;
                return;
            }

            float mult = 1f + overflow * OverflowBuffPercentPerHeat / 100f;
            Actor.Speed = _baseSpeed * mult;
            Actor.AttackCooldown = Mathf.Max(_baseAttackCooldown / mult, 0.01f);
            Actor.IncomingDamageMultiplier = mult;
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

        /// <summary>由外部效果增加热量（默认不超过 MaxHeat，超频开启时无上限）。</summary>
        public void AddHeat(float amount)
        {
            if (amount <= 0f) return;
            Heat = Mathf.Min(Heat + amount, GetHeatCap());
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
                _originalAttackDamage = Actor.AttackDamage;
                _buffActive = true;
            }
            float bonus = _originalAttackDamage * Mathf.Min(_consumedHeat * DamagePerHeat, MaxDamageBonus);
            Actor.AttackDamage = _originalAttackDamage + bonus;
        }

        public override void OnRemoved()
        {
            DamageEventBus.Unsubscribe(OnDamageDealt);
            if (_buffActive && Actor != null)
                Actor.AttackDamage = _originalAttackDamage;
            if (Actor != null)
            {
                Actor.Speed = _baseSpeed;
                Actor.AttackCooldown = _baseAttackCooldown;
                Actor.IncomingDamageMultiplier = 1f;
            }
            base.OnRemoved();
        }

        private void OnDamageDealt(GameActor attacker, GameActor target, int damage)
        {
            if (!EnableAttackHeatGain || IsReleasing) return;
            if (attacker != Actor) return;
            if (DisableHeatGainDuringBuff && _buffActive) return;
            _decayTimer = DecayDelay;
            float gainMult = _buffActive ? BuffHeatGainMultiplier : 1f;
            Heat = Mathf.Min(Heat + AttackHeatGain * gainMult, GetHeatCap());
        }
    }
}

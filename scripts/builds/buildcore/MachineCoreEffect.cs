using Godot;
using Kuros.Core.Effects;

namespace Kuros.Builds.BuildCore
{
    /// <summary>
    /// Machine 核心机制：热量表。
    /// 攻击和移动累积热量，按下核心技能键消耗全部热量换取临时增伤。
    /// </summary>
    [GlobalClass]
    public partial class MachineCoreEffect : ActorEffect
    {
        [ExportCategory("Heat")]
        [Export(PropertyHint.Range, "0,200,1")] public float MaxHeat = 100f;
        [Export(PropertyHint.Range, "0,50,0.5")] public float MoveHeatRate = 3f;
        [Export(PropertyHint.Range, "0,50,0.5")] public float AttackHeatGain = 15f;
        [Export(PropertyHint.Range, "0,20,0.5")] public float DecayRate = 2f;

        [ExportCategory("Release")]
        [Export(PropertyHint.Range, "0,5,0.1")] public float BuffDuration = 3f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float DamagePerHeat = 0.01f;
        [Export(PropertyHint.Range, "0,10,0.1")] public float ReleaseCooldown = 1f;

        /// <summary>当前热量 (0-MaxHeat)，HUD 绑定读取。</summary>
        public float Heat { get; private set; }
        public float HeatRatio => MaxHeat > 0f ? Heat / MaxHeat : 0f;
        public bool IsReleasing { get; private set; }

        private float _releaseCooldownRemaining;
        private float _buffRemaining;
        private float _originalAttackDamage;
        private bool _buffActive;
        private bool _wasMovingLastFrame;

        protected override void OnApply()
        {
            Heat = 0f;
            IsReleasing = false;
            _releaseCooldownRemaining = 0f;
            _buffRemaining = 0f;
            _buffActive = false;
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

            // Buff 计时
            if (_buffActive)
            {
                _buffRemaining -= dt;
                if (_buffRemaining <= 0f)
                {
                    Actor.AttackDamage = _originalAttackDamage;
                    _buffActive = false;
                }
            }

            // 热量累积：移动
            bool moving = Actor.Velocity.Length() > 10f;
            if (moving)
            {
                Heat = Mathf.Min(Heat + MoveHeatRate * dt, MaxHeat);
            }
            else if (Heat > 0f && !IsReleasing)
            {
                Heat = Mathf.Max(Heat - DecayRate * dt, 0f);
            }
            _wasMovingLastFrame = moving;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!IsInstanceValid(this) || Actor == null) return;
            if (_releaseCooldownRemaining > 0f) return;
            if (!@event.IsActionPressed("core_skill") || @event.IsEcho()) return;

            ReleaseHeat();
            GetViewport()?.SetInputAsHandled();
        }

        /// <summary>攻击命中时由外部调用。</summary>
        public void OnAttackLanded()
        {
            if (IsReleasing) return;
            Heat = Mathf.Min(Heat + AttackHeatGain, MaxHeat);
        }

        private void ReleaseHeat()
        {
            if (Heat <= 0f) return;

            float consumed = Heat;
            Heat = 0f;
            IsReleasing = true;
            _releaseCooldownRemaining = ReleaseCooldown;

            // 应用临时增伤 Buff
            if (!_buffActive)
            {
                _originalAttackDamage = Actor.AttackDamage;
                _buffActive = true;
            }
            Actor.AttackDamage = _originalAttackDamage + _originalAttackDamage * consumed * DamagePerHeat;
            _buffRemaining = BuffDuration;
        }

        public override void OnRemoved()
        {
            if (_buffActive && Actor != null)
                Actor.AttackDamage = _originalAttackDamage;
            base.OnRemoved();
        }
    }
}

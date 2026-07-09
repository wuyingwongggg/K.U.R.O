using Godot;
using Kuros.Core.Effects;
using Kuros.Managers;

namespace Kuros.Builds.BuildCore
{
    /// <summary>
    /// Waiter 核心机制：药剂。
    /// 每过 N 秒获取一针药剂，按下核心技能键消耗药剂回血。
    /// </summary>
    [GlobalClass]
    public partial class WaiterCoreEffect : ActorEffect
    {
        [ExportCategory("Medicine")]
        [Export(PropertyHint.Range, "1,30,0.5")] public float MedicineInterval = 5f;
        [Export(PropertyHint.Range, "1,10,1")] public int MaxDoses = 3;
        [Export(PropertyHint.Range, "0.05,1,0.05")] public float HealPercent = 0.2f;

        /// <summary>当前持有药剂数，HUD 绑定读取。</summary>
        public int DoseCount { get; private set; }
        public int MaxDosesValue => MaxDoses;
        public float IntervalProgress => MaxDosesValue > 0 && MedicineInterval > 0f
            ? 1f - (_generationTimer / MedicineInterval)
            : 0f;
        public bool HasDose => DoseCount > 0;

        private float _generationTimer;

        protected override void OnApply()
        {
            DoseCount = 0;
            _generationTimer = MedicineInterval;
        }

        protected override void OnTick(double delta)
        {
            float dt = (float)delta;

            if (DoseCount < MaxDoses)
            {
                _generationTimer -= dt;
                if (_generationTimer <= 0f)
                {
                    DoseCount++;
                    _generationTimer = MedicineInterval;
                }
            }
            else
            {
                _generationTimer = MedicineInterval;
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!IsInstanceValid(this) || Actor == null) return;
            if (!@event.IsActionPressed("core_skill") || @event.IsEcho()) return;
            if (DoseCount <= 0) return;

            DoseCount--;
            int healAmount = Mathf.RoundToInt(Actor.MaxHealth * HealPercent);
            Actor.RestoreHealth(Actor.CurrentHealth + healAmount);
            FloatingDamageTextManager.Instance.ShowFloatingHealing(healAmount, Actor.GlobalPosition);
            GetViewport()?.SetInputAsHandled();
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
        }
    }
}

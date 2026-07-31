using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 惯性驱动：连续向同一方向移动时热量获取速度从 1.0 逐渐递增到目标倍率。
    /// 改变移动方向或停止后倍率重置为 1.0。
    /// TierValues 为每层的目标倍率（1.5 = +50%，1.75 = +75%，2.0 = +100%）。
    /// </summary>
    [GlobalClass]
    public partial class MachineInertiaDriveEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 1.5f, 1.75f, 2.0f };
        [Export(PropertyHint.Range, "0.1,5,0.01")] public float RampSpeed = 0.5f;

        private MachineCoreEffect? _core;
        private float _originalMoveHeatRate;
        private float _currentMultiplier = 1f;
        private int _tier;
        private Vector2 _lastMoveDir;

        private float TargetMultiplier => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _currentMultiplier = 1f;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            _originalMoveHeatRate = _core.MoveHeatRate;
            _lastMoveDir = Vector2.Zero;
        }

        protected override void OnTick(double delta)
        {
            if (_core == null || Actor == null) return;

            Vector2 vel = Actor.Velocity;
            float speed = vel.Length();
            if (speed <= 10f)
            {
                _lastMoveDir = Vector2.Zero;
                _currentMultiplier = 1f;
                _core.MoveHeatRate = _originalMoveHeatRate;
                return;
            }

            Vector2 dir = vel.Normalized();
            if (_lastMoveDir != Vector2.Zero && dir.Dot(_lastMoveDir) < 0.9f)
            {
                _currentMultiplier = 1f;
                _core.MoveHeatRate = _originalMoveHeatRate;
            }
            else
            {
                _currentMultiplier = Mathf.MoveToward(_currentMultiplier, TargetMultiplier, RampSpeed * (float)delta);
                _core.MoveHeatRate = _originalMoveHeatRate * _currentMultiplier;
            }

            _lastMoveDir = dir;
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
        }

        public override void OnRemoved()
        {
            if (_core != null)
                _core.MoveHeatRate = _originalMoveHeatRate;
            base.OnRemoved();
        }
    }
}

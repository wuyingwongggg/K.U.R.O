using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 惯性驱动：连续向同一方向移动时热量获取速度从 0% 逐渐递增到目标百分比。
    /// 改变移动方向或停止后重置为 0%。
    /// TierValues 为每层的目标增幅百分比（50 = +50%，75 = +75%，100 = +100%）。
    /// RampTime 为到达目标所需的固定时间（秒），任意 TierValues 都匀速到达。
    /// 通过 MachineCoreEffect 修改器注册（每帧更新斜坡值）。
    /// </summary>
    [GlobalClass]
    public partial class MachineInertiaDriveEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 50f, 75f, 100f };
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float RampTime = 1f;

        private MachineCoreEffect? _core;
        private float _currentPercent;
        private int _tier;
        private Vector2 _lastMoveDir;

        private float TargetPercent => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _currentPercent = 0f;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (_core == null) return;
            _core.SetStatModifier(MachineCoreEffect.HeatStat.MoveHeatRate, EffectId, 0f);
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
                _currentPercent = 0f;
                _core.SetStatModifier(MachineCoreEffect.HeatStat.MoveHeatRate, EffectId, 0f);
                return;
            }

            Vector2 dir = vel.Normalized();
            if (_lastMoveDir != Vector2.Zero && dir.Dot(_lastMoveDir) < 0.9f)
            {
                _currentPercent = 0f;
            }
            else
            {
                float step = (TargetPercent / Mathf.Max(RampTime, 0.01f)) * (float)delta;
                _currentPercent = Mathf.MoveToward(_currentPercent, TargetPercent, step);
            }

            _core.SetStatModifier(MachineCoreEffect.HeatStat.MoveHeatRate, EffectId, _currentPercent);
            _lastMoveDir = dir;
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
        }

        public override void OnRemoved()
        {
            if (_core != null)
                _core.RemoveStatModifier(MachineCoreEffect.HeatStat.MoveHeatRate, EffectId);
            base.OnRemoved();
        }
    }
}

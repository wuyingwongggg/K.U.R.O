using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 动能增幅（BuildNormal_B_001）：奔跑状态持续越久移动速度越快，最高提升 TierValues 百分比。
    /// 通过 GameActor.SpeedBonusPercent 加法字段增量写入（奔跑期间线性爬升到层百分比），
    /// 与其他速度加成（如超频）加法叠加；停止奔跑/移除时清零自己的贡献。
    /// </summary>
    [GlobalClass]
    public partial class NormalRunRampEffect : ActorEffect
    {
        /// <summary>最高提升百分比（如 15 / 30 表示最高 +15% / +30%）。</summary>
        [Export] public float[] TierValues { get; set; } = { 15f, 30f };
        /// <summary>奔跑多久达到最高提升（秒）。</summary>
        [Export(PropertyHint.Range, "0.5,5,0.1")] public float RampUpSeconds { get; set; } = 2f;

        private GameActor? _actorRef;
        private float _runElapsed;
        private float _lastBonus;
        private int _tier;

        private float CurrentPercent => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _actorRef = Actor;
            _tier = 0;
            _runElapsed = 0f;
            _lastBonus = 0f;
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
        }

        public override void OnRemoved()
        {
            if (_actorRef != null && _lastBonus != 0f)
            {
                _actorRef.SpeedBonusPercent -= _lastBonus;
                _lastBonus = 0f;
            }
            _actorRef = null;
            base.OnRemoved();
        }

        protected override void OnTick(double delta)
        {
            if (_actorRef == null) return;

            bool running = _actorRef.StateMachine?.CurrentState?.Name == "Run";
            if (running)
            {
                _runElapsed += (float)delta;
                float t = Mathf.Min(1f, _runElapsed / Mathf.Max(0.1f, RampUpSeconds));
                float newBonus = CurrentPercent * t;
                _actorRef.SpeedBonusPercent += (newBonus - _lastBonus);
                _lastBonus = newBonus;
            }
            else if (_lastBonus != 0f)
            {
                _runElapsed = 0f;
                _actorRef.SpeedBonusPercent -= _lastBonus; // 停止奔跑清零贡献
                _lastBonus = 0f;
            }
        }
    }
}

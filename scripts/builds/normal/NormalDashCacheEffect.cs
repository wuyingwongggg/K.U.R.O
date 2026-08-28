using Godot;
using Kuros.Actors.Heroes.States;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 闪避缓存（BuildNormal_B_003）：前向闪避结束后的窗口内（TierValues 秒），
    /// 后撤闪避不消耗任何闪避资源（充能 / MachineHeatDashEffect 热量）。
    /// 通过 GameActor.IsDashBackWindowActive 委托注入判定（PlayerDashState 消费）。
    /// </summary>
    [GlobalClass]
    public partial class NormalDashCacheEffect : ActorEffect
    {
        /// <summary>各层免费窗口时长（秒）：前向闪避后多久内后撤闪避免费。</summary>
        [Export] public float[] TierValues { get; set; } = { 1f, 2f };

        private GameActor? _actorRef;
        private PlayerDashState? _dash;
        private ulong _lastSeenEnteredAtMs;
        private ulong _lastForwardDashAtMs;
        private bool _freeUsed;
        private int _tier;

        private float CurrentWindowSeconds => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _actorRef = Actor;
            _dash = Actor?.StateMachine?.GetNodeOrNull<PlayerDashState>("Dash");
            _tier = 0;
            _lastSeenEnteredAtMs = 0;
            _lastForwardDashAtMs = 0;
            if (_actorRef != null)
            {
                _actorRef.IsDashBackWindowActive = IsFreeWindowActive;
            }
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
        }

        public override void OnRemoved()
        {
            if (_actorRef != null)
            {
                _actorRef.IsDashBackWindowActive = null;
            }
            _actorRef = null;
            base.OnRemoved();
        }

        protected override void OnTick(double delta)
        {
            if (_actorRef == null || _dash == null) return;

            // 检测闪避 Enter 时刻变化（含 Reenter 连闪）
            ulong entered = _dash.LastDashEnteredAtMs;
            if (entered != _lastSeenEnteredAtMs)
            {
                _lastSeenEnteredAtMs = entered;
                if (!_dash.LastDashWasBackDash)
                {
                    // 前向闪避开始：启动窗口并重置免费次数
                    _lastForwardDashAtMs = entered;
                    _freeUsed = false;
                }
                else if (IsFreeWindowActive())
                {
                    // 窗口内后撤闪避发生（本次已免费）：锁定，后续后撤不再免费
                    _freeUsed = true;
                }
            }
        }

        /// <summary>免费窗口判定（PlayerDashState 消费）：前向闪避后窗口内且免费次数未使用。</summary>
        private bool IsFreeWindowActive()
        {
            if (_lastForwardDashAtMs == 0 || _freeUsed) return false;
            return Time.GetTicksMsec() - _lastForwardDashAtMs < (ulong)(CurrentWindowSeconds * 1000f);
        }
    }
}

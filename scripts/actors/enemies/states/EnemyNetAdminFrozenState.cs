using Godot;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// NetAdmin 三段式眩晕，收敛到单一 Frozen 状态。
    /// 内部用 StunPhase 驱动 Down→Loop→Up 动画序列。
    /// </summary>
    public partial class EnemyNetAdminFrozenState : EnemyState
    {
        public enum StunPhase { Down, Loop, Up }

        [Export(PropertyHint.Range, "0.1,5,0.01")] public float DownDuration = 0.5f;
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float FrozenDuration = 0.1f;
        [Export(PropertyHint.Range, "0.1,5,0.01")] public float UpDuration = 0.5f;

        public StunPhase CurrentPhase => _phase;

        private StunPhase _phase;
        private float _timer;

        public float GetRemainingTime()
        {
            var freeze = Enemy?.EffectController?.GetEffect<FreezeEffect>();
            if (freeze != null)
                return freeze.GetRemainingDuration();
            return Mathf.Max(_timer, 0f);
        }

        public override void Enter()
        {
            var freeze = Enemy.EffectController?.GetEffect<FreezeEffect>();

            if (freeze != null && Mathf.Abs(freeze.GetRemainingDuration() - freeze.Duration) < 0.01f)
                _phase = StunPhase.Down;
            else if (_phase == StunPhase.Down && freeze != null)
                _phase = StunPhase.Loop;

            _timer = _phase switch
            {
                StunPhase.Down => DownDuration,
                StunPhase.Loop => freeze?.GetRemainingDuration()
                    ?? (Enemy.FrozenStateRemainingTime > 0f ? Enemy.FrozenStateRemainingTime : FrozenDuration),
                StunPhase.Up => UpDuration,
                _ => FrozenDuration
            };

            Enemy.FrozenStateRemainingTime = 0f;
            if (freeze != null && freeze.PendingRemainingTime > 0f)
                freeze.PendingRemainingTime = 0f;

            Enemy.Velocity = Vector2.Zero;
        }

        public override void Exit()
        {
            if (_timer > 0f)
            {
                var freeze = Enemy.EffectController?.GetEffect<FreezeEffect>();
                if (freeze != null)
                    freeze.PendingRemainingTime = _timer;
                else
                    Enemy.FrozenStateRemainingTime = _timer;
            }
            else
            {
                Enemy.FrozenStateRemainingTime = 0f;
                var freeze = Enemy.EffectController?.GetEffect<FreezeEffect>();
                if (freeze != null)
                    freeze.PendingRemainingTime = 0f;
            }

            // Frozen 结束前强制 AttackController 重新评估终极技
            var controller = GetNodeOrNull<Attacks.EnemyD1NetAdminAttackController>(
                "../Attack/AttackController");
            if (controller != null)
            {
                controller.ForceEvaluateUltimate();
                controller.ForceQueueNextAttack("FrozenEnd");
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            Enemy.Velocity = Vector2.Zero;
            Enemy.MoveAndSlide();

            if (_phase == StunPhase.Loop)
            {
                var freeze = Enemy.EffectController?.GetEffect<FreezeEffect>();
                if (freeze != null)
                {
                    float remaining = freeze.GetRemainingDuration();
                    if (remaining <= UpDuration)
                    {
                        _phase = StunPhase.Up;
                        _timer = UpDuration;
                    }
                    else
                    {
                        return;
                    }
                }
            }

            _timer -= (float)delta;
            if (_timer <= 0)
            {
                switch (_phase)
                {
                    case StunPhase.Down:
                        _phase = StunPhase.Loop;
                        var f = Enemy.EffectController?.GetEffect<FreezeEffect>();
                        _timer = f?.GetRemainingDuration() ?? FrozenDuration;
                        break;
                    case StunPhase.Loop:
                        _phase = StunPhase.Up;
                        _timer = UpDuration;
                        break;
                    case StunPhase.Up:
                        _phase = StunPhase.Down;
                        Enemy.AttackTimer = 0f;
                        ChangeState("Idle");
                        break;
                }
            }
        }
    }
}

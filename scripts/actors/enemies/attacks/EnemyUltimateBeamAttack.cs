using Godot;
using Kuros.Actors.Enemies.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// WaiterB 终极激光束攻击。
    /// 攻击期间敌人持续停止移动并始终面向玩家。
    /// 配合 Inspector 中启用 ImmuneToStun / ImmuneToForcedMovement / ImmuneToSpeedSlow，
    /// 可在整个攻击持续时间内免疫玩家施加的控制效果。
    /// </summary>
    public partial class EnemyUltimateBeamAttack : EnemyAttackTemplate
    {
        /// <summary>激光束在 Active 阶段持续的时长（秒）。</summary>
        [Export(PropertyHint.Range, "0,60,0.1")] public float BeamDuration = 3.0f;


        [Export(PropertyHint.Range, "0.1,10,0.1")] public float InterruptFrozenDuration = 2.0f;
        private float _beamTimer;
        private bool _isInBeamPhase;
        private bool _beamFinalized = false;
        public bool IsBeamFinished => _beamFinalized;


        protected override void OnWarmupStarted()
        {
            base.OnWarmupStarted();
            _beamFinalized = false;
            _isInBeamPhase = false;
            SubscribeDamageInterrupt();
        }

        protected override void OnAttackFinished()
        {
            UnsubscribeDamageInterrupt();
            base.OnAttackFinished();
        }

        private void SubscribeDamageInterrupt()
        {
            if (Enemy == null) return;
            Enemy.DamageTaken += OnDamageTakenDuringBeam;
        }

        private void UnsubscribeDamageInterrupt()
        {
            if (Enemy == null) return;
            Enemy.DamageTaken -= OnDamageTakenDuringBeam;
        }

        private void OnDamageTakenDuringBeam(int _)
        {
            if (!IsRunning || Enemy == null) return;
            UnsubscribeDamageInterrupt();
            var frozenState = Enemy.StateMachine?.GetNodeOrNull<EnemyFrozenState>("Frozen");
            if (frozenState != null)
                frozenState.FrozenDuration = InterruptFrozenDuration;
            Enemy.StateMachine?.ChangeState("Frozen");
        }

        protected override void OnActivePhase()
        {
            base.OnActivePhase();
            _beamTimer     = BeamDuration;
            _isInBeamPhase = true;
            _beamFinalized = false;
        }

        /// <summary>Active 阶段计时器耗尽后，只要光束计时器尚未归零就保持挂起。</summary>
        protected override bool ShouldHoldActivePhase() => _isInBeamPhase;

        public override void _PhysicsProcess(double delta)
        {
            if (!IsRunning || Enemy == null) return;

            // 停止移动
            Enemy.Velocity = Vector2.Zero;

            // 持续面向玩家
            if (Player != null)
            {
                bool playerIsRight = Player.GlobalPosition.X >= Enemy.GlobalPosition.X;
                Enemy.FlipFacing(playerIsRight);
            }

            // 倒计时光束持续时间（仅在 Active 阶段挂起期间）
            if (_isInBeamPhase)
            {
                _beamTimer -= (float)delta;
                if (_beamTimer <= 0f)
                {
                    _isInBeamPhase = false;
                    _beamFinalized = true;
                }
            }
        }
    }
}

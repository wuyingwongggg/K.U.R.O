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

        [ExportCategory("Warmup Node Movement")]
        [Export] public NodePath[] MoveNodes { get; set; } = System.Array.Empty<NodePath>();
        [Export(PropertyHint.Range, "1,2000,1")] public float MoveOffsetPx = 100f;

        private float _beamTimer;
        private bool _isInBeamPhase;
        private bool _beamFinalized;
        public bool IsBeamFinished => _beamFinalized;
        private float[] _moveNodeOrigY = System.Array.Empty<float>();
        private Tween?[] _moveTweens = System.Array.Empty<Tween?>();

        protected override void OnWarmupStarted()
        {
            base.OnWarmupStarted();
            _beamFinalized = false;
            _isInBeamPhase = false;
            SubscribeDamageInterrupt();
            SnapshotAndMove(-MoveOffsetPx, WarmupDuration);
        }

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
            MoveToOriginal(RecoveryDuration);
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
            MoveToOriginal(0.1f);
            var frozenState = Enemy.StateMachine?.GetNodeOrNull<EnemyFrozenState>("Frozen");
            if (frozenState != null)
                frozenState.FrozenDuration = InterruptFrozenDuration;
            Enemy.StateMachine?.ChangeState("Frozen");
        }

        protected override void OnActivePhase()
        {
            base.OnActivePhase();
            _beamTimer = BeamDuration;
            _isInBeamPhase = true;
            _beamFinalized = false;
        }

        protected override bool ShouldHoldActivePhase() => _isInBeamPhase;

        public override void _PhysicsProcess(double delta)
        {
            if (!IsRunning || Enemy == null) return;

            Enemy.Velocity = Vector2.Zero;

            if (Player != null)
            {
                bool playerIsRight = Player.GlobalPosition.X >= Enemy.GlobalPosition.X;
                Enemy.FlipFacing(playerIsRight);
            }

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

        private void SnapshotAndMove(float offsetY, float duration)
        {
            if (MoveNodes == null || MoveNodes.Length == 0 || Enemy == null) return;

            _moveNodeOrigY = new float[MoveNodes.Length];
            _moveTweens = new Tween?[MoveNodes.Length];

            for (int i = 0; i < MoveNodes.Length; i++)
            {
                var node = GetNodeOrNull<Node2D>(MoveNodes[i]);
                if (node == null) continue;

                _moveNodeOrigY[i] = node.Position.Y;

                var tween = node.CreateTween();
                tween.TweenProperty(node, "position:y", _moveNodeOrigY[i] + offsetY, duration)
                     .SetEase(Tween.EaseType.InOut);
                _moveTweens[i] = tween;
            }
        }

        private void MoveToOriginal(float duration)
        {
            if (MoveNodes == null || MoveNodes.Length == 0 || Enemy == null) return;

            for (int i = 0; i < MoveNodes.Length && i < _moveTweens.Length; i++)
            {
                _moveTweens[i]?.Kill();
                _moveTweens[i] = null;

                var node = GetNodeOrNull<Node2D>(MoveNodes[i]);
                if (node == null) continue;

                if (i >= _moveNodeOrigY.Length) continue;

                var tween = node.CreateTween();
                tween.TweenProperty(node, "position:y", _moveNodeOrigY[i], duration)
                     .SetEase(Tween.EaseType.InOut);
                _moveTweens[i] = tween;
            }
        }
    }
}

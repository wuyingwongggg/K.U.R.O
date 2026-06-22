using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.Animation
{
    /// <summary>
    /// Enemy_C1_waiterA 专用 Spine 动画控制器，将动画与状态机/攻击模板绑定。
    /// </summary>
    public partial class EnemyC1WaiterASpineAnimationController : EnemySpineAnimationController
    {
        [Export] public NodePath AttackControllerPath { get; set; } = new("StateMachine/Attack/AttackController");
        [Export] public string IdleAnimation = "idle";
        [Export] public string WalkAnimation = "walk";
        [Export] public string AttackAnimation = "attack";
        [Export] public string SkillAnimation = "skill1";
        [Export] public string AssistAnimation = "skill3";
        [Export] public string HitAnimation = "hit";
        [Export] public string StunAnimation = "stun";
        [Export] public string DieAnimation = "death";
        [Export(PropertyHint.Range, "0.1,3,0.1")] public float KeepDistanceTimeScale = 2f;
        private EnemyC1WaiterAAttackController? _attackController;
        private StringComparison _comparison = StringComparison.OrdinalIgnoreCase;
        //private EnemyMoveAttack? _skillChargeMoveAttack;
        private Node? _spineControllerNode;
        private Callable _spineHitCallable;
        private bool _spineHitSubscribed;

        public override void _Ready()
        {
            if (string.IsNullOrEmpty(DefaultLoopAnimation))
            {
                DefaultLoopAnimation = IdleAnimation;
            }

            base._Ready();
        }

        public override void _ExitTree()
        {
            UnsubscribeSpineHitSignal();
            base._ExitTree();
        }

        protected override void OnControllerReady()
        {
            base.OnControllerReady();
            ResolveAttackController();
            EnsureSpineHitSupport();
        }

        protected override float GetPreferredMixDuration()
        {
            return AttackMixDuration;
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            UpdateAnimation();
            TickPartialLoop();
        }

        private void UpdateAnimation()
        {
            if (Enemy?.StateMachine?.CurrentState == null)
            {
                PlayIdle();
                return;
            }

            string stateName = Enemy.StateMachine.CurrentState.Name;
            switch (stateName)
            {
                case "Walk":
                    PlayLoopIfNeeded("Walk", WalkAnimation, WalkMixDuration);
                    break;
                case "Hit":
                    PlayOnceIfNeeded("Hit", HitAnimation, HitMixDuration);
                    break;
                case "Dying":
                    PlayOnceIfNeeded("Die", DieAnimation, DieMixDuration);
                    break;
                case "Frozen":
                    PlayLoopIfNeeded("Frozen", StunAnimation, HitMixDuration);
                    break;
                case "KeepDistance":
                    PlayLoopIfNeeded("KeepDistance", WalkAnimation, WalkMixDuration, KeepDistanceTimeScale);
                    break;
                case "Dead":
                    PlayEmptyIfNeeded();
                    break;
                case "Attack":
                    HandleAttackAnimations();
                    break;
                case "Assist":
                    PlayLoopIfNeeded("Assist", AssistAnimation, WalkMixDuration);
                    break;
                default:
                    PlayIdle();
                    break;
            }
        }

        private void HandleAttackAnimations()
        {
            var controller = ResolveAttackController();
            if (controller == null)
            {
                PlayIdle();
                return;
            }

            string attackName = controller.CurrentAttackName;
            if (!string.IsNullOrEmpty(attackName))
            {   
                if (attackName.Equals(controller.MeleeAttackName, _comparison))
                {
                    PlayOnceIfNeeded("Attack", AttackAnimation, AttackMixDuration);
                    return;
                }

                if (attackName.Equals(controller.ThrowAttackName, _comparison))
                {
                    PlayOnceIfNeeded("Skill1", SkillAnimation, AttackMixDuration);
                    return;
                }

            }

            PlayIdle();
        }

        private void PlayIdle()
        {
            PlayLoopIfNeeded("Idle", IdleAnimation, IdleMixDuration);
        }
        private EnemyC1WaiterAAttackController? ResolveAttackController()
        {
            if (_attackController != null && IsInstanceValid(_attackController))
            {
                return _attackController;
            }

            if (AttackControllerPath.IsEmpty || Enemy == null)
            {
                return null;
            }

            _attackController = GetNodeOrNull<EnemyC1WaiterAAttackController>(AttackControllerPath);
            if (_attackController == null)
            {
                _attackController = Enemy.GetNodeOrNull<EnemyC1WaiterAAttackController>(AttackControllerPath);
            }

            return _attackController;
        }
        // private EnemyMoveAttack? ResolveSkillMoveAttack(EnemyC1WaiterAAttackController controller)
        // {
        //     if (_skillChargeMoveAttack != null && IsInstanceValid(_skillChargeMoveAttack))
        //     {
        //         return _skillChargeMoveAttack;
        //     }

        //     //_skillChargeMoveAttack = controller.GetNodeOrNull<EnemyMoveAttack>(controller.SkillAttackName);
        //     return _skillChargeMoveAttack;
        // }

        private void EnsureSpineHitSupport()
        {
            if (_spineHitSubscribed)
            {
                return;
            }

            if (SpineSpritePath.IsEmpty)
            {
                return;
            }

            _spineControllerNode = GetNodeOrNull(SpineSpritePath) ?? Enemy?.GetNodeOrNull(SpineSpritePath);
            if (_spineControllerNode == null || !_spineControllerNode.HasSignal("hit_received"))
            {
                _spineControllerNode = null;
                return;
            }

            _spineHitCallable = Callable.From<int, string>(OnSpineHitReceived);
            _spineControllerNode.Connect("hit_received", _spineHitCallable);
            _spineHitSubscribed = true;
        }

        private void UnsubscribeSpineHitSignal()
        {
            if (!_spineHitSubscribed || _spineControllerNode == null)
            {
                _spineHitSubscribed = false;
                _spineControllerNode = null;
                return;
            }

            if (_spineControllerNode.IsConnected("hit_received", _spineHitCallable))
            {
                _spineControllerNode.Disconnect("hit_received", _spineHitCallable);
            }

            _spineHitSubscribed = false;
            _spineControllerNode = null;
        }

        private void OnSpineHitReceived(int hitStep, string animationName)
        {
            if (Enemy?.StateMachine?.CurrentState?.Name != "Attack")
            {
                return;
            }

            var controller = ResolveAttackController();
            if (controller == null || string.IsNullOrEmpty(controller.CurrentAttackName))
            {
                return;
            }

            EnemyAttackTemplate? currentAttack = controller.GetNodeOrNull<EnemyAttackTemplate>(controller.CurrentAttackName);
            if (currentAttack == null || !currentAttack.IsRunning)
            {
                return;
            }

            if (!IsExpectedHitAnimation(controller, animationName))
            {
                return;
            }

            if (currentAttack is EnemySimpleMeleeAttack simpleMelee && simpleMelee.RequireAnimationHitTrigger)
            {
                currentAttack.TriggerAnimationHit();
                return;
            }

            currentAttack.TriggerAnimationHit();
        }

        private bool IsExpectedHitAnimation(EnemyC1WaiterAAttackController controller, string animationName)
        {
            string expectedAnimation = string.Empty;
            if (controller.CurrentAttackName.Equals(controller.MeleeAttackName, _comparison))
            {
                expectedAnimation = AttackAnimation;
            }
            // else if (controller.CurrentAttackName.Equals(controller.SkillAttackName, _comparison))
            // {
            //     expectedAnimation = SkillAnimation;
            // }

            if (string.IsNullOrEmpty(expectedAnimation))
            {
                return true;
            }

            return string.Equals(animationName, expectedAnimation, _comparison);
        }

    }
}



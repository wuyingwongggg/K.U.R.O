using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.States
{
	/// <summary>罐子协助失败触发的炮台眩晕：计时结束回 Idle。
	/// 动画由 EnemyF1RogueAITurretSpineAnimationController 映射为 preheat（暂无专用 stun 片）。</summary>
	public partial class EnemyJarAssistStunState : EnemyState
	{
		[Export(PropertyHint.Range, "0,20,0.1")] public float Duration { get; set; } = 7.5f;

		private float _timer;
		private EnemyF1RogueAITurretAttackController? _controller;
		private bool _previousIgnoreHitStateOnDamage;

		public override void Enter()
		{
			_timer = Mathf.Max(0.1f, Duration);
			Enemy.Velocity = Vector2.Zero;

			// 眩晕是输出窗口：受击照常扣血，但不让 Hit 状态顶掉眩晕计时。
			_previousIgnoreHitStateOnDamage = Enemy.IgnoreHitStateOnDamage;
			Enemy.IgnoreHitStateOnDamage = true;

			// 炮台 AI 会在空闲循环里强制切回 Attack，必须暂停攻击控制器，否则眩晕会被立刻顶掉。
			_controller = Enemy.GetNodeOrNull<EnemyF1RogueAITurretAttackController>("StateMachine/Attack/AttackController");
			if (_controller != null)
			{
				_controller.Cancel(clearCooldown: true);
				_controller.SetPhysicsProcess(false);
			}
		}

		public override void Exit()
		{
			Enemy.IgnoreHitStateOnDamage = _previousIgnoreHitStateOnDamage;
			if (_controller != null && GodotObject.IsInstanceValid(_controller))
				_controller.SetPhysicsProcess(true);
			_controller = null;
		}

		public override void PhysicsUpdate(double delta)
		{
			Enemy.Velocity = Vector2.Zero;
			_timer -= (float)delta;
			if (_timer <= 0f)
				ChangeState("Idle");
		}
	}
}

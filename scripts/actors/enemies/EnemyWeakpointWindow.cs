using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// 机械桩族"弱点窗口"组件（cannon / machinegun 已挂，以后的 rogueAI 要窗口就同样挂一个）：
	/// 常态免伤类敌人的**可受击窗口按"正在跑的招"细分**——攻击名 → (起, 止) 秒（从该招起手 TryStart 计），
	/// 只有表内且落在窗口内时才开放受击；**表里没列出的招 = 整个攻击期间开放**（回退现状）。
	/// 没有招在跑（招间空档 / 被打断后）→ 关闭。
	///
	/// 计时自包含：每帧观测 AttackController 下"在跑的招"（与动画控制器同款解析），换招即清零重计——
	/// 攻击脚本零依赖、零改动。消费方：宿主敌人的 <c>CanBeAffected</c>
	/// （<see cref="EnemyF1RogueAITurret"/> 经 FindChildByType 取本组件；缺失时回退"整个 Attack 状态可受击"）。
	/// </summary>
	public partial class EnemyWeakpointWindow : Node
	{
		/// <summary>弱点窗口表：攻击名（AttackName）→ (起, 止) 秒，从该招起手计。
		/// 未列出的招 = 整个攻击期间开放；空表 = 所有招整个攻击期间开放（等价现状）。</summary>
		[Export] public Godot.Collections.Dictionary<string, Vector2> WindowTable { get; set; } = new();

		/// <summary>弱点是否开放（宿主 CanBeAffected 读这个）。首帧前为 false（常态免伤）。</summary>
		public bool IsOpen { get; private set; }

		private SampleEnemy? _enemy;
		private EnemyAttackController? _controller;
		private EnemyAttackTemplate? _running;
		private float _elapsed;

		public override void _Ready()
		{
			_enemy = GetParent() as SampleEnemy;
			if (_enemy == null)
				GD.PushWarning($"{Name}: 必须挂在 SampleEnemy 下（与 FacingController 同款约定）");
		}

		public override void _PhysicsProcess(double delta)
		{
			if (_enemy == null || !GodotObject.IsInstanceValid(_enemy)) return;

			var attack = ResolveRunningAttack();
			if (attack != _running)
			{
				// 换招（或从无到有 / 收招）→ 重置计时："从该招起手计"的起点
				_running = attack;
				_elapsed = 0f;
			}
			else if (attack != null)
			{
				_elapsed += (float)delta;
			}

			IsOpen = attack != null && IsWindowOpenFor(attack);
		}

		private bool IsWindowOpenFor(EnemyAttackTemplate attack)
		{
			if (WindowTable == null || WindowTable.Count == 0) return true;
			if (!WindowTable.TryGetValue(attack.AttackName, out Vector2 window)) return true;
			return _elapsed >= window.X && _elapsed < window.Y;
		}

		/// <summary>在跑的招：状态机在 Attack 且 AttackController 子节点里有 IsRunning 的模板。
		/// 解析与动画控制器 ResolveRunningTemplate 同款；控制器节点缓存、失效时重找。</summary>
		private EnemyAttackTemplate? ResolveRunningAttack()
		{
			if (_enemy!.StateMachine?.CurrentState?.Name != "Attack") return null;

			if (_controller == null || !GodotObject.IsInstanceValid(_controller))
				_controller = _enemy.GetNodeOrNull<EnemyAttackController>("StateMachine/Attack/AttackController");
			if (_controller == null) return null;

			foreach (var child in _controller.GetChildren())
				if (child is EnemyAttackTemplate template && template.IsRunning)
					return template;
			return null;
		}
	}
}

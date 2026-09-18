using Godot;

namespace Kuros.Controllers
{
	/// <summary>
	/// X 轴跟随（Marker 限位）：**只写 X** —— 平滑跟随玩家的横向位置，并夹在场景里两个 Marker2D 的 X 区间内；
	/// Y 完全不碰（保持场景摆放的高度），典型用法是关卡里的招牌 / 指示牌随玩家横移（F_begin 的 exit_sign）。
	/// 放在 _Process（与 CameraFollow 同帧）避免与摄像机产生相对抖动；
	/// 平滑用指数趋近 1-e^(-k·dt) 而不是固定 Lerp 系数，帧率变化时手感一致。
	/// </summary>
	[GlobalClass]
	public partial class PlayerXRangeFollower : Sprite2D
	{
		[ExportCategory("跟随")]
		/// <summary>被跟随的玩家路径；留空 = 用 "player" 组（与其它系统同源）。</summary>
		[Export] public NodePath PlayerPath { get; set; } = new();
		/// <summary>跟随速度（指数趋近系数，每秒）：越大越紧跟；0 = 不平滑，直接贴合目标。</summary>
		[Export(PropertyHint.Range, "0,30,0.1")] public float FollowSpeed { get; set; } = 5f;
		/// <summary>额外横向偏移（目标 X = 玩家 X + 偏移）。</summary>
		[Export] public float XOffset { get; set; } = 0f;

		[ExportCategory("限位 Marker")]
		/// <summary>行程两端的 Marker2D（相对本节点）；顺序无所谓，内部按 X 排序取区间。</summary>
		[Export] public NodePath MarkerAPath { get; set; } = new();
		[Export] public NodePath MarkerBPath { get; set; } = new();

		/// <summary>找不到玩家时的宽限时长（秒）：玩家可能比关卡晚一两帧才生成，过了宽限再打一次提示。</summary>
		private const float PlayerMissingWarnDelay = 1f;

		private Node2D? _player;
		private Node2D? _markerA;
		private Node2D? _markerB;
		private float _rangeLo;
		private float _rangeHi;
		private bool _rangeReady;
		private bool _rangeWarned;
		private float _playerMissingSeconds;
		private bool _playerWarnLogged;

		public override void _Process(double delta)
		{
			RefreshPlayer((float)delta);
			UpdateRange();   // 每帧现算：见方法注释（关卡摆位时序 / Marker 被动画挪动）
			if (_player == null || !_rangeReady) return;

			float targetX = Mathf.Clamp(_player.GlobalPosition.X + XOffset, _rangeLo, _rangeHi);
			float t = FollowSpeed <= 0f ? 1f : 1f - Mathf.Exp(-FollowSpeed * (float)delta);

			// 只写 X：Y 不碰（场景摆放高度即"锁死"），也不会抹掉父级（如 Environments）的摆放
			var pos = GlobalPosition;
			pos.X = Mathf.Lerp(pos.X, targetX, t);
			GlobalPosition = pos;
		}

		/// <summary>供 AnimationPlayer 的 method 轨道调用：销毁本节点（如"玩家离开后牌子消失"）。
		/// 带存在性守卫——节点已脱离场景树 / 已入队删除时直接忽略，
		/// 方法轨被重复触发（循环动画、多条轨道）或节点已被别的步骤删掉时都不会报错。</summary>
		public void DestroySelf()
		{
			if (!IsInsideTree() || IsQueuedForDeletion()) return;
			QueueFree();
		}

		/// <summary>玩家解析：显式路径优先，否则用 "player" 组；每帧重试（玩家可能晚于本节点生成）。
		/// 超过宽限仍找不到就打一次提示——"节点不动"最常见的原因就是树里没有玩家（单独运行关卡场景时正常）。</summary>
		private void RefreshPlayer(float delta)
		{
			if (_player != null && GodotObject.IsInstanceValid(_player)) return;

			_player = PlayerPath != null && !PlayerPath.IsEmpty ? GetNodeOrNull<Node2D>(PlayerPath) : null;
			_player ??= GetTree()?.GetFirstNodeInGroup("player") as Node2D;
			if (_player != null) return;

			_playerMissingSeconds += delta;
			if (_playerWarnLogged || _playerMissingSeconds < PlayerMissingWarnDelay) return;

			_playerWarnLogged = true;
			GD.PushWarning($"{Name}: 未找到玩家（player 组为空，PlayerPath='{PlayerPath}'），本节点不会移动；"
				+ "单独运行关卡场景 / 编辑器预览时没有玩家属正常，正常从舞台流程进入时请检查玩家是否已入组。");
		}

		/// <summary>限位区间**每帧现算**（只缓存 Marker 节点本身）：
		///   ① 关卡会被 StageGeneratorManager 在 _Ready 之后才摆位（先 AddChild → 再设 room.Position），
		///      缓存的全局坐标会整体偏掉，夹取区间跟着缩水；
		///   ② Marker 也可能被动画/父节点挪动。
		/// 区间按 X 排序取值，两端的命名/摆放顺序都不影响结果。</summary>
		private void UpdateRange()
		{
			if (_markerA == null || !GodotObject.IsInstanceValid(_markerA))
				_markerA = MarkerAPath != null && !MarkerAPath.IsEmpty ? GetNodeOrNull<Node2D>(MarkerAPath) : null;
			if (_markerB == null || !GodotObject.IsInstanceValid(_markerB))
				_markerB = MarkerBPath != null && !MarkerBPath.IsEmpty ? GetNodeOrNull<Node2D>(MarkerBPath) : null;

			if (_markerA == null || _markerB == null)
			{
				_rangeReady = false;
				if (!_rangeWarned)
				{
					GD.PushWarning($"{Name}: 限位 Marker 未配置或找不到（{MarkerAPath} / {MarkerBPath}），本节点不会移动");
					_rangeWarned = true;
				}
				return;
			}

			_rangeLo = Mathf.Min(_markerA.GlobalPosition.X, _markerB.GlobalPosition.X);
			_rangeHi = Mathf.Max(_markerA.GlobalPosition.X, _markerB.GlobalPosition.X);
			_rangeReady = true;
		}
	}
}

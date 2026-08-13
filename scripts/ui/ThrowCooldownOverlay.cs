using Godot;

namespace Kuros.UI
{
	/// <summary>
	/// 投掷武器 / 闪避冷却扇形遮罩控件。
	/// 从 12 点钟方向顺时针绘制半透明扇形，覆盖图标表示冷却剩余时间。
	/// 可用于场景节点，也可运行时动态创建（BattleHUD 快捷栏冷却遮罩）。
	/// </summary>
	public partial class ThrowCooldownOverlay : Control
	{
		private float _progress;

		/// <summary>冷却进度 0-1（0 = 无遮罩，1 = 完全覆盖）。</summary>
		public float Progress
		{
			get => _progress;
			set { _progress = Mathf.Clamp(value, 0f, 1f); QueueRedraw(); }
		}

		public override void _Ready()
		{
			base._Ready();
			MouseFilter = MouseFilterEnum.Ignore;
		}

		public override void _Draw()
		{
			base._Draw();
			if (_progress <= 0f) return;

			Vector2 rectSize = Size;
			Vector2 center = rectSize * 0.5f;
			Vector2 halfSize = rectSize * 0.5f;

			var overlayColor = new Color(0f, 0f, 0f, 0.5f);

			int steps = 48;
			float startAngle = -Mathf.Pi / 2f; // 从12点钟方向开始
			float endAngle = startAngle + Mathf.Pi * 2f * _progress;
			var points = new Vector2[steps + 2];
			points[0] = center;
			for (int i = 0; i <= steps; i++)
			{
				float t = (float)i / steps;
				float angle = Mathf.Lerp(startAngle, endAngle, t);
				Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
				points[i + 1] = center + GetRectEdgePoint(dir, halfSize);
			}
			DrawPolygon(points, new Color[] { overlayColor });
		}

		private static Vector2 GetRectEdgePoint(Vector2 direction, Vector2 halfSize)
		{
			if (direction == Vector2.Zero) return Vector2.Zero;
			float tx = direction.X != 0f ? halfSize.X / Mathf.Abs(direction.X) : float.MaxValue;
			float ty = direction.Y != 0f ? halfSize.Y / Mathf.Abs(direction.Y) : float.MaxValue;
			return direction * Mathf.Min(tx, ty);
		}

		public override void _Notification(int what)
		{
			base._Notification(what);
			if (what == NotificationResized) QueueRedraw();
		}
	}
}

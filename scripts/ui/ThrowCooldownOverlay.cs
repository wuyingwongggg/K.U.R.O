using Godot;

namespace Kuros.UI
{
	/// <summary>
	/// 投掷武器 / 充能冷却扇形遮罩控件。
	/// 从 12 点钟方向顺时针绘制半透明扇形，覆盖图标表示冷却剩余时间。
	/// 可用于场景节点，也可运行时动态创建（BattleHUD 快捷栏冷却遮罩）。
	/// 素材化：提供 MaskTexture（配合 cooldown_mask.gdshader）可整体替换遮罩视觉；
	/// 无素材时回退为程序绘制的半透明扇形（颜色由 MaskColor 控制，默认黑 0.5）。
	/// </summary>
	public partial class ThrowCooldownOverlay : Control
	{
		/// <summary>遮罩素材（方形图即可,经 shader 按扇形裁剪）。null = 程序绘制兜底。</summary>
		[Export] public Texture2D? MaskTexture { get; set; }
		/// <summary>遮罩颜色（程序绘制时即填充色;素材模式为叠加色,默认白不改变素材）。</summary>
		[Export] public Color MaskColor { get; set; } = new Color(0f, 0f, 0f, 0.5f);

		private float _progress;
		private ShaderMaterial? _maskMaterial;

		/// <summary>冷却进度 0-1（0 = 无遮罩，1 = 完全覆盖）。</summary>
		public float Progress
		{
			get => _progress;
			set
			{
				_progress = Mathf.Clamp(value, 0f, 1f);
				if (_maskMaterial != null)
					_maskMaterial.SetShaderParameter("u_progress", _progress);
				QueueRedraw();
			}
		}

		public override void _Ready()
		{
			base._Ready();
			MouseFilter = MouseFilterEnum.Ignore;
			RebuildMaterial();
		}

		/// <summary>切换素材后调用（编辑器换图/运行时赋值后重建材质）。</summary>
		public void RebuildMaterial()
		{
			if (MaskTexture == null)
			{
				_maskMaterial = null;
				Material = null;
				QueueRedraw();
				return;
			}

			_maskMaterial ??= new ShaderMaterial
			{
				Shader = GD.Load<Shader>("res://shaders/ui/cooldown_mask.gdshader"),
			};
			_maskMaterial.SetShaderParameter("u_mask", MaskTexture);
			_maskMaterial.SetShaderParameter("u_tint", MaskColor);
			_maskMaterial.SetShaderParameter("u_progress", _progress);
			Material = _maskMaterial;
			QueueRedraw();
		}

		public override void _Draw()
		{
			base._Draw();
			if (_progress <= 0f) return;
			if (MaskTexture != null) return; // 素材模式由 shader 渲染

			Vector2 rectSize = Size;
			Vector2 center = rectSize * 0.5f;
			Vector2 halfSize = rectSize * 0.5f;

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

			// 颜色数组与顶点等长(单元素数组在部分平台不渲染)
			var colors = new Color[points.Length];
			System.Array.Fill(colors, MaskColor);
			DrawPolygon(points, colors);
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

using System.Collections.Generic;
using Godot;

namespace Kuros.UI
{
	/// <summary>
	/// Dash 充能点图标（横向并排）：每个闪避充能对应一个点。
	/// 可用（点序号 < 当前充能数）显示亮色贴图，CD 计时中显示暗色贴图。
	/// 消耗时最右侧亮点变暗、恢复时从右往左逐个变亮（左侧为可用点，右侧为 CD 点）。
	/// 由 BattleHUD.UpdateDashDisplay 每帧驱动 SetCharges。
	/// </summary>
	public partial class DashIconControl : HBoxContainer
	{
		/// <summary>亮色闪避点贴图（充能可用）。</summary>
		[Export(PropertyHint.File, "*.png,*.svg")] public string ActiveTexturePath { get; set; } = "res://assets/ui/dash/闪避条.png";
		/// <summary>暗色闪避点贴图（CD 计时）。</summary>
		[Export(PropertyHint.File, "*.png,*.svg")] public string CooldownTexturePath { get; set; } = "res://assets/ui/dash/闪避条底色.png";
		/// <summary>单个闪避点显示尺寸。</summary>
		[Export] public Vector2 PointSize { get; set; } = new Vector2(24, 24);
		/// <summary>闪避点之间的间距。</summary>
		[Export(PropertyHint.Range, "0,30,1")] public int PointSeparation { get; set; } = 6;

		private readonly List<TextureRect> _points = new();
		private Texture2D? _activeTex;
		private Texture2D? _cooldownTex;

		public override void _Ready()
		{
			AddThemeConstantOverride("separation", PointSeparation);
			_activeTex = GD.Load<Texture2D>(ActiveTexturePath);
			_cooldownTex = GD.Load<Texture2D>(CooldownTexturePath);
		}

		/// <summary>
		/// 更新充能点显示：总点数与 maxCharges 对齐，左侧 activeCount 个点用亮色贴图，
		/// 其余用暗色贴图（超出 maxCharges 的多余点隐藏）。
		/// </summary>
		public void SetCharges(int activeCount, int maxCharges)
		{
			EnsurePointCount(maxCharges);
			activeCount = Mathf.Clamp(activeCount, 0, maxCharges);

			for (int i = 0; i < _points.Count; i++)
			{
				var point = _points[i];
				bool visible = i < maxCharges;
				point.Visible = visible;
				if (!visible) continue;
				point.Texture = i < activeCount ? _activeTex : _cooldownTex;
			}
		}

		private void EnsurePointCount(int count)
		{
			while (_points.Count < count)
			{
				var point = new TextureRect
				{
					CustomMinimumSize = PointSize,
					ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
					StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
					TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
					MouseFilter = MouseFilterEnum.Ignore,
					SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
					SizeFlagsVertical = SizeFlags.ShrinkCenter,
				};
				AddChild(point);
				_points.Add(point);
			}
		}
	}
}

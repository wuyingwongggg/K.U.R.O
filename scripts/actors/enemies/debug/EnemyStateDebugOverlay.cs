using Godot;

/// <summary>
/// 敌人状态调试覆盖层的绘制节点：由 <see cref="SampleEnemy"/> 在开启 EnableStateDebugOverlay 时按需创建。
/// 单独做成节点只为一件事——**自己的 z 索引**（ZAsRelative = false + DebugOverlayZIndex）：
/// 直接画在敌人身上时，覆盖层的 z 会跟着敌人自己的 z_index（以及父级，如滑槽的 z）走，
/// 例如挂在 z = -1 的滑槽机械上就会被背景盖住、看不见。
/// 绘制坐标系与敌人根节点完全一致（自身 position 为 0 + 继承敌人 transform），
/// 所以 DebugOverlayOffset / FontSize / Color 的语义与原来画在敌人身上时相同。
/// </summary>
public partial class EnemyStateDebugOverlay : Node2D
{
	/// <summary>被绘制文本的主人（由 SampleEnemy 在创建时回填）。</summary>
	public SampleEnemy? Enemy { get; set; }

	public override void _Draw()
	{
		if (Enemy == null || !GodotObject.IsInstanceValid(Enemy)) return;

		var font = ThemeDB.FallbackFont;
		if (font == null) return;

		string[] lines = Enemy.DebugOverlayText.Split('\n');
		Vector2 pos = Enemy.DebugOverlayOffset;
		float lineHeight = Enemy.DebugOverlayFontSize + 4f;
		foreach (string line in lines)
		{
			DrawString(font, pos, line, HorizontalAlignment.Left, -1f, Enemy.DebugOverlayFontSize, Enemy.DebugOverlayColor);
			pos.Y += lineHeight;
		}
	}
}

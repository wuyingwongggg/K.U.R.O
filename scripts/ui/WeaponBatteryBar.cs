using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 武器电量竖条 UI（CanvasLayer 屏幕空间）：跟随玩家显示在身旁。
    /// 跟随公式与 CoreHUD 一致：视口中心 + (玩家位置 - 相机中心) × zoom + 偏移，指数平滑。
    /// CanvasLayer 无变换，渲染不受父节点（玩家）transform 与相机影响，位置由本脚本每帧计算。
    /// </summary>
    public partial class WeaponBatteryBar : CanvasLayer
    {
        /// <summary>电池条相对玩家屏幕位置的偏移（屏幕像素）。</summary>
        [Export] public Vector2 PlayerAnchorOffset { get; set; } = new(-64f, -130f);
        /// <summary>跟随平滑速度（越大越跟手，帧率无关的指数平滑）。</summary>
        [Export(PropertyHint.Range, "0.1,100,0.1")] public float SmoothSpeed { get; set; } = 12f;
        [Export] public NodePath BatteryPanelPath { get; set; } = new("BatteryPanel");
        [Export] public NodePath BatteryFillPath { get; set; } = new("BatteryPanel/BatteryFill");
        [Export] public NodePath BatteryValueLabelPath { get; set; } = new("BatteryPanel/BatteryValueLabel");

        private Control? _panel;
        private ColorRect? _fill;
        private Label? _label;
        private bool _positionInitialized;
        private bool _wasVisible;

        public override void _Ready()
        {
            _panel = BatteryPanelPath != null && !BatteryPanelPath.IsEmpty
                ? GetNodeOrNull<Control>(BatteryPanelPath) : null;
            _fill = BatteryFillPath != null && !BatteryFillPath.IsEmpty
                ? GetNodeOrNull<ColorRect>(BatteryFillPath) : null;
            _label = BatteryValueLabelPath != null && !BatteryValueLabelPath.IsEmpty
                ? GetNodeOrNull<Label>(BatteryValueLabelPath) : null;
        }

        /// <summary>更新电量显示：填充高度按比例从下往上，电量低时变红。</summary>
        public void SetCharge(float current, float max)
        {
            if (_fill == null || _label == null) return;

            float ratio = max > 0f ? Mathf.Clamp(current / max, 0f, 1f) : 0f;
            _fill.AnchorTop = 1f - ratio;
            _fill.Color = ratio < 0.25f
                ? new Color(1f, 0.35f, 0.3f, 1f)
                : new Color(0.3f, 0.9f, 0.45f, 1f);
            _label.Text = $"{Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}";
        }

        public override void _Process(double delta)
        {
            // 重新显示时立即定位（避免从上次隐藏的旧位置平滑飞入）
            if (Visible && !_wasVisible)
            {
                _positionInitialized = false;
            }
            _wasVisible = Visible;

            if (_panel == null || !Visible) return;

            var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            var camera = GetViewport().GetCamera2D();
            if (player == null || camera == null) return;

            Vector2 targetPos = GetViewport().GetVisibleRect().Size * 0.5f
                + (player.GlobalPosition - camera.GetScreenCenterPosition()) * camera.Zoom
                + PlayerAnchorOffset;

            if (!_positionInitialized)
            {
                _positionInitialized = true;
                _panel.Position = targetPos;
                return;
            }

            float t = 1f - Mathf.Exp(-SmoothSpeed * (float)delta);
            _panel.Position = _panel.Position.Lerp(targetPos, t);
        }
    }
}

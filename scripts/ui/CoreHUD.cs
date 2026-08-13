using Godot;
using Godot.Collections;
using Kuros.Builds.BuildCore;
using Kuros.Systems;

namespace Kuros.UI
{
    public partial class CoreHUD : Control
    {
        [Export] public Control? MachinePanel { get; set; }
        [Export] public Control? WaiterPanel { get; set; }
        [Export] public Control? ThrowPanel { get; set; }

        [ExportGroup("Machine Heat Bar")]
        [Export] public TextureProgressBar? HeatBar { get; set; }
        [Export] public TextureProgressBar? HeatFillBar { get; set; }
        [Export] public Label? HeatValueLabel { get; set; }

        [ExportGroup("Follow Player")]
        /// <summary>核心面板跟随玩家显示（世界坐标 → 屏幕坐标），关闭则按场景锚点定位（默认左下角）。</summary>
        [Export] public bool FollowPlayer { get; set; } = true;
        /// <summary>面板相对玩家位置的屏幕偏移（如 (0, -90) 显示在玩家头顶上方）。</summary>
        [Export] public Vector2 PlayerAnchorOffset { get; set; } = new Vector2(0, -90);
        /// <summary>跟随平滑速度（越大跟得越紧，越小越滞后；指数平滑，值越大越灵敏）。</summary>
        [Export(PropertyHint.Range, "1,30,0.5")] public float SmoothSpeed { get; set; } = 10f;

        private readonly Dictionary<string, Control?> _panelMap = new();
        private MachineCoreEffect? _boundMachineCore;
        private float _baseFillScaleX = 1f;
        private bool _anchorsReset;

        public override void _Ready()
        {
            _panelMap[BuildClassConstants.Machine] = MachinePanel;
            _panelMap[BuildClassConstants.Waiter] = WaiterPanel;
            _panelMap[BuildClassConstants.Throw] = ThrowPanel;

            // 当前场景结构：HeatFillBar 是 MachinePanel 的直接子节点（单个 TextureProgressBar 条）
            HeatBar ??= MachinePanel?.GetNodeOrNull<TextureProgressBar>("HeatFillBar");
            HeatFillBar ??= HeatBar;
            HeatValueLabel ??= MachinePanel?.GetNodeOrNull<Label>("HeatValueLabel");

            if (HeatFillBar != null)
                _baseFillScaleX = HeatFillBar.Scale.X;
        }

        public void BindMachineCore(MachineCoreEffect? effect)
        {
            _boundMachineCore = effect;
        }

        public override void _Process(double delta)
        {
            // 跟随玩家：将可见核心面板定位到玩家附近（世界坐标 → 屏幕坐标）
            if (FollowPlayer)
                UpdateFollowPlayer((float)delta);

            if (HeatBar == null || HeatFillBar == null)
                return;
            if (_boundMachineCore == null || !IsInstanceValid(_boundMachineCore))
                return;
            if (MachinePanel == null || !MachinePanel.Visible)
                return;

            float maxHeat = _boundMachineCore.MaxHeat;
            float heat = _boundMachineCore.Heat;
            HeatBar.MaxValue = maxHeat;
            HeatBar.Value = heat;
            HeatFillBar.MaxValue = maxHeat;
            HeatFillBar.Value = heat;

            // 爆表：条 Scale.X 跟随溢出量实时放大（1 → 1 + overflow/MaxHeat，最大 1.5 倍）
            float overflow = Mathf.Max(heat - maxHeat, 0f);
            float factor = 1f + (maxHeat > 0f ? overflow / maxHeat : 0f);
            var fillScale = HeatFillBar.Scale;
            fillScale.X = _baseFillScaleX * factor;
            HeatFillBar.Scale = fillScale;

            if (HeatValueLabel != null)
                HeatValueLabel.Text = $"{(int)heat}/{(int)maxHeat}";
        }

        /// <summary>
        /// 把当前可见的核心面板定位到玩家位置附近（指数平滑跟随）。
        /// 玩家世界坐标经相机转屏幕坐标 + 偏移；首次调用时把面板锚点重置为 TopLeft
        /// （手动定位模式，避免场景左下角锚点干扰）并直接定位（不插值，防止从原点飞入）。
        /// </summary>
        private void UpdateFollowPlayer(float delta)
        {
            if (MachinePanel == null) return;

            var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            var camera = GetViewport().GetCamera2D();
            if (player == null || camera == null) return;

            // 世界坐标 → 屏幕坐标：视口中心 + (玩家位置 - 相机中心) × 缩放
            Vector2 targetPos = GetViewport().GetVisibleRect().Size * 0.5f
                + (player.GlobalPosition - camera.GetScreenCenterPosition()) * camera.Zoom
                + PlayerAnchorOffset;

            if (!_anchorsReset)
            {
                _anchorsReset = true;
                MachinePanel.AnchorLeft = 0;
                MachinePanel.AnchorTop = 0;
                MachinePanel.AnchorRight = 0;
                MachinePanel.AnchorBottom = 0;
                MachinePanel.Position = targetPos; // 首次直接定位，避免从原点插值飞入
                return;
            }

            // 指数平滑：SmoothSpeed 越大跟得越紧；帧率无关（1 - exp(-speed × delta)）
            float t = 1f - Mathf.Exp(-SmoothSpeed * delta);
            MachinePanel.Position = MachinePanel.Position.Lerp(targetPos, t);
        }

        public void ShowFor(string buildClass)
        {
            _boundMachineCore = null;
            HideAll();
            if (_panelMap.TryGetValue(buildClass, out var panel) && panel != null)
                panel.Visible = true;
        }

        public void HideAll()
        {
            foreach (var panel in _panelMap.Values)
            {
                if (panel != null) panel.Visible = false;
            }
        }
    }
}

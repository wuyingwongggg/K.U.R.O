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

        private readonly Dictionary<string, Control?> _panelMap = new();
        private MachineCoreEffect? _boundMachineCore;
        private float _baseFillScaleX = 1f;

        public override void _Ready()
        {
            _panelMap[BuildClassConstants.Machine] = MachinePanel;
            _panelMap[BuildClassConstants.Waiter] = WaiterPanel;
            _panelMap[BuildClassConstants.Throw] = ThrowPanel;

            HeatBar ??= MachinePanel?.GetNodeOrNull<TextureProgressBar>("HeatBar");
            HeatFillBar ??= MachinePanel?.GetNodeOrNull<TextureProgressBar>("HeatBar/HeatFillBar");
            HeatValueLabel ??= MachinePanel?.GetNodeOrNull<Label>("HeatBar/HeatValueLabel");

            if (HeatFillBar != null)
                _baseFillScaleX = HeatFillBar.Scale.X;
        }

        public void BindMachineCore(MachineCoreEffect? effect)
        {
            _boundMachineCore = effect;
        }

        public override void _Process(double delta)
        {
            if (HeatBar == null || HeatFillBar == null)
                return;
            if (_boundMachineCore == null || !IsInstanceValid(_boundMachineCore))
                return;
            if (MachinePanel == null || !MachinePanel.Visible)
                return;

            // 非爆表段：旧逻辑（MaxValue = MaxHeat，Value = Heat，maxHeat 时满条）
            HeatBar.MaxValue = _boundMachineCore.MaxHeat;
            HeatBar.Value = _boundMachineCore.Heat;
            HeatFillBar.MaxValue = _boundMachineCore.MaxHeat;
            HeatFillBar.Value = _boundMachineCore.Heat;

            // 爆表段（新增）：条 Scale.X 跟随溢出量实时放大（1 → 1 + overflow/MaxHeat，最大 1.5 倍），
            // 溢出部分显示在放大后的条上；非爆表时还原基准尺寸
            float maxHeat = _boundMachineCore.MaxHeat;
            float heat = _boundMachineCore.Heat;
            float overflow = Mathf.Max(heat - maxHeat, 0f);
            float factor = 1f + (maxHeat > 0f ? overflow / maxHeat : 0f);
            var fillScale = HeatFillBar.Scale;
            fillScale.X = _baseFillScaleX * factor;
            HeatFillBar.Scale = fillScale;

            if (HeatValueLabel != null)
                HeatValueLabel.Text = $"{(int)_boundMachineCore.Heat}/{(int)_boundMachineCore.MaxHeat}";
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

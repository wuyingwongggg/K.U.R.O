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

        public override void _Ready()
        {
            _panelMap[BuildClassConstants.Machine] = MachinePanel;
            _panelMap[BuildClassConstants.Waiter] = WaiterPanel;
            _panelMap[BuildClassConstants.Throw] = ThrowPanel;

            HeatBar ??= MachinePanel?.GetNodeOrNull<TextureProgressBar>("HeatBar");
            HeatFillBar ??= MachinePanel?.GetNodeOrNull<TextureProgressBar>("HeatBar/HeatFillBar");
            HeatValueLabel ??= MachinePanel?.GetNodeOrNull<Label>("HeatBar/HeatValueLabel");
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

            HeatBar.MaxValue = _boundMachineCore.MaxHeat;
            HeatBar.Value = _boundMachineCore.Heat;
            HeatFillBar.MaxValue = _boundMachineCore.MaxHeat;
            HeatFillBar.Value = _boundMachineCore.Heat;

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

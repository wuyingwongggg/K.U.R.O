using Godot;
using Godot.Collections;
using Kuros.Systems;

namespace Kuros.UI
{
    /// <summary>
    /// 核心机制 HUD 容器。根据当前核心类型显示对应的子 UI 面板。
    /// 挂载于 BattleHUD 下。新增核心类型只需加一行 Export + 一行 _panelMap 注册。
    /// </summary>
    public partial class CoreHUD : Control
    {
        [Export] public Control? MachinePanel { get; set; }
        [Export] public Control? WaiterPanel { get; set; }
        [Export] public Control? ThrowPanel { get; set; }

        private readonly Dictionary<string, Control?> _panelMap = new();

        public override void _Ready()
        {
            _panelMap[BuildClassConstants.Machine] = MachinePanel;
            _panelMap[BuildClassConstants.Waiter] = WaiterPanel;
            _panelMap[BuildClassConstants.Throw] = ThrowPanel;
        }

        public void ShowFor(string buildClass)
        {
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

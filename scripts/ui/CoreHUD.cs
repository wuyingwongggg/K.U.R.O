using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 核心机制 HUD 容器。根据当前核心类型显示对应的子 UI 面板。
    /// 挂载于 BattleHUD 下。
    /// </summary>
    public partial class CoreHUD : Control
    {
        [Export] public Control? MachinePanel { get; set; }
        [Export] public Control? WaiterPanel { get; set; }
        [Export] public Control? ThrowPanel { get; set; }

        public void ShowFor(string buildClass)
        {
            HideAll();
            switch (buildClass)
            {
                case "Machine":
                    if (MachinePanel != null) MachinePanel.Visible = true;
                    break;
                case "Waiter":
                    if (WaiterPanel != null) WaiterPanel.Visible = true;
                    break;
                case "Throw":
                    if (ThrowPanel != null) ThrowPanel.Visible = true;
                    break;
            }
        }

        public void HideAll()
        {
            if (MachinePanel != null) MachinePanel.Visible = false;
            if (WaiterPanel != null) WaiterPanel.Visible = false;
            if (ThrowPanel != null) ThrowPanel.Visible = false;
        }
    }
}

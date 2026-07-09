using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 技能界面窗口 - 显示主技能和被动技能
    /// </summary>
    public partial class SkillWindow : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button CloseButton { get; private set; } = null!;
        [Export] public VBoxContainer PassiveSkillsContainer { get; private set; } = null!;
        [Export] public Label PassiveSkillsTitle { get; private set; } = null!;
        [Export] public Button DetailButton { get; private set; } = null!;

        private bool _isOpen = false;
        private SkillDetailWindow? _skillDetailWindow;
        private const string SkillDetailWindowPath = "res://scenes/ui/windows/SkillDetailWindow.tscn";
        private InventoryWindow? _cachedInventoryWindow;

        public override void _Ready()
        {
            base._Ready();
            ProcessMode = ProcessModeEnum.Always;

            CacheNodeReferences();
            UpdateSkillDisplay();
            Visible = true;
            _isOpen = true;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            base._UnhandledInput(@event);

            if (!_isOpen || !Visible)
            {
                return;
            }

            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Tab)
            {
                OnDetailButtonPressed();
                GetViewport().SetInputAsHandled();
            }
        }

        private void ConnectButtonSignal(Button? button, string methodName)
        {
            if (button == null) return;
            var callable = new Callable(this, methodName);
            if (!button.IsConnected(Button.SignalName.Pressed, callable))
            {
                button.Connect(Button.SignalName.Pressed, callable);
            }
        }

        private void CacheNodeReferences()
        {
            CloseButton ??= GetNodeOrNull<Button>("MainPanel/Header/CloseButton");
            PassiveSkillsContainer ??= GetNodeOrNull<VBoxContainer>("MainPanel/Body/SkillsVBox/PassiveSkillsSection/PassiveSkillsScroll/PassiveSkillsContainer");
            PassiveSkillsTitle ??= GetNodeOrNull<Label>("MainPanel/Body/SkillsVBox/PassiveSkillsSection/PassiveSkillsTitle");
            DetailButton ??= GetNodeOrNull<Button>("MainPanel/Body/DetailButton");

            if (DetailButton != null)
            {
                DetailButton.FocusMode = Control.FocusModeEnum.None;
            }

            ConnectButtonSignal(CloseButton, nameof(HideWindow));
            ConnectButtonSignal(DetailButton, nameof(OnDetailButtonPressed));
        }

        private void UpdateSkillDisplay()
        {
            if (PassiveSkillsContainer != null)
            {
                foreach (Node child in PassiveSkillsContainer.GetChildren())
                {
                    child.QueueFree();
                }
            }

            if (PassiveSkillsTitle != null)
            {
                PassiveSkillsTitle.Text = string.Empty;
                PassiveSkillsTitle.Visible = false;
            }

            if (PassiveSkillsContainer != null && PassiveSkillsContainer.GetParent() is Control passiveSection)
            {
                passiveSection.Visible = true;
            }
        }

        private bool IsInventoryWindowOpen()
        {
            if (_cachedInventoryWindow != null && IsInstanceValid(_cachedInventoryWindow))
            {
                return _cachedInventoryWindow.Visible;
            }

            _cachedInventoryWindow = GetTree().GetFirstNodeInGroup("inventory_window") as InventoryWindow;
            if (_cachedInventoryWindow != null)
            {
                return _cachedInventoryWindow.Visible;
            }

            var root = GetTree().Root;
            if (root != null)
            {
                _cachedInventoryWindow = FindInventoryWindowInNode(root);
                if (_cachedInventoryWindow != null)
                {
                    return _cachedInventoryWindow.Visible;
                }
            }

            return false;
        }

        private InventoryWindow? FindInventoryWindowInNode(Node node)
        {
            if (node is InventoryWindow inventoryWindow)
            {
                return inventoryWindow;
            }

            foreach (Node child in node.GetChildren())
            {
                var found = FindInventoryWindowInNode(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        public void ShowWindow()
        {
            if (_isOpen) return;

            UpdateSkillDisplay();
            Visible = true;
            _isOpen = true;
        }

        public void HideWindow()
        {
            if (!_isOpen) return;

            Visible = false;
            _isOpen = false;
        }

        public void ToggleWindow()
        {
            if (_isOpen)
                HideWindow();
            else
                ShowWindow();
        }

        public bool IsOpen => _isOpen;

        private void OnDetailButtonPressed()
        {
            if (_skillDetailWindow != null && _skillDetailWindow.IsOpen)
            {
                _skillDetailWindow.HideWindow();
                return;
            }

            if (_skillDetailWindow == null)
            {
                var scene = GD.Load<PackedScene>(SkillDetailWindowPath);
                if (scene == null)
                {
                    GD.PrintErr("无法加载技能详情窗口场景：", SkillDetailWindowPath);
                    return;
                }

                _skillDetailWindow = scene.Instantiate<SkillDetailWindow>();

                var parent = GetParent();
                if (parent != null)
                {
                    parent.AddChild(_skillDetailWindow);
                }
                else
                {
                    GD.PrintErr("SkillWindow.OnDetailButtonPressed: 无法找到父节点");
                    return;
                }
            }

            _skillDetailWindow.ShowWindow();
        }
    }
}

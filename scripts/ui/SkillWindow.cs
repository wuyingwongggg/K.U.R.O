using Godot;
using System.Collections.Generic;
using Kuros.Managers;
using Kuros.Systems;

namespace Kuros.UI
{
    public partial class SkillWindow : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button CloseButton { get; private set; } = null!;
        [Export] public VBoxContainer PassiveSkillsContainer { get; private set; } = null!;
        [Export] public Label PassiveSkillsTitle { get; private set; } = null!;
        [Export] public Button DetailButton { get; private set; } = null!;

        private bool _isOpen;
        private SkillDetailWindow? _skillDetailWindow;
        private const string SkillDetailWindowPath = "res://scenes/ui/windows/SkillDetailWindow.tscn";
        private InventoryWindow? _cachedInventoryWindow;

        public bool IsOpen => _isOpen;

        public override void _Ready()
        {
            base._Ready();
            ProcessMode = ProcessModeEnum.Always;
            CacheNodeReferences();
            BuildSelectionManager.Instance.PickedEffectsChanged += RefreshBuildIcons;
            RefreshBuildIcons();
            UIManager.RegisterInteractiveChildren(this);
            Visible = true;
            _isOpen = true;
        }

        public override void _ExitTree()
        {
            BuildSelectionManager.Instance.PickedEffectsChanged -= RefreshBuildIcons;
            base._ExitTree();
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            base._UnhandledInput(@event);
            if (!_isOpen || !Visible) return;
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
                button.Connect(Button.SignalName.Pressed, callable);
        }

        private void CacheNodeReferences()
        {
            CloseButton ??= GetNodeOrNull<Button>("MainPanel/Header/CloseButton");
            PassiveSkillsContainer ??= GetNodeOrNull<VBoxContainer>("MainPanel/Body/SkillsVBox/PassiveSkillsSection/PassiveSkillsScroll/PassiveSkillsContainer");
            PassiveSkillsTitle ??= GetNodeOrNull<Label>("MainPanel/Body/SkillsVBox/PassiveSkillsSection/PassiveSkillsTitle");
            DetailButton ??= GetNodeOrNull<Button>("MainPanel/DetailButton");
            if (DetailButton != null) DetailButton.FocusMode = Control.FocusModeEnum.None;
            ConnectButtonSignal(CloseButton, nameof(HideWindow));
            ConnectButtonSignal(DetailButton, nameof(OnDetailButtonPressed));
        }

        /// <summary>从 BuildSelectionManager 读取已选效果并刷新图标显示。</summary>
        private void RefreshBuildIcons()
        {
            var owned = new List<OwnedBuildViewData>();
            var bsm = BuildSelectionManager.Instance;
            if (bsm != null)
            {
                foreach (var kvp in bsm.PickedEffectIds)
                {
                    var def = bsm.FindEffectById(kvp.Key);
                    if (def?.Icon == null) continue;
                    owned.Add(new OwnedBuildViewData
                    {
                        Name = def.DisplayName,
                        Icon = def.Icon,
                        EffectId = def.EffectId,
                        StackCount = kvp.Value,
                    });
                }
            }

            UpdateSkillDisplay(owned);
        }

        private void UpdateSkillDisplay(List<OwnedBuildViewData> owned)
        {
            if (PassiveSkillsContainer != null)
            {
                foreach (Node child in PassiveSkillsContainer.GetChildren())
                    child.QueueFree();
            }

            if (PassiveSkillsTitle != null)
            {
                PassiveSkillsTitle.Visible = false;
            }

            if (owned.Count > 0)
            {
                PassiveSkillsContainer?.AddChild(CreateBuildIconsPanel(owned));
            }

            if (PassiveSkillsContainer != null && PassiveSkillsContainer.GetParent() is Control section)
                section.Visible = true;
        }

        private static Control CreateBuildIconsPanel(List<OwnedBuildViewData> owned)
        {
            var margin = new MarginContainer();
            margin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            margin.AddThemeConstantOverride("margin_left", 12);
            margin.AddThemeConstantOverride("margin_top", 12);
            margin.AddThemeConstantOverride("margin_right", 12);
            margin.AddThemeConstantOverride("margin_bottom", 12);

            var root = new VBoxContainer();
            root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            root.AddThemeConstantOverride("separation", 16);
            margin.AddChild(root);

            foreach (var item in owned)
            {
                var centerContainer = new CenterContainer();
                centerContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                centerContainer.CustomMinimumSize = new Vector2(0, 96);
                root.AddChild(centerContainer);

                var iconRect = new TextureRect();
                iconRect.CustomMinimumSize = new Vector2(96, 96);
                iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                iconRect.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
                iconRect.Texture = item.Icon;
                iconRect.TooltipText = $"{item.Name}{(item.StackCount > 1 ? $" ×{item.StackCount}" : "")}";
                centerContainer.AddChild(iconRect);
            }

            return margin;
        }

        public void ShowWindow()
        {
            if (_isOpen) return;
            RefreshBuildIcons();
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
            if (_isOpen) HideWindow();
            else ShowWindow();
        }

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
                if (scene == null) return;
                _skillDetailWindow = scene.Instantiate<SkillDetailWindow>();
                GetParent()?.AddChild(_skillDetailWindow);
            }

            _skillDetailWindow.ShowWindow();
        }

        private class OwnedBuildViewData
        {
            public string Name { get; set; } = string.Empty;
            public Texture2D? Icon { get; set; }
            public string EffectId { get; set; } = string.Empty;
            public int StackCount { get; set; }
        }
    }
}

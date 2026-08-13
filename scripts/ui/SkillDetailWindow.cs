using Godot;
using System.Collections.Generic;
using Kuros.Managers;
using Kuros.Systems;

namespace Kuros.UI
{
    /// <summary>
    /// 技能详情窗口 - 显示所有技能的详细信息
    /// </summary>
    public partial class SkillDetailWindow : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button CloseButton { get; private set; } = null!;
        [Export] public ScrollContainer SkillsScrollContainer { get; private set; } = null!;
        [Export] public VBoxContainer SkillsContainer { get; private set; } = null!;

        private bool _isOpen = false;

        public bool IsOpen => _isOpen;

        private readonly List<SkillDetailData> _allSkills = new();

        [Signal] public delegate void SkillDetailClosedEventHandler();

        public override void _Ready()
        {
            base._Ready();
            ProcessMode = ProcessModeEnum.Always;

            CacheNodeReferences();
            RefreshSkillData();
            UpdateSkillDisplay();
            HideWindow();
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
            CloseButton ??= GetNodeOrNull<Button>("MainPanel/CloseButton");
            SkillsScrollContainer ??= GetNodeOrNull<ScrollContainer>("MainPanel/Body/SkillsScroll");
            SkillsContainer ??= GetNodeOrNull<VBoxContainer>("MainPanel/Body/SkillsScroll/SkillsContainer");

            ConnectButtonSignal(CloseButton, nameof(HideWindow));
        }

        /// <summary>
        /// 从 BuildSelectionManager 读取全部已选构筑效果，组装详情卡片数据。
        /// 当前数值 = 堆叠数对应的层级值（TierValues[堆叠-1]）。
        /// </summary>
        private void RefreshSkillData()
        {
            _allSkills.Clear();

            var bsm = BuildSelectionManager.Instance;
            if (bsm == null || bsm.PickedEffectIds.Count == 0)
            {
                _allSkills.Add(new SkillDetailData
                {
                    Id = "no_skills",
                    Name = "技能详情",
                    Description = "当前尚未拥有技能。",
                    Icon = null,
                    IsActive = false,
                    Damage = "N/A",
                    Range = "N/A",
                    ManaCost = "N/A"
                });
                return;
            }

            foreach (var kvp in bsm.PickedEffectIds)
            {
                var def = bsm.FindEffectById(kvp.Key);
                if (def == null) continue;

                int stacks = kvp.Value;

                _allSkills.Add(new SkillDetailData
                {
                    Id = def.EffectId,
                    Name = def.DisplayName,
                    // 模板填充：{HeatCostValues:0} 等占位符替换为 PropertyOverrides 中的实际数值
                    Description = def.BuildDescriptionWithValues(stacks - 1),
                    Icon = def.Icon,
                    Rarity = def.Rarity,
                    IsActive = false,
                    Damage = "N/A",
                    Range = "N/A",
                    ManaCost = "N/A"
                });
            }
        }

        private void UpdateSkillDisplay()
        {
            if (SkillsContainer != null)
            {
                foreach (Node child in SkillsContainer.GetChildren())
                {
                    child.QueueFree();
                }
            }

            if (SkillsContainer != null)
            {
                foreach (var skill in _allSkills)
                {
                    var skillCard = CreateSkillDetailCard(skill);
                    SkillsContainer.AddChild(skillCard);
                }
            }
        }

        private Control CreateSkillDetailCard(SkillDetailData skill)
        {
            var card = new Panel();
            card.CustomMinimumSize = new Vector2(600, 200);
            // 水平扩展填充：让卡片横向填满 SkillsContainer 宽度
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            // 关键：card 是 Panel（非 Container），子节点必须显式锚定 FullRect 才能铺满卡片
            var margin = new MarginContainer();
            margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            margin.OffsetLeft = 16;
            margin.OffsetTop = 16;
            margin.OffsetRight = -16;
            margin.OffsetBottom = -16;
            card.AddChild(margin);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 12);
            vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            margin.AddChild(vbox);

            var headerHbox = new HBoxContainer();
            headerHbox.AddThemeConstantOverride("separation", 12);
            vbox.AddChild(headerHbox);

            var iconRect = new TextureRect();
            iconRect.CustomMinimumSize = new Vector2(80, 80);
            iconRect.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
            iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            if (skill.Icon != null)
            {
                iconRect.Texture = skill.Icon;
            }
            headerHbox.AddChild(iconRect);

            var nameVbox = new VBoxContainer();
            nameVbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            headerHbox.AddChild(nameVbox);

            var nameLabel = new Label();
            nameLabel.Text = skill.Name;
            nameLabel.AddThemeFontSizeOverride("font_size", 24);
            // 按稀有度着色：Common 蓝 / Rare 紫 / Epic 黄
            nameLabel.AddThemeColorOverride("font_color", GetRarityColor(skill.Rarity));
            nameVbox.AddChild(nameLabel);

            var descLabel = new RichTextLabel();
            descLabel.Text = skill.Description;
            descLabel.BbcodeEnabled = true;
            descLabel.FitContent = true;
            descLabel.CustomMinimumSize = new Vector2(0, 60);
            descLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            vbox.AddChild(descLabel);

            if (skill.IsActive)
            {
                var statsHbox = new HBoxContainer();
                statsHbox.AddThemeConstantOverride("separation", 24);
                vbox.AddChild(statsHbox);

                var cooldownLabel = new Label();
                cooldownLabel.Text = $"冷却时间: {skill.Cooldown:F1}秒";
                cooldownLabel.AddThemeFontSizeOverride("font_size", 16);
                statsHbox.AddChild(cooldownLabel);

                var damageLabel = new Label();
                damageLabel.Text = $"伤害: {skill.Damage}";
                damageLabel.AddThemeFontSizeOverride("font_size", 16);
                statsHbox.AddChild(damageLabel);

                var rangeLabel = new Label();
                rangeLabel.Text = $"范围: {skill.Range}";
                rangeLabel.AddThemeFontSizeOverride("font_size", 16);
                statsHbox.AddChild(rangeLabel);

                var manaLavel = new Label();
                manaLavel.Text = $"法力消耗: {skill.ManaCost}";
                manaLavel.AddThemeFontSizeOverride("font_size", 16);
                statsHbox.AddChild(manaLavel);
            }

            return card;
        }

        public void ShowWindow()
        {
            if (_isOpen) return;

            RefreshSkillData();
            UpdateSkillDisplay();

            Visible = true;
            ProcessMode = ProcessModeEnum.Always;
            SetProcessInput(true);
            SetProcessUnhandledInput(true);
            _isOpen = true;

            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PushPause();
            }

            var parent = GetParent();
            if (parent != null)
            {
                parent.MoveChild(this, parent.GetChildCount() - 1);
            }
        }

        public void HideWindow()
        {
            if (!_isOpen) return;

            Visible = false;
            SetProcessInput(false);
            SetProcessUnhandledInput(false);
            _isOpen = false;

            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PopPause();
            }

            EmitSignal(SignalName.SkillDetailClosed);
        }

        public override void _Input(InputEvent @event)
        {
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: true, useSetInputAsHandled: true))
            {
                return;
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: true, useSetInputAsHandled: false))
            {
                return;
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: false, useSetInputAsHandled: true))
            {
                return;
            }
        }

        private bool TryHandleCloseInput(InputEvent @event, bool useAcceptEvent, bool useSetInputAsHandled)
        {
            var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
            if (itemPopup != null && itemPopup.Visible)
            {
                return false;
            }

            bool isEscKey = @event.IsActionPressed("ui_cancel") ||
                (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape);
            bool isInventoryKey = @event.IsActionPressed("open_inventory");

            if (isEscKey || isInventoryKey)
            {
                HideWindow();
                if (useSetInputAsHandled) GetViewport().SetInputAsHandled();
                if (useAcceptEvent) AcceptEvent();
                return true;
            }
            return false;
        }

        private bool IsInventoryWindowOpen()
        {
            var root = GetTree().Root;
            if (root != null)
            {
                var inventoryWindows = FindAllInventoryWindowsInTree(root);

                foreach (var inventoryWindow in inventoryWindows)
                {
                    if (inventoryWindow.Visible)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private List<InventoryWindow> FindAllInventoryWindowsInTree(Node node)
        {
            var result = new List<InventoryWindow>();

            if (node is InventoryWindow inventoryWindow)
            {
                result.Add(inventoryWindow);
            }

            foreach (Node child in node.GetChildren())
            {
                result.AddRange(FindAllInventoryWindowsInTree(child));
            }

            return result;
        }

        private bool IsSkillWindowOpen()
        {
            var root = GetTree().Root;
            if (root != null)
            {
                var skillWindows = FindAllSkillWindowsInTree(root);

                foreach (var skillWindow in skillWindows)
                {
                    if (skillWindow.Visible && skillWindow.IsOpen)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private List<SkillWindow> FindAllSkillWindowsInTree(Node node)
        {
            var result = new List<SkillWindow>();

            if (node is SkillWindow skillWindow)
            {
                result.Add(skillWindow);
            }

            foreach (Node child in node.GetChildren())
            {
                result.AddRange(FindAllSkillWindowsInTree(child));
            }

            return result;
        }

        /// <summary>Build 稀有度 → 名称颜色：Common 蓝 / Rare 紫 / Epic 黄 / Core 金。</summary>
        private static Color GetRarityColor(BuildRarity rarity) => rarity switch
        {
            BuildRarity.Common => new Color(0.35f, 0.6f, 1.0f),   // 蓝
            BuildRarity.Rare => new Color(0.7f, 0.4f, 1.0f),      // 紫
            BuildRarity.Epic => new Color(1.0f, 0.85f, 0.3f),     // 黄
            _ => Colors.White,
        };

        internal class SkillDetailData
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public Texture2D? Icon { get; set; }
            public float Cooldown { get; set; } = 0.0f;
            public BuildRarity Rarity { get; set; } = BuildRarity.Common;
            public bool IsActive { get; set; } = true;
            public string Damage { get; set; } = "0";
            public string Range { get; set; } = "0";
            public string ManaCost { get; set; } = "0";
        }
    }
}

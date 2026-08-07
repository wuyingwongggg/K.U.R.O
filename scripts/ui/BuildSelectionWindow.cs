using System;
using System.Collections.Generic;
using Godot;
using Kuros.Managers;
using Kuros.Systems;

namespace Kuros.UI
{
    public partial class BuildSelectionWindow : Control
    {
        [Export] public PackedScene? CardTemplate { get; set; }
        [Export] public Control? CardContainer { get; set; }

        private List<BuildCard> _cards = new();
        private List<BuildEffectDefinition> _options = new();
        private Action<BuildEffectDefinition>? _onConfirmed;
        private IReadOnlyDictionary<string, int>? _stacksByEffectId;
        private int _selectedIndex;
        private bool _isOpen;

        public override void _Ready()
        {
            ResolveExports();
            Visible = false;
            ProcessMode = ProcessModeEnum.Always;
        }

        private void ResolveExports()
        {
            CardContainer ??= GetNodeOrNull<Control>("Cards");
        }

        public void ShowWindow(List<BuildEffectDefinition> options, Action<BuildEffectDefinition> onConfirmed,
            IReadOnlyDictionary<string, int>? stacksByEffectId = null)
        {
            if (_isOpen) return;

            _options = options;
            _onConfirmed = onConfirmed;
            _stacksByEffectId = stacksByEffectId;
            _selectedIndex = -1;

            Visible = true;
            ProcessMode = ProcessModeEnum.Always;
            SetProcessInput(true);
            _isOpen = true;

            PauseManager.Instance.PushPause();

            CallDeferred(nameof(DeferredPopulate));
        }

        private void DeferredPopulate()
        {
            PopulateOptions();
            UpdateHighlights();
        }

        public void CloseWindow()
        {
            if (!_isOpen) return;

            Visible = false;
            SetProcessInput(false);
            _isOpen = false;

            GetTree().CreateTimer(0.15f).Timeout += () =>
            {
                if (PauseManager.Instance.IsPaused)
                    PauseManager.Instance.PopPause();
            };

            var parent = GetParent();
            if (parent is CanvasLayer canvasLayer)
                canvasLayer.QueueFree();
            QueueFree();
        }

        public override void _Input(InputEvent @event)
        {
            if (!_isOpen) return;
            if (PauseManager.Instance.PauseCount > 1) return;

            if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                int numKey = (int)(keyEvent.Keycode - Key.Key1);
                if (numKey >= 0 && numKey < _options.Count)
                {
                    ConfirmSelection(numKey);
                    return;
                }
            }

            if (@event.IsActionPressed("ui_left") || @event.IsActionPressed("move_left"))
            {
                _selectedIndex = (_selectedIndex - 1 + _options.Count) % _options.Count;
                UpdateHighlights();
                return;
            }
            if (@event.IsActionPressed("ui_right") || @event.IsActionPressed("move_right"))
            {
                _selectedIndex = (_selectedIndex + 1) % _options.Count;
                UpdateHighlights();
                return;
            }

            if (@event.IsActionPressed("ui_accept"))
            {
                ConfirmSelection(_selectedIndex);
                return;
            }
        }

        private void PopulateOptions()
        {
            foreach (var card in _cards)
                card.QueueFree();
            _cards.Clear();

            if (CardTemplate == null || CardContainer == null) return;

            int count = _options.Count;
            float gap = 16f;
            float totalWidth = CardContainer.Size.X;
            float cardWidth = (totalWidth - gap * (count - 1)) / count;
            float cardHeight = Mathf.Min(cardWidth * (340f / 260f), CardContainer.Size.Y);
            float cardY = (CardContainer.Size.Y - cardHeight) / 2f;

            for (int i = 0; i < count; i++)
            {
                var effect = _options[i];
                var card = CardTemplate.Instantiate<BuildCard>();
                card.CardIndex = i;
                card.NameLabel!.Text = effect.DisplayName;
                int currentStacks = 0;
                _stacksByEffectId?.TryGetValue(effect.EffectId, out currentStacks);
                card.DescLabel!.Text = BuildTierDescription(effect, currentStacks);
                card.BuildClassLabel!.Text = !string.IsNullOrWhiteSpace(effect.BuildClass)
                    ? $"[{GetBuildClassName(effect.BuildClass)}]"
                    : "";
                card.KeyLabel!.Text = $"[{i + 1}]";
                if (card.Icon != null)
                {
                    card.Icon.Texture = effect.Icon;
                    card.Icon.Visible = effect.Icon != null;
                }
                card.Confirmed += OnCardConfirmed;

                float x = i * (cardWidth + gap);
                CardContainer.AddChild(card);
                card.Position = new Vector2(x, cardY);
                card.Size = new Vector2(cardWidth, cardHeight);
                card.SyncViewportSize();
                card.ApplyCardScale();
                card.ApplyRarityVisuals(effect.Rarity);
                _cards.Add(card);
            }
        }

        private static readonly System.Text.RegularExpressions.Regex TierTokenRegex = new(
            @"{([A-Za-z_][A-Za-z0-9_]*)?:?(\d+)}");

        /// <summary>
        /// 描述模板填充：把 {数组名:下标} 占位符替换为对应数组的值并做 BBCode 高亮。
        /// 简写 {i} 等价于 {TierValues:i}；多个数组（如 GainValues/CapValues）共享同一当前层索引。
        /// 当前可获得层（stacks 0 起 → 下标 stacks）金色高亮，其余层暗色。
        /// 找不到数组 / 下标越界 / 描述无占位符时原样返回。
        /// </summary>
        private static string BuildTierDescription(BuildEffectDefinition effect, int stacks)
        {
            string template = effect.Description;
            if (!template.Contains('{'))
                return template;

            return TierTokenRegex.Replace(template, match =>
            {
                string arrayName = match.Groups[1].Success && match.Groups[1].Value.Length > 0
                    ? match.Groups[1].Value
                    : "TierValues";
                int index = int.Parse(match.Groups[2].Value);

                var values = effect.GetOverrideFloatArray(arrayName);
                if (values == null || index < 0 || index >= values.Length)
                    return match.Value; // 无数据 → 保留原文

                int tierIndex = Mathf.Clamp(stacks, 0, values.Length - 1);

                // 修改器百分比为负（减容/缓速类）时，描述按数值大小显示（降低 10%，而非 -10%）
                string valueText = Mathf.Abs(values[index]).ToString();
                return index == tierIndex
                    ? $"[color=#FFD700]{valueText}[/color]"
                    : $"[color=#8A8A8A]{valueText}[/color]";
            });
        }

        private void OnCardConfirmed(int index)
        {
            ConfirmSelection(index);
        }

        private void UpdateHighlights()
        {
            for (int i = 0; i < _cards.Count; i++)
                _cards[i].IsSelected = i == _selectedIndex;
        }

        private static readonly Dictionary<string, string> BuildClassDisplayNames = new()
        {
            { BuildClassConstants.Machine, "机械协议" },
            { BuildClassConstants.Waiter, "宴会协议" },
            { BuildClassConstants.Throw, "投掷协议" },
            { BuildClassConstants.Generic, "通用" },
        };

        private static string GetBuildClassName(string buildClass)
        {
            if (BuildClassDisplayNames.TryGetValue(buildClass, out var name))
                return name;
            return buildClass;
        }

        private void ConfirmSelection(int index)
        {
            if (index < 0 || index >= _options.Count) return;

            var chosen = _options[index];
            var callback = _onConfirmed;
            CloseWindow();
            callback?.Invoke(chosen);
        }
    }
}

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

        public void ShowWindow(List<BuildEffectDefinition> options, Action<BuildEffectDefinition> onConfirmed)
        {
            if (_isOpen) return;

            _options = options;
            _onConfirmed = onConfirmed;
            _selectedIndex = 0;

            PopulateOptions();
            UpdateHighlights();
            Visible = true;
            ProcessMode = ProcessModeEnum.Always;
            SetProcessInput(true);
            _isOpen = true;

            PauseManager.Instance.PushPause();
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

            if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                switch (keyEvent.Keycode)
                {
                    case Key.Key1 when _options.Count > 0:
                        ConfirmSelection(0);
                        return;
                    case Key.Key2 when _options.Count > 1:
                        ConfirmSelection(1);
                        return;
                    case Key.Key3 when _options.Count > 2:
                        ConfirmSelection(2);
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

            if (@event.IsActionPressed("ui_accept") || @event.IsActionPressed("attack"))
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
            float cardHeight = CardContainer.Size.Y;

            for (int i = 0; i < count; i++)
            {
                var effect = _options[i];
                var card = CardTemplate.Instantiate<BuildCard>();
                card.CardIndex = i;
                card.NameLabel!.Text = effect.DisplayName;
                card.DescLabel!.Text = effect.Description;
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
                card.Position = new Vector2(x, 0);
                card.Size = new Vector2(cardWidth, cardHeight);
                CardContainer.AddChild(card);
                _cards.Add(card);
            }
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

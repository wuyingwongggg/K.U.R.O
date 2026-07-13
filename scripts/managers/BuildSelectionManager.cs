using System;
using System.Collections.Generic;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core.Effects;
using Kuros.Systems;
using Kuros.UI;

namespace Kuros.Managers
{
    /// <summary>
    /// 构筑选择管理器：监听玩家分数，达到阈值时弹出三选一构筑效果窗口。
    /// </summary>
    public partial class BuildSelectionManager : Node
    {
        public static BuildSelectionManager Instance { get; private set; } = null!;

        /// <summary>构筑效果变动时触发（选择新效果、恢复效果等）。</summary>
        public event Action? PickedEffectsChanged;

        private const string GenericBuildClass = BuildClassConstants.Generic;

        [ExportGroup("Thresholds")]
        [Export] public ScoreThresholdCurve? ThresholdCurve { get; set; }

        [ExportGroup("Core Pool")]
        [Export] public Godot.Collections.Array<BuildCoreDefinition> CorePool { get; set; } = new();

        [ExportGroup("Effect Pool")]
        [Export] public Godot.Collections.Array<BuildEffectDefinition> EffectPool { get; set; } = new();

        [ExportGroup("Core Trigger")]
        [Export(PropertyHint.Range, "0,999999,1")] public int CoreTriggerScore = 0;

        [ExportGroup("Debug")]
        [Export] public bool DebugTrigger { get; set; }

        private SamplePlayer? _boundPlayer;
        private string? _playerCoreClass;
        private string? _selectedCoreId;
        private bool _coreSelected;
        private int _lastKnownScore;
        private int _triggerCount;
        private bool _isSelectionActive;
        private int _pendingScore;
        private readonly Dictionary<string, int> _pickedEffectIds = new();
        private readonly System.Random _rng = new();

        public bool IsSelectionActive => _isSelectionActive;

        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                QueueFree();
                return;
            }
            Instance = this;
            TryBindPlayer();
        }

        public override void _ExitTree()
        {
            UnbindPlayer();
            base._ExitTree();
        }

        public override void _Process(double delta)
        {
            if (_boundPlayer == null || !IsInstanceValid(_boundPlayer))
                TryBindPlayer();

            if (DebugTrigger && _boundPlayer != null && !_isSelectionActive)
            {
                DebugTrigger = false;
                TriggerSelection();
            }
        }

        private void TryBindPlayer()
        {
            if (_isSelectionActive) return;

            var tree = GetTree();
            if (tree == null) return;

            var player = tree.GetFirstNodeInGroup("player") as SamplePlayer;
            if (player == null || !IsInstanceValid(player)) return;
            if (player == _boundPlayer) return;

            if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                _boundPlayer.StatsUpdated -= OnPlayerStatsUpdated;

            _boundPlayer = player;
            _boundPlayer.StatsUpdated += OnPlayerStatsUpdated;

            // 恢复跨场景分数和核心状态
            if (_pendingScore > 0 && player.Score < _pendingScore)
            {
                player.AddScore(_pendingScore - player.Score);
            }

            _lastKnownScore = _pendingScore > 0 ? _pendingScore : player.Score;
            _triggerCount = ThresholdCurve?.GetTriggerCount(_lastKnownScore) ?? 0;
        }

        private void UnbindPlayer()
        {
            if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                _boundPlayer.StatsUpdated -= OnPlayerStatsUpdated;
            _boundPlayer = null;
        }

        private void OnPlayerStatsUpdated(int health, int maxHealth, int score)
        {
            if (score > _lastKnownScore)
                CheckAndTriggerSelection(score);
            _lastKnownScore = score;
            _pendingScore = score;
        }

        private void CheckAndTriggerSelection(int newScore)
        {
            if (_isSelectionActive) return;
            if (ThresholdCurve == null) return;

            // 必须选完核心才能进入效果三选一
            if (!_coreSelected) return;
            if (EffectPool.Count == 0) return;

            int nextThreshold = ThresholdCurve.GetCumulativeScore(_triggerCount + 1);
            if (newScore >= nextThreshold)
            {
                _triggerCount++;
                TriggerSelection();
            }
        }

        private PackedScene? _coreWindowScene;

        public void TriggerCoreSelection()
        {
            if (_coreSelected) return;
            if (_boundPlayer == null) TryBindPlayer();
            if (_boundPlayer == null || !IsInstanceValid(_boundPlayer)) return;
            if (CorePool.Count == 0) return;
            _isSelectionActive = true;

            // 将 CoreDefinition 包装为 BuildEffectDefinition 以复用现有窗口
            var options = new List<BuildEffectDefinition>();
            foreach (var core in CorePool)
            {
                if (core == null) continue;
                options.Add(new BuildEffectDefinition
                {
                    EffectId = core.CoreId,
                    DisplayName = core.DisplayName,
                    Description = core.Description,
                    BuildClass = core.BuildClass,
                    Icon = core.Icon,
                });
            }

            _coreWindowScene ??= GD.Load<PackedScene>("res://scenes/ui/windows/BuildSelectionWindow.tscn");
            if (_coreWindowScene == null)
            {
                _isSelectionActive = false;
                return;
            }
            var window = _coreWindowScene.Instantiate<BuildSelectionWindow>();
            if (window.TitleLabel != null)
                window.TitleLabel.Text = "选择构筑核心";

            var canvasLayer = new CanvasLayer { Layer = 3 };
            GetTree().Root.AddChild(canvasLayer);
            canvasLayer.AddChild(window);

            window.ShowWindow(options, chosenEffect =>
            {
                _selectedCoreId = chosenEffect.EffectId;
                SetPlayerCoreClass(chosenEffect.BuildClass);
                _coreSelected = true;
                _isSelectionActive = false;

                // 实例化核心机制的 ActorEffect
                var chosenCore = FindCoreById(chosenEffect.EffectId);
                if (chosenCore?.CoreEffectScene != null && _boundPlayer?.EffectController != null)
                {
                    var coreEffect = chosenCore.CoreEffectScene.Instantiate<ActorEffect>();
                    coreEffect.EffectId = chosenCore.CoreId;
                    coreEffect.DisplayName = chosenCore.DisplayName;
                    coreEffect.Duration = 0f;
                    _boundPlayer.ApplyEffect(coreEffect);
                }

                // 通知 CoreHUD 切换显示
                var coreHUD = GetTree().Root.FindChild("CoreHUD", recursive: true, owned: false) as UI.CoreHUD;
                coreHUD?.ShowFor(chosenEffect.BuildClass);

                if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                    CheckAndTriggerSelection(_boundPlayer.Score);
            });
        }

        private BuildCoreDefinition? FindCoreById(string coreId)
        {
            foreach (var core in CorePool)
            {
                if (core?.CoreId == coreId)
                    return core;
            }
            return null;
        }

        public void SetPlayerCoreClass(string buildClass)
        {
            _playerCoreClass = buildClass;
        }

        /// <summary>重置所有构筑状态（新游戏开始时调用）。</summary>
        public void ResetBuildState()
        {
            _coreSelected = false;
            _selectedCoreId = null;
            _playerCoreClass = null;
            _pickedEffectIds.Clear();
            _pendingScore = 0;
            _lastKnownScore = 0;
            _triggerCount = 0;
        }

        private PackedScene? _windowScene;

        private void TriggerSelection()
        {
            if (_boundPlayer == null || !IsInstanceValid(_boundPlayer)) return;

            var options = PickRandomEffects(3);
            if (options.Count == 0) return;

            _isSelectionActive = true;

            _windowScene ??= GD.Load<PackedScene>("res://scenes/ui/windows/BuildSelectionWindow.tscn");
            var window = _windowScene.Instantiate<BuildSelectionWindow>();

            var canvasLayer = new CanvasLayer { Layer = 3 };
            GetTree().Root.AddChild(canvasLayer);
            canvasLayer.AddChild(window);

            window.ShowWindow(options, chosenEffect =>
            {
                if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                {
                    ApplyEffectBonuses(chosenEffect);
                }
                _isSelectionActive = false;

                if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                    CheckAndTriggerSelection(_boundPlayer.Score);
            });
        }

        private void ApplyEffectBonuses(BuildEffectDefinition effect)
        {
            if (_boundPlayer?.EffectController == null) return;

            string effectId = effect.EffectId;
            if (string.IsNullOrWhiteSpace(effectId)) return;

            // 追踪已选效果
            _pickedEffectIds.TryGetValue(effectId, out int currentStacks);
            _pickedEffectIds[effectId] = currentStacks + 1;
            PickedEffectsChanged?.Invoke();

            // 复杂效果：实例化 EffectScene 中的 ActorEffect
            if (effect.EffectScene != null)
            {
                var existing = _boundPlayer.EffectController.GetEffect(effectId);
                if (existing != null)
                {
                    existing.Refresh(1);
                    return;
                }

                var instance = effect.EffectScene.Instantiate<ActorEffect>();
                instance.EffectId = effectId;
                instance.DisplayName = effect.DisplayName;
                instance.Duration = 0f;
                _boundPlayer.ApplyEffect(instance);
                return;
            }

            // 纯数值效果：BuildStatBonusEffect
            if (effect.StatBonuses.Count > 0)
            {
                var existing = _boundPlayer.EffectController.GetEffect(effectId) as BuildStatBonusEffect;
                if (existing != null)
                {
                    existing.Refresh(1);
                    return;
                }

                var bonus = new BuildStatBonusEffect
                {
                    EffectId = effectId,
                    DisplayName = effect.DisplayName,
                    StatBonuses = new Godot.Collections.Dictionary<string, float>(effect.StatBonuses),
                    Duration = 0f,
                };
                _boundPlayer.ApplyEffect(bonus);
            }
        }

        private List<BuildEffectDefinition> PickRandomEffects(int count)
        {
            var result = new List<BuildEffectDefinition>();

            // 按核心类型 + Generic 过滤
            var filtered = new List<BuildEffectDefinition>();
            foreach (var effect in EffectPool)
            {
                if (effect == null) continue;
                if (string.IsNullOrWhiteSpace(_playerCoreClass)) continue;
                if (effect.BuildClass == _playerCoreClass || effect.BuildClass == GenericBuildClass)
                    filtered.Add(effect);
            }

            if (filtered.Count == 0) return result;
            count = Mathf.Min(count, filtered.Count);

            for (int i = 0; i < count; i++)
            {
                int j = _rng.Next(i, filtered.Count);
                (filtered[i], filtered[j]) = (filtered[j], filtered[i]);
                result.Add(filtered[i]);
            }

            return result;
        }

        /// <summary>跨场景恢复核心效果和所有已选构筑效果。</summary>
        public void RestoreBuildState(SamplePlayer player)
        {
            if (player?.EffectController == null) return;

            // 恢复核心效果
            if (!string.IsNullOrWhiteSpace(_selectedCoreId))
            {
                var core = FindCoreById(_selectedCoreId);
                if (core?.CoreEffectScene != null)
                {
                    var coreEffect = core.CoreEffectScene.Instantiate<ActorEffect>();
                    coreEffect.EffectId = core.CoreId;
                    coreEffect.DisplayName = core.DisplayName;
                    coreEffect.Duration = 0f;
                    player.ApplyEffect(coreEffect);
                }

                // 恢复 CoreHUD
                var coreHUD = GetTree().Root.FindChild("CoreHUD", recursive: true, owned: false) as UI.CoreHUD;
                coreHUD?.ShowFor(_playerCoreClass ?? "");
            }

            // 恢复已选的构筑效果（重新实例化，按记录的栈层数）
            foreach (var kvp in _pickedEffectIds)
            {
                string effectId = kvp.Key;
                int stacks = kvp.Value;

                var definition = FindEffectById(effectId);
                if (definition == null) continue;

                for (int s = 0; s < stacks; s++)
                {
                    if (definition.EffectScene != null && s == 0)
                    {
                        var instance = definition.EffectScene.Instantiate<ActorEffect>();
                        instance.EffectId = effectId;
                        instance.DisplayName = definition.DisplayName;
                        instance.Duration = 0f;
                        player.ApplyEffect(instance);
                        // 额外栈层：Refresh
                        for (int r = 1; r < stacks; r++)
                        {
                            var existing = player.EffectController.GetEffect(effectId);
                            existing?.Refresh(1);
                        }
                        break;
                    }
                    else if (definition.StatBonuses.Count > 0)
                    {
                        if (s == 0)
                        {
                            var bonus = new BuildStatBonusEffect
                            {
                                EffectId = effectId,
                                DisplayName = definition.DisplayName,
                                StatBonuses = new Godot.Collections.Dictionary<string, float>(definition.StatBonuses),
                                Duration = 0f,
                            };
                            player.ApplyEffect(bonus);
                        }
                        else
                        {
                            var existing = player.EffectController.GetEffect(effectId);
                            existing?.Refresh(1);
                        }
                    }
                }
            }

            PickedEffectsChanged?.Invoke();
        }

        public BuildEffectDefinition? FindEffectById(string effectId)
        {
            foreach (var effect in EffectPool)
            {
                if (effect?.EffectId == effectId)
                    return effect;
            }
            return null;
        }

        public IReadOnlyDictionary<string, int> PickedEffectIds => _pickedEffectIds;
    }
}

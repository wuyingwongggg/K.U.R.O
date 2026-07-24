using System;
using System.Collections.Generic;
using Godot;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Actors.Heroes;
using Kuros.Builds.BuildCore;
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

        [ExportGroup("Thresholds")]
        [Export] public ScoreThresholdCurve? ThresholdCurve { get; set; }

        [ExportGroup("Core Pool")]
        [Export] public Godot.Collections.Array<BuildCoreDefinition> CorePool { get; set; } = new();

        [ExportGroup("Effect Pool")]
        [Export] public Godot.Collections.Array<BuildEffectDefinition> EffectPool { get; set; } = new();

        [ExportGroup("Rarity")]
        /// <summary>稀有度权重倍率。Key: "Common"/"Rare"/"Epic"，默认 Common=3, Rare=1, Epic=0.3。</summary>
        [Export] public Godot.Collections.Dictionary<string, float> RarityMultiplier { get; set; } = new()
        {
            { "Common", 3.0f },
            { "Rare", 1.0f },
            { "Epic", 0.3f },
        };

        /// <summary>每次选择的卡牌数量。</summary>
        [Export(PropertyHint.Range, "2,10,1")]
        public int CardsPerSelection { get; set; } = 3;

        /// <summary>未选核心时使用的默认构筑类别。空 = 不选核心不触发三选一。</summary>
        [Export] public string DefaultBuildClass { get; set; } = "";

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

            // 必须选完核心（或配置了 DefaultBuildClass）才能进入效果三选一
            if (!_coreSelected && string.IsNullOrWhiteSpace(DefaultBuildClass)) return;
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
                ActorEffect? createdCoreEffect = null;
                if (chosenCore?.CoreEffectScene != null && _boundPlayer?.EffectController != null)
                {
                    var coreEffect = chosenCore.CoreEffectScene.Instantiate<ActorEffect>();
                    coreEffect.EffectId = chosenCore.CoreId;
                    coreEffect.DisplayName = chosenCore.DisplayName;
                    coreEffect.Duration = 0f;
                    _boundPlayer.ApplyEffect(coreEffect);
                    createdCoreEffect = coreEffect;
                }

                // 通知 CoreHUD 切换显示，并注入 MachineCoreEffect 引用
                var coreHUD = GetTree().Root.FindChild("CoreHUD", recursive: true, owned: false) as UI.CoreHUD;
                if (coreHUD != null)
                {
                    coreHUD.ShowFor(chosenEffect.BuildClass);
                    if (createdCoreEffect is MachineCoreEffect machineCore)
                        coreHUD.BindMachineCore(machineCore);
                }

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

            GD.Print($"[BuildSelection] CardsPerSelection = {CardsPerSelection}");
            var options = PickRandomEffects(CardsPerSelection);
            GD.Print($"[BuildSelection] PickRandomEffects returned {options.Count} cards");
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

            // 复杂效果：遍历 EffectEntries，每个 entry 自带 PropertyOverrides
            if (effect.EffectEntries.Count > 0)
            {
                bool isNew = _boundPlayer.EffectController.GetEffect(effectId) == null;
                foreach (var entry in effect.EffectEntries)
                {
                    if (entry?.Scene == null) continue;
                    if (isNew)
                    {
                        var instance = entry.InstantiateEffect();
                        if (instance != null)
                        {
                            instance.EffectId = effectId;
                            instance.DisplayName = effect.DisplayName;
                            instance.Duration = 0f;
                            _boundPlayer.ApplyEffect(instance);
                        }
                    }
                    else
                    {
                        var existing = _boundPlayer.EffectController.GetEffect(effectId);
                        existing?.Refresh(1);
                    }
                }
                return;
            }

            // 所有效果统一由 EffectEntries 驱动（含 AttackEffectEntry.PropertyOverrides）
        }

        private List<BuildEffectDefinition> PickRandomEffects(int count)
        {
            var result = new List<BuildEffectDefinition>();

            // 找到当前核心的 AllowedEffectClasses
            var core = FindActiveCore();
            // 确定过滤条件
            var allowedClasses = core?.AllowedEffectClasses;
            HashSet<string> allowedSet;
            if (allowedClasses != null && allowedClasses.Count > 0)
                allowedSet = new HashSet<string>(allowedClasses);
            else if (!string.IsNullOrWhiteSpace(_playerCoreClass))
                allowedSet = new HashSet<string> { _playerCoreClass };
            else if (!string.IsNullOrWhiteSpace(DefaultBuildClass))
                allowedSet = new HashSet<string> { DefaultBuildClass };
            else
                return result;

            // 按 BuildClass 过滤 + MaxStacks 排除 + Rarity 加权（统一路径）
            var candidates = new List<(BuildEffectDefinition effect, float cumulativeWeight)>();
            float totalWeight = 0f;

            foreach (var effect in EffectPool)
            {
                if (effect == null) continue;
                if (string.IsNullOrWhiteSpace(effect.BuildClass)) continue;
                if (!allowedSet.Contains(effect.BuildClass)) continue;

                _pickedEffectIds.TryGetValue(effect.EffectId, out int stacks);
                if (effect.MaxStacks > 0 && stacks >= effect.MaxStacks)
                    continue;

                float mult = 1.0f;
                RarityMultiplier?.TryGetValue(effect.Rarity.ToString(), out mult);
                float w = effect.Weight * mult;
                if (w <= 0f) continue;

                totalWeight += w;
                candidates.Add((effect, totalWeight));
            }

            GD.Print($"[BuildSelection] PickRandomEffects: requested={count}, candidates={candidates.Count}, allowedSet=[{string.Join(",", allowedSet)}]");
            if (candidates.Count == 0) return result;
            int pickCount = Mathf.Min(count, candidates.Count);
            GD.Print($"[BuildSelection] PickRandomEffects: pickCount={pickCount}");

            for (int p = 0; p < pickCount; p++)
            {
                float roll = (float)_rng.NextDouble() * totalWeight;
                int idx = 0;
                while (idx < candidates.Count && candidates[idx].cumulativeWeight < roll)
                    idx++;
                if (idx >= candidates.Count) idx = candidates.Count - 1;

                var picked = candidates[idx].effect;
                result.Add(picked);

                // 移除已选，重新计算权重
                float removedWeight = picked.Weight;
                float removedMult = 1f;
                RarityMultiplier?.TryGetValue(picked.Rarity.ToString(), out removedMult);
                totalWeight -= removedWeight * removedMult;
                candidates.RemoveAt(idx);
            }

            return result;
        }

        private BuildCoreDefinition? FindActiveCore()
        {
            if (string.IsNullOrWhiteSpace(_selectedCoreId)) return null;
            foreach (var core in CorePool)
            {
                if (core?.CoreId == _selectedCoreId)
                    return core;
            }
            return null;
        }

        /// <summary>跨场景恢复核心效果和所有已选构筑效果。</summary>
        public void RestoreBuildState(SamplePlayer player)
        {
            if (player?.EffectController == null) return;

            // 恢复核心效果
            if (!string.IsNullOrWhiteSpace(_selectedCoreId))
            {
                ActorEffect? restoredCoreEffect = null;
                var core = FindCoreById(_selectedCoreId);
                if (core?.CoreEffectScene != null)
                {
                    var coreEffect = core.CoreEffectScene.Instantiate<ActorEffect>();
                    coreEffect.EffectId = core.CoreId;
                    coreEffect.DisplayName = core.DisplayName;
                    coreEffect.Duration = 0f;
                    player.ApplyEffect(coreEffect);
                    restoredCoreEffect = coreEffect;
                }

                // 恢复 CoreHUD，注入 MachineCoreEffect 引用
                var coreHUD = GetTree().Root.FindChild("CoreHUD", recursive: true, owned: false) as UI.CoreHUD;
                if (coreHUD != null)
                {
                    coreHUD.ShowFor(_playerCoreClass ?? "");
                    if (restoredCoreEffect is MachineCoreEffect machineCore)
                        coreHUD.BindMachineCore(machineCore);
                }
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
                    if (definition.EffectEntries.Count > 0 && s == 0)
                    {
                        foreach (var entry in definition.EffectEntries)
                        {
                            if (entry?.Scene == null) continue;
                            var instance = entry.InstantiateEffect();
                            if (instance != null)
                            {
                                instance.EffectId = effectId;
                                instance.DisplayName = definition.DisplayName;
                                instance.Duration = 0f;
                                player.ApplyEffect(instance);
                            }
                        }
                        // 额外栈层：Refresh
                        for (int r = 1; r < stacks; r++)
                        {
                            var existing = player.EffectController.GetEffect(effectId);
                            existing?.Refresh(1);
                        }
                        break;
                    }
                    // 所有效果统一由 EffectEntries 驱动
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

using System;
using System.Collections.Generic;
using Godot;
using Kuros.Items;
using Kuros.Items.Tags;
using Kuros.Systems.AI;

namespace Kuros.Companions
{
    /// <summary>
    /// Rule-based support brain for P2. Reads GameState and emits lightweight non-blocking hints.
    /// </summary>
    public partial class P2SupportBrain : Node
    {
        [ExportCategory("References")]
        [Export] public NodePath GameStateProviderPath { get; set; } = new("../MainCharacter/GameStateProvider");
        [Export] public NodePath SupportExecutorPath { get; set; } = new("../AI_Executor");
        [Export] public NodePath SupportDecisionBridgePath { get; set; } = new("../AI_DecisionBridge");
        [Export] public NodePath AiDecisionBridgePath { get; set; } = new("../MainCharacter/AiDecisionBridge");
        [Export] public NodePath WeaponCarrierPath { get; set; } = new("../AI_WeaponCarrier");

        [ExportCategory("AI Bridge")]
        [Export] public bool EnableAiDecisionBridge { get; set; } = false;
        [Export] public bool AiDecisionHasPriority { get; set; } = true;
        /// <summary>武器拾取检测范围（px）：范围内存在可拾取武器时自动前往拾取。</summary>
        [Export(PropertyHint.Range, "100,3000,50")] public float CarryRangeMax { get; set; } = 2000f;
        /// <summary>武器忽略范围（px）：距玩家小于此值的武器不拾取（玩家自己会捡，防止反复拾取/放置循环）。</summary>
        [Export(PropertyHint.Range, "100,1000,50")] public float CarryRangeMin { get; set; } = 400f;
        [Export] public bool UseLiveAiDecisionSource { get; set; } = true;
        [Export] public bool RequestAiDecisionFromBridge { get; set; } = true;
        /// <summary>AI 决策请求间隔（秒）：每 N 秒向 AiDecisionBridge 请求一次决策，避免频繁请求。</summary>
        [Export(PropertyHint.Range, "0.2,10,0.1")] public float AiRequestIntervalSeconds { get; set; } = 1.0f;
        [Export] public bool ConsumeOnlyFreshAiDecision { get; set; } = true;
        [Export(PropertyHint.MultilineText)] public string DebugAiSuggestionJson { get; set; } = string.Empty;

        [ExportCategory("AI Personality Chatter")]
        [Export] public bool EnableAiPersonalityChatter { get; set; } = true;
        [Export] public bool PersonalityChatterOnlyWhenSafe { get; set; } = false;
        [Export(PropertyHint.Range, "3,60,0.5")] public float PersonalityChatterMinIntervalSeconds { get; set; } = 14f;
        [Export(PropertyHint.Range, "0,1,0.01")] public float PersonalityChatterChance { get; set; } = 0.28f;
        [Export(PropertyHint.Range, "8,80,1")] public int PersonalityChatterMaxChars { get; set; } = 26;

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float EvaluateIntervalSeconds { get; set; } = 0.5f;
        [Export(PropertyHint.Range, "0.1,20,0.1")] public float GlobalHintCooldownSeconds { get; set; } = 2.2f;

        [ExportCategory("Rules")]
        [Export(PropertyHint.Range, "0.05,1,0.01")] public float LowHpThresholdRatio { get; set; } = 0.35f;
        /// <summary>自动治疗阈值：玩家血量比例 ≤ 此值时 P2 主动释放治疗技能（不依赖攻击状态/J 面板切换）。</summary>
        [Export(PropertyHint.Range, "0.05,1,0.01")] public float HealThresholdRatio { get; set; } = 0.5f;
        [Export(PropertyHint.Range, "10,2000,1")] public float EnemyDangerDistance { get; set; } = 320f;
        [Export(PropertyHint.Range, "1,30,0.5")] public float QuietSceneReminderSeconds { get; set; } = 9f;
        /// <summary>治疗规则冷却（秒）：低血被攻击 → 用食物。</summary>
        [Export(PropertyHint.Range, "0.5,30,0.5")] public float HealRuleCooldownSeconds { get; set; } = 5.5f;
        /// <summary>护盾规则冷却（秒）：敌人近身 → 放技能。</summary>
        [Export(PropertyHint.Range, "0.5,30,0.5")] public float ShieldRuleCooldownSeconds { get; set; } = 4.0f;
        /// <summary>武器拾取规则冷却（秒）：范围内有武器 → 前往拾取。</summary>
        [Export(PropertyHint.Range, "0.5,30,0.5")] public float WeaponFetchCooldownSeconds { get; set; } = 6f;
        /// <summary>AI 决策消费冷却（秒）：同一条 AI 决策映射执行的间隔。</summary>
        [Export(PropertyHint.Range, "0.2,10,0.1")] public float AiDecisionConsumeCooldownSeconds { get; set; } = 1.5f;

        private GameStateProvider? _gameStateProvider;
        private P2SupportExecutor? _supportExecutor;
        private P2SupportDecisionBridge? _decisionBridge;
        private AiDecisionBridge? _aiDecisionBridge;
        private P2WeaponCarrier? _weaponCarrier;
        private float _tickAccum;
        private ulong _globalNextHintAtMs;
        private ulong _nextAiRequestAtMs;
        private ulong _nextPersonalityChatterAtMs;
        private string _lastConsumedAiDecisionSignature = string.Empty;
        private string _lastPersonalitySourceSignature = string.Empty;
        private readonly Dictionary<string, ulong> _ruleCooldownUntilMs = new();

        public ulong LastEvaluateAtMs { get; private set; }
        public string LastTriggeredRuleKey { get; private set; } = string.Empty;
        public string LastDecisionJson { get; private set; } = string.Empty;
        public string LastAiRejectReason { get; private set; } = string.Empty;
        public bool HasAiDecisionBridge => _aiDecisionBridge != null && IsInstanceValid(_aiDecisionBridge) && _aiDecisionBridge.IsInsideTree();
        public bool IsAiRequestInFlight => _aiDecisionBridge?.RequestInFlight == true;
        public string LastAiDecisionIntent => _aiDecisionBridge?.LastStructuredDecision?.Intent ?? string.Empty;
        public string LastAiDecisionUrgency => _aiDecisionBridge?.LastStructuredDecision?.Urgency ?? string.Empty;
        public string LastAiDecisionParseError => _aiDecisionBridge?.LastDecisionParseError ?? string.Empty;
        public string LastConsumedAiDecisionSignature => _lastConsumedAiDecisionSignature;
        public ulong TotalDecisionsEmitted { get; private set; }
        public ulong TotalDecisionsApplied { get; private set; }
        public ulong TotalDecisionsRejected { get; private set; }
        public ulong TotalFallbackHints { get; private set; }
        public ulong TotalAiMappedApplied { get; private set; }
        public ulong TotalPersonalityChatters { get; private set; }

        public override void _Process(double delta)
        {
            ResolveDependencies();
            if (_gameStateProvider == null || _supportExecutor == null)
            {
                return;
            }

            _tickAccum += (float)delta;
            if (_tickAccum < Mathf.Max(0.1f, EvaluateIntervalSeconds))
            {
                return;
            }

            _tickAccum = 0f;
            Evaluate(_gameStateProvider.CaptureGameState());
        }

        private void Evaluate(GameState state)
        {
            LastEvaluateAtMs = Time.GetTicksMsec();

            // Personality chatter runs on its own low-frequency gate and should not depend on rule branch returns.
            TryEmitPersonalityChatter(state);

            bool aiDecisionApplied = EnableAiDecisionBridge && TryEmitAiDecision(state);
            if (aiDecisionApplied && AiDecisionHasPriority)
            {
                return;
            }

            if (state.PlayerMaxHp <= 0)
            {
                return;
            }

            float hpRatio = state.PlayerHp / (float)Mathf.Max(1, state.PlayerMaxHp);

            // 低血自动治疗（技能路径）：血量 ≤ HealThresholdRatio 即触发治疗技能，
            // 独立于攻击状态/J 面板切换——Executor 按 target "heal" 解析出治疗技能执行 ApplyHeal。
            // 不 return：技能治疗失败（冷却中/满血）时，后续极低血+被攻击仍可落食物路径兜底
            // （全局冷却会阻止同帧重复决策）。
            if (hpRatio <= HealThresholdRatio)
            {
                TryEmitDecision(
                    ruleKey: "heal_low_hp",
                    decision: SupportDecision.TriggerSupportSkill(
                        sourceRule: "heal_low_hp",
                        reason: "player hp below heal threshold",
                        target: "heal",
                        urgency: "high"),
                    perRuleCooldownSeconds: HealRuleCooldownSeconds);
            }

            if (hpRatio <= LowHpThresholdRatio && state.PlayerUnderAttack)
            {
                TryEmitDecision(
                    ruleKey: "low_hp_under_attack",
                    decision: SupportDecision.UseSupportItem(
                        sourceRule: "low_hp_under_attack",
                        reason: "player hp below threshold while under attack",
                        itemTag: ItemTagIds.Food,
                        urgency: "high"),
                    perRuleCooldownSeconds: HealRuleCooldownSeconds);
                return;
            }

            if (state.AliveEnemyCount > 0 && state.NearestEnemyDistance > 0f && state.NearestEnemyDistance <= EnemyDangerDistance)
            {
                TryEmitDecision(
                    ruleKey: "enemy_too_close",
                    decision: SupportDecision.TriggerSupportSkill(
                        sourceRule: "enemy_too_close",
                        reason: "nearest enemy is within danger distance",
                        target: "player",
                        urgency: "medium"),
                    perRuleCooldownSeconds: ShieldRuleCooldownSeconds);
                return;
            }

            // 范围内存在可拾取武器（玩家近似距离）→ 自动前往拾取并拖回玩家旁
            if (WeaponNearby())
            {
                TryEmitDecision(
                    ruleKey: "weapon_nearby",
                    decision: SupportDecision.FetchWeapon(
                        sourceRule: "weapon_nearby",
                        reason: "weapon found in carry range",
                        urgency: "medium"),
                    perRuleCooldownSeconds: WeaponFetchCooldownSeconds);
                return;
            }

            if (state.AliveEnemyCount == 0)
            {
                TryEmitDecision(
                    ruleKey: "quiet_scene_pickup",
                    decision: SupportDecision.Hint(
                        // 无后缀 key：PushHint 会自动发现 dtl 中 quiet_scene_pickup_N 变体并随机
                        message: "quiet_scene_pickup",
                        sourceRule: "quiet_scene_pickup",
                        reason: "no alive enemies",
                        urgency: "low",
                        durationSeconds: 1.8f),
                    perRuleCooldownSeconds: QuietSceneReminderSeconds);
            }
        }

        /// <summary>到达目标武器回调：AI 判断该武器是否为玩家需要——
        /// 需要 → 拾取并挂载（随后触发 WeaponPickedUp 再决策）；不需要 → 放弃并前往下一把。</summary>
        private void OnWeaponTargetReached(ItemDefinition item)
        {
            if (_weaponCarrier == null) return;
            if (_weaponCarrier.IsWeaponDesired(item))
                _weaponCarrier.PickupAndHold();
            else
                _weaponCarrier.AbortAndFindNext();
        }

        /// <summary>拾取后回调：AI 判断——玩家需要 → 继续拖回玩家旁；不需要 → 立刻原地放置并交由后续规则换目标。</summary>
        private void OnWeaponPickedUp(ItemDefinition item)
        {
            if (_weaponCarrier == null) return;
            if (_weaponCarrier.IsWeaponDesired(item))
                _weaponCarrier.ReturnToPlayer();
            else
                _weaponCarrier.PlaceAtCurrent(); // 原地放置（随后触发 WeaponPlaced → 规则可再选下一把）
        }

        /// <summary>武器放置完成回调：从此刻开始拾取 CD（决策发出时设置的 CD 会被此处覆盖，
        /// 实际语义 = 放置完成 + WeaponFetchCooldownSeconds 后才能再次拾取）。</summary>
        private void OnWeaponPlaced(ItemDefinition item)
        {
            _ruleCooldownUntilMs["weapon_nearby"] = Time.GetTicksMsec() + SecondsToMs(WeaponFetchCooldownSeconds);
            LastTriggeredRuleKey = "weapon_nearby_placed";
        }

        /// <summary>CarryRangeMax 内（且距玩家 ≥ CarryRangeMin）是否存在可拾取的武器世界实体
        /// （world_items 组 + Weapon 类过滤，以玩家位置为基准近似）。</summary>
        private bool WeaponNearby()
        {
            var player = GetTree().GetFirstNodeInGroup("player") as Kuros.Core.GameActor;
            if (player == null) return false;

            foreach (Node node in GetTree().GetNodesInGroup("world_items"))
            {
                // 两类实体都支持：非投掷 WorldItemEntity 与投掷 RigidBodyWorldItemEntity（无继承，需分别读取）
                ItemDefinition? def = null;
                if (node is Kuros.Items.World.WorldItemEntity world)
                    def = world.ItemDefinition;
                else if (node is Kuros.Items.World.RigidBodyWorldItemEntity rigid)
                    def = rigid.ItemDefinition;
                if (def == null) continue;

                bool isWeapon = def.IsThrowWeapon
                    || string.Equals(def.Category, "Weapon", StringComparison.OrdinalIgnoreCase);
                if (!isWeapon) continue;

                float d = player.GlobalPosition.DistanceTo(((Node2D)node).GlobalPosition);
                if (d <= CarryRangeMax && d >= CarryRangeMin)
                    return true;
            }
            return false;
        }

        private bool TryEmitAiDecision(GameState state)
        {
            if (_decisionBridge == null)
            {
                LastAiRejectReason = "decision bridge not available";
                return false;
            }

            if (UseLiveAiDecisionSource)
            {
                RequestLiveAiDecisionIfNeeded();

                if (_aiDecisionBridge?.LastStructuredDecision?.IsValid == true)
                {
                    var live = _aiDecisionBridge.LastStructuredDecision;
                    string signature = BuildAiDecisionSignature(live);
                    if (ConsumeOnlyFreshAiDecision && signature == _lastConsumedAiDecisionSignature)
                    {
                        LastAiRejectReason = "ai decision unchanged";
                        return false;
                    }

                    if (!_decisionBridge.TryBuildDecisionFromAiDecision(live, out var mappedDecision, out string mapReject))
                    {
                        LastAiRejectReason = mapReject;
                        return false;
                    }

                    if (!_decisionBridge.TryValidateDecision(mappedDecision, state, out string liveValidateReject))
                    {
                        LastAiRejectReason = liveValidateReject;
                        return false;
                    }

                    LastAiRejectReason = string.Empty;
                    _lastConsumedAiDecisionSignature = signature;
                    TryEmitDecision("ai_bridge_live", mappedDecision, perRuleCooldownSeconds: AiDecisionConsumeCooldownSeconds);
                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(DebugAiSuggestionJson))
            {
                LastAiRejectReason = UseLiveAiDecisionSource ? "no valid live ai decision yet" : "empty ai suggestion json";
                return false;
            }

            if (!_decisionBridge.TryBuildDecisionFromJson(DebugAiSuggestionJson, out var aiDecision, out string parseReject))
            {
                LastAiRejectReason = parseReject;
                return false;
            }

            if (!_decisionBridge.TryValidateDecision(aiDecision, state, out string validateReject))
            {
                LastAiRejectReason = validateReject;
                return false;
            }

            LastAiRejectReason = string.Empty;
            TryEmitDecision("ai_bridge_debug", aiDecision, perRuleCooldownSeconds: AiDecisionConsumeCooldownSeconds);
            return true;
        }

        private void RequestLiveAiDecisionIfNeeded()
        {
            if (!RequestAiDecisionFromBridge || _aiDecisionBridge == null)
            {
                return;
            }

            ulong now = Time.GetTicksMsec();
            if (now < _nextAiRequestAtMs || _aiDecisionBridge.RequestInFlight)
            {
                return;
            }

            _ = _aiDecisionBridge.RequestDecisionAsync();
            _nextAiRequestAtMs = now + SecondsToMs(AiRequestIntervalSeconds);
        }

        private static string BuildAiDecisionSignature(AiDecision decision)
        {
            return string.Join("|", new[]
            {
                decision.Intent ?? string.Empty,
                decision.Target ?? string.Empty,
                decision.Urgency ?? string.Empty,
                decision.DurationSeconds.ToString("0.###"),
                decision.Reason ?? string.Empty
            });
        }

        private void TryEmitDecision(string ruleKey, SupportDecision decision, float perRuleCooldownSeconds)
        {
            ulong now = Time.GetTicksMsec();
            if (now < _globalNextHintAtMs)
            {
                return;
            }

            if (_ruleCooldownUntilMs.TryGetValue(ruleKey, out ulong untilMs) && now < untilMs)
            {
                return;
            }

            LastTriggeredRuleKey = ruleKey;
            LastDecisionJson = decision.ToJson(pretty: false);
            TotalDecisionsEmitted++;

            bool applied = _supportExecutor?.TryExecute(decision) == true;
            if (!applied && _supportExecutor != null)
            {
                TotalDecisionsRejected++;
                string fallbackMessage = BuildFallbackHint(ruleKey);
                // 兜底返回空串表示该场景无需提示（如玩家已有护盾），跳过兜底
                if (!string.IsNullOrWhiteSpace(fallbackMessage))
                {
                    var fallback = SupportDecision.Hint(
                        message: fallbackMessage,
                        sourceRule: $"{ruleKey}_fallback_hint",
                        reason: $"fallback because primary decision rejected: {_supportExecutor.LastRejectedReason}",
                        urgency: "medium",
                        durationSeconds: 1.8f);

                    LastTriggeredRuleKey = $"{ruleKey}_fallback_hint";
                    LastDecisionJson = fallback.ToJson(pretty: false);
                    TotalFallbackHints++;
                    bool fallbackApplied = _supportExecutor.TryExecute(fallback);
                    if (fallbackApplied)
                    {
                        TotalDecisionsApplied++;
                    }
                }
            }
            else if (applied)
            {
                TotalDecisionsApplied++;
                if (ruleKey.StartsWith("ai_bridge", System.StringComparison.Ordinal))
                {
                    TotalAiMappedApplied++;
                }
            }

            _globalNextHintAtMs = now + SecondsToMs(GlobalHintCooldownSeconds);
            _ruleCooldownUntilMs[ruleKey] = now + SecondsToMs(perRuleCooldownSeconds);
        }

        private void TryEmitPersonalityChatter(GameState state)
        {
            if (!EnableAiPersonalityChatter || _supportExecutor == null || _aiDecisionBridge?.LastStructuredDecision?.IsValid != true)
            {
                return;
            }

            if (PersonalityChatterOnlyWhenSafe && state.AliveEnemyCount > 0)
            {
                return;
            }

            ulong now = Time.GetTicksMsec();
            if (now < _nextPersonalityChatterAtMs)
            {
                return;
            }

            if (GD.Randf() > Mathf.Clamp(PersonalityChatterChance, 0f, 1f))
            {
                _nextPersonalityChatterAtMs = now + SecondsToMs(PersonalityChatterMinIntervalSeconds * 0.5f);
                return;
            }

            var decision = _aiDecisionBridge.LastStructuredDecision;
            string sourceSignature = BuildAiDecisionSignature(decision);
            if (sourceSignature == _lastPersonalitySourceSignature)
            {
                _nextPersonalityChatterAtMs = now + SecondsToMs(PersonalityChatterMinIntervalSeconds * 0.75f);
                return;
            }

            string text = BuildPersonalityText(decision);
            if (string.IsNullOrWhiteSpace(text))
            {
                _nextPersonalityChatterAtMs = now + SecondsToMs(PersonalityChatterMinIntervalSeconds * 0.5f);
                return;
            }

            var hint = SupportDecision.HintRaw(
                rawText: text,
                sourceRule: "ai_personality_chatter",
                reason: "ambient chatter from live ai decision",
                urgency: "low",
                durationSeconds: 2.4f,
                target: "player");

            if (_supportExecutor.TryExecute(hint))
            {
                _lastPersonalitySourceSignature = sourceSignature;
                TotalPersonalityChatters++;
            }

            _nextPersonalityChatterAtMs = now + SecondsToMs(PersonalityChatterMinIntervalSeconds);
        }

        private string BuildPersonalityText(AiDecision decision)
        {
            string reason = (decision.Reason ?? string.Empty).Trim();
            string intent = (decision.Intent ?? string.Empty).Trim().ToLowerInvariant();

            string prefix = intent switch
            {
                "attack" => "我觉得可以主动压一下，",
                "use_skill" => "这波节奏不错，",
                "retreat" => "先别贪，我建议稳一手，",
                "reposition" => "换个站位更舒服，",
                "loot" => "安全的话顺手摸掉落，",
                _ => "我这边判断是，"
            };

            string core = string.IsNullOrWhiteSpace(reason) ? "当前局势可以再快一点。" : reason;
            string text = $"{prefix}{core}";
            if (text.Length > Mathf.Max(8, PersonalityChatterMaxChars))
            {
                text = text[..Mathf.Max(8, PersonalityChatterMaxChars)] + "...";
            }

            return text;
        }

        /// <summary>生成兜底提示 key：按被拒规则选择对应文本；返回空串表示不触发兜底提示。
        /// enemy_too_close（护盾决策）在玩家已有护盾时返回空——有盾即无需告知"暂不可用"。</summary>
        private string BuildFallbackHint(string ruleKey)
        {
            if (ruleKey == "low_hp_under_attack")
            {
                return "fallback_low_hp";
            }

            if (ruleKey == "enemy_too_close")
            {
                // 玩家当前持有护盾：护盾决策被拒无需兜底提示（已有盾，不必提示"暂不可用"）
                if (_supportExecutor != null && _supportExecutor.GetActiveShieldPoints() > 0)
                {
                    return string.Empty;
                }

                return "fallback_enemy_close";
            }

            return "fallback_generic";
        }

        private void ResolveDependencies()
        {
            if (_gameStateProvider == null || !IsInstanceValid(_gameStateProvider) || !_gameStateProvider.IsInsideTree())
            {
                _gameStateProvider = GetNodeOrNull<GameStateProvider>(GameStateProviderPath)
                    ?? GetNodeOrNull<GameStateProvider>(NormalizeRelativePath(GameStateProviderPath))
                    ?? GetTree().GetFirstNodeInGroup("player")?.GetNodeOrNull<GameStateProvider>("GameStateProvider");
            }

            var nextCarrier = _weaponCarrier
                ?? GetNodeOrNull<P2WeaponCarrier>(WeaponCarrierPath)
                ?? GetNodeOrNull<P2WeaponCarrier>(NormalizeRelativePath(WeaponCarrierPath));
            if (!ReferenceEquals(nextCarrier, _weaponCarrier))
            {
                if (_weaponCarrier != null)
                {
                    _weaponCarrier.WeaponTargetReached -= OnWeaponTargetReached;
                    _weaponCarrier.WeaponPickedUp -= OnWeaponPickedUp;
                    _weaponCarrier.WeaponPlaced -= OnWeaponPlaced;
                }
                _weaponCarrier = nextCarrier;
                if (_weaponCarrier != null)
                {
                    // 决策点事件：到达武器 / 拾取后 → AI 判断武器是否玩家需要（改向或中途放置）
                    _weaponCarrier.WeaponTargetReached += OnWeaponTargetReached;
                    _weaponCarrier.WeaponPickedUp += OnWeaponPickedUp;
                    _weaponCarrier.WeaponPlaced += OnWeaponPlaced; // 放置完成 → 开始拾取 CD
                }
            }

            if (_supportExecutor == null || !IsInstanceValid(_supportExecutor) || !_supportExecutor.IsInsideTree())
            {
                _supportExecutor = GetNodeOrNull<P2SupportExecutor>(SupportExecutorPath)
                    ?? GetNodeOrNull<P2SupportExecutor>(NormalizeRelativePath(SupportExecutorPath));
            }

            if (_decisionBridge == null || !IsInstanceValid(_decisionBridge) || !_decisionBridge.IsInsideTree())
            {
                _decisionBridge = GetNodeOrNull<P2SupportDecisionBridge>(SupportDecisionBridgePath)
                    ?? GetNodeOrNull<P2SupportDecisionBridge>(NormalizeRelativePath(SupportDecisionBridgePath));
            }

            if (_aiDecisionBridge == null || !IsInstanceValid(_aiDecisionBridge) || !_aiDecisionBridge.IsInsideTree())
            {
                _aiDecisionBridge = GetNodeOrNull<AiDecisionBridge>(AiDecisionBridgePath)
                    ?? GetNodeOrNull<AiDecisionBridge>(NormalizeRelativePath(AiDecisionBridgePath));
            }
        }

        private static ulong SecondsToMs(float seconds)
        {
            return (ulong)Mathf.RoundToInt(Mathf.Max(0f, seconds) * 1000f);
        }

        private static NodePath NormalizeRelativePath(NodePath path)
        {
            string text = path.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("../", System.StringComparison.Ordinal))
            {
                return path;
            }

            return new NodePath($"../{text}");
        }
    }
}
using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Items.Tags;
using Kuros.Managers;

namespace Kuros.Companions
{
    /// <summary>
    /// P2 支持动作执行器：通过本地白名单应用结构化的支持决策（SupportDecision）。
    /// 负责意图分发（show_hint / trigger_support_skill / move_to / fetch_weapon / hold）、
    /// 技能/物品冷却、护盾拦截玩家伤害、治疗与装备加成、骨骼 action 动画触发。
    /// 由 AI_Brain（规则/LLM）发出决策，本类校验并执行。
    /// </summary>
    public partial class P2SupportExecutor : Node
    {
        /// <summary>决策成功执行时触发（携带决策 JSON，供调试面板显示）。</summary>
        [Signal] public delegate void DecisionAppliedEventHandler(string decisionJson);
        /// <summary>决策被拒绝时触发（携带拒绝原因）。</summary>
        [Signal] public delegate void DecisionRejectedEventHandler(string reason);
        /// <summary>技能/装备切换时触发（携带新技能 ID 与装备 ID）。</summary>
        [Signal] public delegate void LoadoutChangedEventHandler(string supportSkillId, string equipmentId);

        [ExportCategory("References")]
        /// <summary>P2 总控制器路径（父节点）。</summary>
        [Export] public NodePath CompanionControllerPath { get; set; } = new("..");
        /// <summary>玩家节点路径（Stage 中兄弟节点）。</summary>
        [Export] public NodePath PlayerPath { get; set; } = new("../MainCharacter");
        /// <summary>武器搬运组件路径（P2 的子节点 AI_WeaponCarrier）。</summary>
        [Export] public NodePath WeaponCarrierPath { get; set; } = new("AI_WeaponCarrier");
        /// <summary>对话控制器路径（P2 的子节点 AI_Dialogue，气泡逻辑）。</summary>
        [Export] public NodePath DialogueControllerPath { get; set; } = new("AI_Dialogue");

        [ExportCategory("Support Execution")]
        /// <summary>默认支持的武器技能动作名（技能处理器使用）。</summary>
        [Export] public string DefaultSupportSkillAction { get; set; } = "weapon_skill_block";
        /// <summary>支持技能执行冷却（秒）。</summary>
        [Export(PropertyHint.Range, "0,20,0.1")] public float SupportSkillCooldownSeconds { get; set; } = 3.0f;
        /// <summary>执行日志开关。</summary>
        [Export] public bool EnableLogging { get; set; } = false;

        [ExportCategory("Move Decision")]
        /// <summary>move_to 决策（远离敌人）的目标距离：玩家位置 + 远离方向 × 此值。</summary>
        [Export(PropertyHint.Range, "100,2000,50")] public float MoveAwayDistance { get; set; } = 600f;

        [ExportCategory("Shield VFX")]
        /// <summary>护盾格挡时玩家的闪光颜色。</summary>
        [Export] public Color ShieldBlockFlashColor { get; set; } = new Color(0.55f, 0.85f, 1f, 1f);
        /// <summary>闪光总时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.05,0.6,0.01")] public float ShieldBlockFlashDuration { get; set; } = 0.16f;
        /// <summary>闪光强度（0~1，向闪光色插值比例）。</summary>
        [Export(PropertyHint.Range, "0.1,1,0.01")] public float ShieldBlockFlashStrength { get; set; } = 0.58f;

        [ExportCategory("P2 Loadout")]
        /// <summary>可用支持技能列表（默认加载 ShieldTest/HealTest）。</summary>
        [Export] public Godot.Collections.Array<P2SupportSkillDefinition> SupportSkills { get; set; } = new();
        /// <summary>可用装备列表（默认加载 HealAmp10）。</summary>
        [Export] public Godot.Collections.Array<P2SupportEquipmentDefinition> SupportEquipments { get; set; } = new();
        /// <summary>当前装备的支持技能 ID（默认护盾测试技能）。</summary>
        [Export] public string EquippedSupportSkillId { get; set; } = "p2_skill_shield_test";
        /// <summary>当前装备的装备 ID（默认治疗增幅 10%）。</summary>
        [Export] public string EquippedEquipmentId { get; set; } = "p2_equipment_heal_amp_10";

        private P2CompanionController? _companionController; // P2 总控制器
        private P2WeaponCarrier? _weaponCarrier;             // 武器搬运组件（fetch_weapon 转发）
        private P2DialogueController? _dialogue;             // 对话控制器（气泡/Speak）
        private global::SamplePlayer? _player;               // 玩家引用（治疗/护盾目标）
        private global::SamplePlayer? _shieldBoundPlayer;    // 当前绑定护盾拦截的玩家（防重复订阅）

        // ── 状态/统计暴露（供 P2DebugPanel 显示） ──
        /// <summary>最近应用的决策 JSON。</summary>
        public string LastAppliedDecisionJson { get; private set; } = string.Empty;
        /// <summary>最近拒绝原因。</summary>
        public string LastRejectedReason { get; private set; } = string.Empty;
        /// <summary>最近意图名。</summary>
        public string LastIntent { get; private set; } = string.Empty;
        /// <summary>最近决策时间戳。</summary>
        public ulong LastDecisionAtMs { get; private set; }
        /// <summary>最近执行结果（none/applied/rejected）。</summary>
        public string LastResult { get; private set; } = "none";
        /// <summary>最近执行的动作细节。</summary>
        public string LastActionDetail { get; private set; } = string.Empty;
        /// <summary>累计决策请求数。</summary>
        public ulong TotalDecisionRequests { get; private set; }
        /// <summary>累计成功执行数。</summary>
        public ulong TotalDecisionApplied { get; private set; }
        /// <summary>累计拒绝数。</summary>
        public ulong TotalDecisionRejected { get; private set; }
        /// <summary>护盾累计吸收的伤害。</summary>
        public int TotalShieldAbsorbedDamage { get; private set; }
        /// <summary>技能累计治疗量。</summary>
        public int TotalHealFromSkills { get; private set; }

        // ── 冷却与护盾状态 ──
        private readonly Dictionary<string, ulong> _supportSkillCooldownsMs = new(StringComparer.OrdinalIgnoreCase); // 技能冷却表（技能ID → 可用时间戳）
        private int _activeShieldPoints;      // 当前护盾剩余点数
        private ulong _shieldExpireAtMs;      // 护盾过期时间戳
        private Tween? _shieldFlashTween;     // 护盾格挡闪光动画
        private Color _playerBaseModulate = Colors.White; // 玩家原始 Modulate（格挡闪光恢复目标，防连续伤害累积污染）
        private Node? _shieldEffectInstance;  // 护盾特效实例（随护盾生命周期销毁：施加生成，超时/打破时消失）

        // ── 默认资源路径（无配置时自动加载） ──
        private const string ShieldSkillResourcePath = "res://resources/companions/P2SupportSkill_ShieldTest.tres";
        private const string HealSkillResourcePath = "res://resources/companions/P2SupportSkill_HealTest.tres";
        private const string HealAmpEquipmentResourcePath = "res://resources/companions/P2SupportEquipment_HealAmp10.tres";

        public override void _Ready()
        {
            ResolveDependencies();
            EnsureDefaultLoadoutResources(); // 兜底加载默认技能/装备
            SetProcess(true);
        }

        public override void _ExitTree()
        {
            UnbindShieldInterceptor(); // 退订玩家伤害拦截，防悬挂回调
            _shieldEffectInstance?.QueueFree(); // 清理护盾特效，防悬挂
            _shieldEffectInstance = null;
            base._ExitTree();
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (_activeShieldPoints <= 0)
            {
                return;
            }

            // 护盾到期自动清除
            if (Time.GetTicksMsec() >= _shieldExpireAtMs)
            {
                ClearShieldState(notifyHint: true);
            }
        }

        // ── Loadout 查询接口（供 P2LoadoutPanel 使用） ──

        /// <summary>获取可用技能列表。</summary>
        public Godot.Collections.Array<P2SupportSkillDefinition> GetSupportSkills() => SupportSkills;

        /// <summary>获取可用装备列表。</summary>
        public Godot.Collections.Array<P2SupportEquipmentDefinition> GetSupportEquipments() => SupportEquipments;

        /// <summary>获取当前装备技能 ID。</summary>
        public string GetEquippedSupportSkillId() => EquippedSupportSkillId;

        /// <summary>获取当前装备 ID。</summary>
        public string GetEquippedEquipmentId() => EquippedEquipmentId;

        /// <summary>获取当前护盾剩余点数。</summary>
        public int GetActiveShieldPoints() => Mathf.Max(0, _activeShieldPoints);

        /// <summary>获取护盾剩余时间（秒），无护盾返回 0。</summary>
        public float GetShieldRemainingSeconds()
        {
            if (_activeShieldPoints <= 0 || _shieldExpireAtMs == 0)
            {
                return 0f;
            }

            ulong now = Time.GetTicksMsec();
            if (now >= _shieldExpireAtMs)
            {
                return 0f;
            }

            return (_shieldExpireAtMs - now) / 1000f;
        }

        /// <summary>获取当前装备技能的剩余冷却（秒）。</summary>
        public float GetSupportSkillCooldownRemainingSeconds()
        {
            var skill = FindSkillById(EquippedSupportSkillId);
            return GetSupportSkillCooldownRemainingSeconds(skill);
        }

        /// <summary>获取指定技能的剩余冷却（秒）。</summary>
        public float GetSupportSkillCooldownRemainingSeconds(string skillId)
        {
            var skill = FindSkillById(skillId);
            return GetSupportSkillCooldownRemainingSeconds(skill);
        }

        private float GetSupportSkillCooldownRemainingSeconds(P2SupportSkillDefinition? skill)
        {
            ulong now = Time.GetTicksMsec();
            ulong nextAt = GetSupportSkillNextAvailableAtMs(skill);
            if (now >= nextAt)
            {
                return 0f;
            }

            return (nextAt - now) / 1000f;
        }

        /// <summary>获取当前装备的治疗倍率（最小 0.1）。</summary>
        public float GetCurrentHealPowerMultiplier()
        {
            var equipment = FindEquipmentById(EquippedEquipmentId);
            if (equipment == null)
            {
                return 1f;
            }

            return Mathf.Max(0.1f, equipment.HealPowerMultiplier);
        }

        /// <summary>切换装备技能（无效 ID 返回 false）。</summary>
        public bool EquipSupportSkill(string skillId)
        {
            if (FindSkillById(skillId) == null)
            {
                return false;
            }

            EquippedSupportSkillId = skillId;
            EmitSignal(SignalName.LoadoutChanged, EquippedSupportSkillId, EquippedEquipmentId);
            return true;
        }

        /// <summary>切换装备（无效 ID 返回 false）。</summary>
        public bool EquipSupportEquipment(string equipmentId)
        {
            if (FindEquipmentById(equipmentId) == null)
            {
                return false;
            }

            EquippedEquipmentId = equipmentId;
            EmitSignal(SignalName.LoadoutChanged, EquippedSupportSkillId, EquippedEquipmentId);
            return true;
        }

        /// <summary>
        /// 执行一条支持决策（意图白名单入口）。
        /// 未知意图拒绝；每意图的成功/拒绝都会记录统计并发出信号。
        /// </summary>
        public bool TryExecute(SupportDecision decision)
        {
            ResolveDependencies();
            TotalDecisionRequests++;

            if (_companionController == null)
            {
                TotalDecisionRejected++;
                LastResult = "rejected";
                EmitSignal(SignalName.DecisionRejected, "companion controller not available");
                return false;
            }

            if (decision == null || !decision.IsValid)
            {
                TotalDecisionRejected++;
                LastResult = "rejected";
                EmitSignal(SignalName.DecisionRejected, "invalid support decision");
                return false;
            }

            string intent = decision.Intent.Trim().ToLowerInvariant();
            LastIntent = intent;
            LastDecisionAtMs = Time.GetTicksMsec();
            switch (intent)
            {
                case "show_hint":
                    // 预定义 Dialogic 气泡（hintKey 对应 p2_hint.dtl 的 label）
                    _dialogue?.PushHint(decision.Message);
                    if (EnableLogging)
                    {
                        GD.Print($"[P2SupportExecutor] applied show_hint: {decision.Message}");
                    }
                    LastAppliedDecisionJson = decision.ToJson(pretty: false);
                    LastRejectedReason = string.Empty;
                    LastResult = "applied";
                    LastActionDetail = decision.Message;
                    TotalDecisionApplied++;
                    EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
                    return true;

                case "show_hint_raw":
                    // 动态文本气泡（运行时生成，不依赖 dtl）
                    _dialogue?.PushHintDirect(decision.Message);
                    if (EnableLogging)
                    {
                        GD.Print($"[P2SupportExecutor] applied show_hint_raw: {decision.Message}");
                    }
                    LastAppliedDecisionJson = decision.ToJson(pretty: false);
                    LastRejectedReason = string.Empty;
                    LastResult = "applied";
                    LastActionDetail = decision.Message;
                    TotalDecisionApplied++;
                    EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
                    return true;

                case "hold":
                    // 无动作（等待）
                    LastAppliedDecisionJson = decision.ToJson(pretty: false);
                    LastRejectedReason = string.Empty;
                    LastResult = "applied";
                    LastActionDetail = "hold";
                    TotalDecisionApplied++;
                    EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
                    return true;

                case "trigger_support_skill":
                    return ExecuteSupportSkill(decision);

                case "move_to":
                    return ExecuteMoveTo(decision);

                case "fetch_weapon":
                    return ExecuteFetchWeapon(decision);

                default:
                    // 白名单外意图拒绝
                    LastRejectedReason = $"intent '{intent}' is not in whitelist";
                    LastResult = "rejected";
                    LastActionDetail = intent;
                    TotalDecisionRejected++;
                    EmitSignal(SignalName.DecisionRejected, $"intent '{intent}' is not in whitelist");
                    return false;
            }
        }

        /// <summary>执行拾取武器决策（fetch_weapon）：交给 P2WeaponCarrier 前往拾取并拖回玩家旁。</summary>
        private bool ExecuteFetchWeapon(SupportDecision decision)
        {
            ResolveDependencies();
            if (_weaponCarrier == null)
            {
                Reject("fetch_weapon", "weapon carrier not available");
                return false;
            }

            if (_weaponCarrier.IsBusy || _weaponCarrier.IsCarrying)
            {
                Reject("fetch_weapon", "carrier busy or already carrying");
                return false;
            }

            if (!_weaponCarrier.StartFetchNearestWeapon())
            {
                Reject("fetch_weapon", "范围内没有可拾取的武器");
                return false;
            }

            _dialogue?.Speak(P2DialogueEvent.WeaponFetchStart); // 出发拾取气泡（fetch_weapon_N 随机变体）

            if (EnableLogging)
                GD.Print("[P2SupportExecutor] applied fetch_weapon");
            LastAppliedDecisionJson = decision.ToJson(pretty: false);
            LastRejectedReason = string.Empty;
            LastResult = "applied";
            LastActionDetail = "fetch_weapon";
            TotalDecisionApplied++;
            EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
            return true;
        }

        /// <summary>执行移动决策（move_to）：解析目标世界坐标 → 交给 P2CompanionController.SetMoveTarget。
        /// 目标语义：away_enemy = 远离最近敌人方向；offset:x:y = 相对玩家偏移。</summary>
        private bool ExecuteMoveTo(SupportDecision decision)
        {
            ResolveDependencies();
            if (_companionController == null)
            {
                Reject("move_to", "companion controller not available");
                return false;
            }

            Vector2? target = ResolveMoveTarget(decision.Target);
            if (!target.HasValue)
            {
                Reject("move_to", $"无法解析移动目标 '{decision.Target}'（支持 away_enemy / offset:x:y）");
                return false;
            }

            _companionController.SetMoveTarget(target.Value);
            if (EnableLogging)
                GD.Print($"[P2SupportExecutor] applied move_to: {decision.Target} → {target.Value}");
            LastAppliedDecisionJson = decision.ToJson(pretty: false);
            LastRejectedReason = string.Empty;
            LastResult = "applied";
            LastActionDetail = decision.Target;
            TotalDecisionApplied++;
            EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
            return true;
        }

        /// <summary>解析移动目标：away_enemy → 玩家位置 + 远离最近敌人方向 × MoveAwayDistance；offset:x:y → 玩家位置 + 偏移。</summary>
        private Vector2? ResolveMoveTarget(string target)
        {
            if (_player == null) return null;

            if (target == "away_enemy" || target == "nearest_enemy_away")
            {
                var enemy = FindNearestEnemy();
                if (enemy == null) return null;
                Vector2 away = (_player.GlobalPosition - enemy.GlobalPosition).Normalized();
                if (away == Vector2.Zero) away = Vector2.Right;
                return _player.GlobalPosition + away * MoveAwayDistance;
            }

            if (target.StartsWith("offset:", StringComparison.Ordinal))
            {
                string[] parts = target.Substring("offset:".Length).Split(':');
                if (parts.Length == 2
                    && float.TryParse(parts[0], out float x)
                    && float.TryParse(parts[1], out float y))
                {
                    return _player.GlobalPosition + new Vector2(x, y);
                }
            }

            return null;
        }

        /// <summary>查找距玩家最近的存活敌人（move_to away_enemy 用）。</summary>
        private GameActor? FindNearestEnemy()
        {
            if (_player == null) return null;
            GameActor? nearest = null;
            float best = float.MaxValue;
            foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            {
                if (node is not GameActor actor || actor.IsDead || actor.IsDeathSequenceActive) continue;
                float d = _player.GlobalPosition.DistanceTo(actor.GlobalPosition);
                if (d < best)
                {
                    best = d;
                    nearest = actor;
                }
            }
            return nearest;
        }

        /// <summary>统一拒绝处理：记录统计并发出 DecisionRejected 信号。</summary>
        private void Reject(string intent, string reason)
        {
            LastRejectedReason = reason;
            LastResult = "rejected";
            LastActionDetail = intent;
            TotalDecisionRejected++;
            EmitSignal(SignalName.DecisionRejected, reason);
        }

        /// <summary>执行护盾技能：解析技能 → 冷却检查 → 技能 Handler 执行 → 成功触发 action 动画 + 设置技能冷却。</summary>
        private bool ExecuteSupportSkill(SupportDecision decision)
        {
            if (_player == null)
            {
                TotalDecisionRejected++;
                LastRejectedReason = "player not available for support skill";
                LastResult = "rejected";
                LastActionDetail = "trigger_support_skill";
                EmitSignal(SignalName.DecisionRejected, "player not available for support skill");
                return false;
            }

            // 按决策目标解析技能（heal/shield 关键词），回退当前装备技能
            var skill = ResolveSkillForDecision(decision.Target) ?? FindSkillById(EquippedSupportSkillId);
            if (skill == null)
            {
                TotalDecisionRejected++;
                LastRejectedReason = "no equipped support skill available";
                LastResult = "rejected";
                LastActionDetail = "trigger_support_skill";
                EmitSignal(SignalName.DecisionRejected, "no equipped support skill available");
                return false;
            }

            // 技能冷却检查
            ulong now = Time.GetTicksMsec();
            ulong nextAt = GetSupportSkillNextAvailableAtMs(skill);
            if (now < nextAt)
            {
                float remain = (nextAt - now) / 1000f;
                TotalDecisionRejected++;
                LastRejectedReason = $"support skill on cooldown ({remain:0.0}s)";
                LastResult = "rejected";
                LastActionDetail = skill.SkillId;
                EmitSignal(SignalName.DecisionRejected, LastRejectedReason);
                return false;
            }

            // 技能 Handler 执行（如护盾 Handler → ApplyShield）
            string detail = string.Empty;
            string rejectReason = string.Empty;
            bool executed = skill.Handler?.TryExecute(this, skill, out detail, out rejectReason) == true;
            if (!executed)
            {
                TotalDecisionRejected++;
                LastRejectedReason = string.IsNullOrWhiteSpace(rejectReason)
                    ? $"support skill handler failed: {skill.SkillId}"
                    : rejectReason;
                LastResult = "rejected";
                LastActionDetail = "trigger_support_skill";
                EmitSignal(SignalName.DecisionRejected, LastRejectedReason);
                return false;
            }

            if (EnableLogging)
            {
                GD.Print($"[P2SupportExecutor] applied trigger_support_skill: {detail}");
            }

            _companionController?.TriggerAction(); // 动作成功 → 两阶段 action 动画（接近玩家 → 播放）

            LastAppliedDecisionJson = decision.ToJson(pretty: false);
            LastRejectedReason = string.Empty;
            LastResult = "applied";
            LastActionDetail = detail;
            TotalDecisionApplied++;
            SetSupportSkillCooldown(skill, now); // 成功后设置冷却
            EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
            return true;
        }

        /// <summary>
        /// 施加护盾（技能 Handler 调用）：累加护盾点数 + 设定过期时间 + 绑定玩家伤害拦截 + 更新玩家护盾值 + 气泡提示。
        /// </summary>
        public bool ApplyShield(int shieldAmount, float durationSeconds, string skillId, out string detail, out string rejectReason)
        {
            rejectReason = string.Empty;
            if (_player == null)
            {
                rejectReason = "player not available for shield skill";
                detail = "shield_player_missing";
                return false;
            }

            if (_player.IsDeathSequenceActive || _player.IsDead)
            {
                rejectReason = "player is dying or dead, shield rejected";
                detail = "shield_player_dead";
                return false;
            }

            int addShield = Mathf.Max(1, shieldAmount);
            float duration = Mathf.Max(0.5f, durationSeconds);

            _activeShieldPoints += addShield;
            _shieldExpireAtMs = Time.GetTicksMsec() + SecondsToMs(duration);
            BindShieldInterceptor();
            _player.SetShieldValue(_activeShieldPoints);

            // 护盾特效（ActionEffectScenes[0]）：记录实例，随护盾生命周期销毁
            _shieldEffectInstance = _companionController?.SpawnActionEffect(0);
            _dialogue?.Speak(P2DialogueEvent.ShieldApplied, _activeShieldPoints);
            detail = $"{skillId}|shield={_activeShieldPoints}|dur={duration:0.0}s";
            return true;
        }

        /// <summary>
        /// 治疗（技能 Handler 调用）：满血拒绝；按装备倍率计算治疗量并恢复玩家生命 + 气泡提示。
        /// </summary>
        public bool ApplyHeal(int healAmount, string skillId, out string detail, out string rejectReason)
        {
            rejectReason = string.Empty;
            if (_player == null)
            {
                rejectReason = "player not available for heal skill";
                detail = "heal_player_missing";
                return false;
            }

            if (_player.IsDeathSequenceActive || _player.IsDead)
            {
                rejectReason = "player is dying or dead, heal rejected";
                detail = "heal_player_dead";
                return false;
            }

            if (_player.MaxHealth <= 0 || _player.CurrentHealth >= _player.MaxHealth)
            {
                rejectReason = "player hp already full for heal skill";
                detail = "heal_full_hp";
                return false;
            }

            float multiplier = GetCurrentHealPowerMultiplier();
            int finalHeal = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, healAmount) * multiplier));
            int nextHealth = Mathf.Min(_player.MaxHealth, _player.CurrentHealth + finalHeal);
            _player.RestoreHealth(nextHealth, _player.MaxHealth);
            TotalHealFromSkills += finalHeal;

            // 治疗飘字（绿色 +数值 显示在玩家头上）
            FloatingDamageTextManager.Instance?.ShowFloatingHealing(finalHeal, _player.GlobalPosition, 0f);
            _companionController?.SpawnActionEffect(1); // 治疗特效（ActionEffectScenes[1]）

            _dialogue?.Speak(P2DialogueEvent.Healed, finalHeal);
            detail = $"{skillId}|heal={finalHeal}|mult={multiplier:0.00}";
            return true;
        }

        /// <summary>
        /// 玩家伤害拦截回调（绑定在玩家 DamageIntercepted）：护盾吸收伤害。
        /// 全吸收 → IsBlocked=true（整体拦截）；部分吸收 → 剩余伤害继续结算；护盾耗尽/过期自动清除。
        /// </summary>
        private bool OnPlayerDamageIntercepted(GameActor.DamageEventArgs args)
        {
            if (_activeShieldPoints <= 0)
            {
                return false;
            }

            if (Time.GetTicksMsec() >= _shieldExpireAtMs)
            {
                ClearShieldState(notifyHint: true);
                return false;
            }

            int incoming = Mathf.Max(0, args.Damage);
            if (incoming <= 0)
            {
                return false;
            }

            int absorbed = Mathf.Min(incoming, _activeShieldPoints);
            _activeShieldPoints -= absorbed;
            TotalShieldAbsorbedDamage += absorbed;
            _player?.SetShieldValue(_activeShieldPoints);
            args.Damage = Mathf.Max(0, incoming - absorbed);
            if (args.Damage <= 0)
            {
                args.IsBlocked = true; // 全吸收：整体拦截本次伤害
            }

            if (absorbed > 0)
            {
                PlayShieldBlockVfx(); // 格挡闪光
            }

            if (_activeShieldPoints <= 0)
            {
                ClearShieldState(notifyHint: true);
            }

            return args.IsBlocked;
        }

        /// <summary>清除护盾状态：销毁护盾特效 + 清零点数/过期时间 + 清除玩家护盾值 + 退订拦截 + 可选气泡提示。
        /// 所有护盾结束路径（超时/伤害耗尽）都汇聚于此，护盾特效随生命周期在此销毁。</summary>
        private void ClearShieldState(bool notifyHint)
        {
            if (_activeShieldPoints <= 0 && _shieldExpireAtMs == 0)
            {
                return;
            }

            _shieldEffectInstance?.QueueFree();
            _shieldEffectInstance = null;
            _activeShieldPoints = 0;
            _shieldExpireAtMs = 0;
            _player?.ClearShield();
            UnbindShieldInterceptor();
            if (notifyHint)
            {
                _dialogue?.Speak(P2DialogueEvent.ShieldExpired);
            }
        }

        /// <summary>绑定玩家伤害拦截（仅绑一次，玩家变更时先解绑旧的）。</summary>
        private void BindShieldInterceptor()
        {
            if (_player == null)
            {
                return;
            }

            if (!ReferenceEquals(_shieldBoundPlayer, _player))
            {
                UnbindShieldInterceptor();
                _shieldBoundPlayer = _player;
                _shieldBoundPlayer.DamageIntercepted += OnPlayerDamageIntercepted;
            }
        }

        /// <summary>解绑玩家伤害拦截（节点销毁/玩家变更时）。</summary>
        private void UnbindShieldInterceptor()
        {
            if (_shieldBoundPlayer == null)
            {
                return;
            }

            _shieldBoundPlayer.DamageIntercepted -= OnPlayerDamageIntercepted;
            _shieldBoundPlayer = null;
        }

        /// <summary>护盾格挡闪光：玩家 Modulate 短暂插值到闪光色再还原（Tween 驱动）。</summary>
        private void PlayShieldBlockVfx()
        {
            if (_player == null)
            {
                return;
            }

            if (_shieldFlashTween != null && _shieldFlashTween.IsRunning())
            {
                _shieldFlashTween.Kill();
            }

            // 恢复目标用缓存的玩家原始颜色：连续伤害时旧 Tween 被 Kill 停在中间色，
            // 若读当前 Modulate 会以污染色为基准越偏越远；缓存值保证最终一定还原
            Color baseColor = _playerBaseModulate;
            float strength = Mathf.Clamp(ShieldBlockFlashStrength, 0.1f, 1f);
            Color flashColor = baseColor.Lerp(ShieldBlockFlashColor, strength);

            float total = Mathf.Max(0.05f, ShieldBlockFlashDuration);
            float inDuration = total * 0.35f;
            float outDuration = total - inDuration;

            _shieldFlashTween = CreateTween();
            _shieldFlashTween.TweenProperty(_player, "modulate", flashColor, inDuration);
            _shieldFlashTween.TweenProperty(_player, "modulate", baseColor, outDuration);
        }

        /// <summary>兜底加载默认技能/装备（列表为空时），并修正装备 ID 有效性。</summary>
        private void EnsureDefaultLoadoutResources()
        {
            if (SupportSkills.Count == 0)
            {
                var shieldSkill = GD.Load<P2SupportSkillDefinition>(ShieldSkillResourcePath);
                var healSkill = GD.Load<P2SupportSkillDefinition>(HealSkillResourcePath);
                if (shieldSkill != null)
                {
                    SupportSkills.Add(shieldSkill);
                }

                if (healSkill != null)
                {
                    SupportSkills.Add(healSkill);
                }
            }

            if (SupportEquipments.Count == 0)
            {
                SupportEquipments.Add(new P2SupportEquipmentDefinition
                {
                    EquipmentId = "p2_equipment_none",
                    DisplayName = "无装备",
                    HealPowerMultiplier = 1.0f
                });

                var healAmp = GD.Load<P2SupportEquipmentDefinition>(HealAmpEquipmentResourcePath);
                if (healAmp != null)
                {
                    SupportEquipments.Add(healAmp);
                }
            }

            if (FindSkillById(EquippedSupportSkillId) == null && SupportSkills.Count > 0)
            {
                EquippedSupportSkillId = SupportSkills[0].SkillId;
            }

            if (FindEquipmentById(EquippedEquipmentId) == null && SupportEquipments.Count > 0)
            {
                EquippedEquipmentId = SupportEquipments[0].EquipmentId;
            }
        }

        /// <summary>按决策目标解析技能：空/player/self → 当前装备；ID 直接匹配；含 heal → 治疗技能；含 shield/block → 护盾技能。</summary>
        private P2SupportSkillDefinition? ResolveSkillForDecision(string rawTarget)
        {
            string target = (rawTarget ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(target) || target == "player" || target == "self")
            {
                return FindSkillById(EquippedSupportSkillId);
            }

            var direct = FindSkillById(target);
            if (direct != null)
            {
                return direct;
            }

            if (target.Contains("heal", StringComparison.Ordinal))
            {
                return FindSkillByType("heal") ?? FindSkillById(EquippedSupportSkillId);
            }

            if (target.Contains("shield", StringComparison.Ordinal) || target.Contains("block", StringComparison.Ordinal))
            {
                return FindSkillByType("shield") ?? FindSkillById(EquippedSupportSkillId);
            }

            return FindSkillById(EquippedSupportSkillId);
        }

        /// <summary>按技能类型名查找技能（如 heal/shield，依赖 P2SupportSkillDefinition.GetSkillTypeNormalized）。</summary>
        private P2SupportSkillDefinition? FindSkillByType(string type)
        {
            string normalized = (type ?? string.Empty).Trim().ToLowerInvariant();
            for (int i = 0; i < SupportSkills.Count; i++)
            {
                var skill = SupportSkills[i];
                if (skill == null)
                {
                    continue;
                }

                if (skill.GetSkillTypeNormalized() == normalized)
                {
                    return skill;
                }
            }

            return null;
        }

        /// <summary>按技能 ID 查找技能（忽略大小写）。</summary>
        private P2SupportSkillDefinition? FindSkillById(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return null;
            }

            for (int i = 0; i < SupportSkills.Count; i++)
            {
                var skill = SupportSkills[i];
                if (skill == null || string.IsNullOrWhiteSpace(skill.SkillId))
                {
                    continue;
                }

                if (string.Equals(skill.SkillId, skillId, StringComparison.OrdinalIgnoreCase))
                {
                    return skill;
                }
            }

            return null;
        }

        /// <summary>按装备 ID 查找装备（忽略大小写）。</summary>
        private P2SupportEquipmentDefinition? FindEquipmentById(string equipmentId)
        {
            if (string.IsNullOrWhiteSpace(equipmentId))
            {
                return null;
            }

            for (int i = 0; i < SupportEquipments.Count; i++)
            {
                var equipment = SupportEquipments[i];
                if (equipment == null || string.IsNullOrWhiteSpace(equipment.EquipmentId))
                {
                    continue;
                }

                if (string.Equals(equipment.EquipmentId, equipmentId, StringComparison.OrdinalIgnoreCase))
                {
                    return equipment;
                }
            }

            return null;
        }

        /// <summary>解析依赖引用：Controller（父节点）/ 武器搬运（AI_WeaponCarrier）/ 玩家（路径 → 组回退）。</summary>
        private void ResolveDependencies()
        {
            if (_companionController != null && IsInstanceValid(_companionController) && _companionController.IsInsideTree())
            {
                ResolvePlayer();
                return;
            }

            _companionController = GetNodeOrNull<P2CompanionController>(CompanionControllerPath)
                ?? GetNodeOrNull<P2CompanionController>(NormalizeRelativePath(CompanionControllerPath));

            _weaponCarrier ??= GetNodeOrNull<P2WeaponCarrier>(WeaponCarrierPath)
                ?? GetNodeOrNull<P2WeaponCarrier>(NormalizeRelativePath(WeaponCarrierPath));

            _dialogue ??= GetNodeOrNull<P2DialogueController>(DialogueControllerPath)
                ?? GetNodeOrNull<P2DialogueController>(NormalizeRelativePath(DialogueControllerPath));

            ResolvePlayer();
        }

        /// <summary>解析玩家引用（路径 → ../归一化 → "player" 组回退）；玩家变更时重绑护盾拦截。</summary>
        private void ResolvePlayer()
        {
            if (_player != null && IsInstanceValid(_player) && _player.IsInsideTree())
            {
                return;
            }

            var nextPlayer = GetNodeOrNull<global::SamplePlayer>(PlayerPath)
                ?? GetNodeOrNull<global::SamplePlayer>(NormalizeRelativePath(PlayerPath))
                ?? GetTree().GetFirstNodeInGroup("player") as global::SamplePlayer;

            if (!ReferenceEquals(_player, nextPlayer))
            {
                UnbindShieldInterceptor();
                _player = nextPlayer;
                _playerBaseModulate = nextPlayer?.Modulate ?? Colors.White; // 缓存原始颜色（玩家变更时刷新）
                if (_activeShieldPoints > 0)
                {
                    BindShieldInterceptor(); // 换玩家后护盾拦截重绑到新玩家
                }
            }
        }

        /// <summary>相对路径归一化：无 ../ 前缀时补上（统一相对本节点的路径形式）。</summary>
        private static NodePath NormalizeRelativePath(NodePath path)
        {
            string text = path.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("../", System.StringComparison.Ordinal))
            {
                return path;
            }

            return new NodePath($"../{text}");
        }

        /// <summary>秒 → 毫秒（clamp ≥ 0）。</summary>
        private static ulong SecondsToMs(float seconds)
        {
            return (ulong)Mathf.RoundToInt(Mathf.Max(0f, seconds) * 1000f);
        }

        /// <summary>获取技能下一次可用的时间戳（无记录 = 0 = 立即可用）。</summary>
        private ulong GetSupportSkillNextAvailableAtMs(P2SupportSkillDefinition? skill)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.SkillId))
            {
                return 0;
            }

            if (_supportSkillCooldownsMs.TryGetValue(skill.SkillId, out ulong nextAt))
            {
                return nextAt;
            }

            return 0;
        }

        /// <summary>设置技能冷却：优先技能定义 CooldownSeconds，为 0 时用全局 SupportSkillCooldownSeconds。</summary>
        private void SetSupportSkillCooldown(P2SupportSkillDefinition skill, ulong nowMs)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.SkillId))
            {
                return;
            }

            float configured = skill.CooldownSeconds;
            if (configured <= 0f)
            {
                configured = SupportSkillCooldownSeconds;
            }

            _supportSkillCooldownsMs[skill.SkillId] = nowMs + SecondsToMs(configured);
        }
    }
}

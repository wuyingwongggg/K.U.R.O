using System;
using System.Collections.Generic;
using Godot;

namespace Kuros.Companions
{
    /// <summary>P2 逻辑对话事件：每个逻辑状态一个枚举值，通过 P2DialogueController.Speak 触发对应文本。
    /// 固定文本走 dtl label（可建多个变体 label 随机）；带参数的走动态文本注入。</summary>
    public enum P2DialogueEvent
    {
        Ready,              // 初始就位（dtl: ready_N，自动随机变体）
        Combat,             // 战斗提示（dtl: combat，调试热键触发）
        QuietScenePickup,   // 安静场景提示（dtl: quiet_scene_pickup_N，自动随机变体）
        ShieldApplied,      // 护盾施加（dtl: shield_applied_N，自动随机变体）
        Healed,             // 治疗恢复（dtl: healed）
        EquipmentBonus,     // 装备加成恢复（dtl: equipment_bonus）
        ShieldExpired,      // 护盾到期（dtl: shield_expired）
        FallbackLowHp,      // 治疗决策被拒兜底（dtl: fallback_low_hp）
        FallbackEnemyClose, // 护盾决策被拒兜底（dtl: fallback_enemy_close）
        FallbackGeneric,    // 通用兜底（dtl: fallback_generic）
        WeaponFetchStart,   // 出发拾取武器（dtl: fetch_weapon_N，自动随机变体）
        FollowStarted,      // 进入跟随模式（dtl: follow_started_N，自动随机变体；越界跟随触发）
        FreeRoamStarted,    // 恢复自由模式（dtl: free_roam_started_N，自动随机变体；跟随超时触发）
        AiChatter,          // AI 个性台词（dtl: ai_chatter）
    }

    /// <summary>
    /// P2 对话控制器（独立节点）：负责 Dialogic 气泡的全部逻辑——
    /// 连接管理、队列、播放（PushHint/PushHintRandom/PushHintDirect）、
    /// 以及按逻辑状态触发的统一接口（Speak 枚举路由）。
    /// 移动/动画/受击等其余逻辑在 P2CompanionController。
    /// </summary>
    [GlobalClass]
    public partial class P2DialogueController : Node
    {
        /// <summary>P2 的 Dialogic 角色资源路径（.dch）。气泡会跟随 BubbleAnchorPath 节点位置显示。</summary>
        [Export(PropertyHint.File, "*.dch")] public string P2CharacterPath { get; set; } = "res://dialogic/character/P2.dch";
        /// <summary>气泡锚点节点（相对本节点；P2.tscn 的 Anchor_Hint 在 P2 根下，需 ../ 回根）。</summary>
        [Export] public NodePath BubbleAnchorPath { get; set; } = new("../Anchor_Hint");
        /// <summary>提示文本 timeline（.dtl）路径：PushHint 会读取该文件自动发现 label 变体（key_1..key_N 随机）。</summary>
        [Export(PropertyHint.File, "*.dtl")] public string HintTimelinePath { get; set; } = "res://dialogic/timeline/p2_hint.dtl";
        /// <summary>气泡自动关闭时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.5,10,0.1")] public float HintDisplaySeconds { get; set; } = 2.2f;
        /// <summary>气泡队列最大长度（超出丢弃新 hint）。</summary>
        [Export(PropertyHint.Range, "1,20,1")] public int MaxHintQueueSize { get; set; } = 6;

        private GodotObject? _dialogic;            // /root/Dialogic 单例引用
        private Callable _timelineEndedCallable;
        private readonly Queue<string> _hintQueue = new();
        private bool _dialogicBusy;                // 气泡正在播放
        private bool _waitingForHintEnd;           // 等待当前气泡结束

        public override void _Ready()
        {
            // 订阅 Dialogic 的 timeline 结束信号（气泡队列推进）
            _dialogic = GetNodeOrNull("/root/Dialogic");
            if (_dialogic != null)
            {
                _timelineEndedCallable = Callable.From(OnDialogicTimelineEnded);
                _dialogic.Connect("timeline_ended", _timelineEndedCallable);
            }
        }

        public override void _ExitTree()
        {
            // 退订 Dialogic 信号，防止悬挂回调
            if (_dialogic != null && IsInstanceValid(_dialogic)
                && _dialogic.IsConnected("timeline_ended", _timelineEndedCallable))
            {
                _dialogic.Disconnect("timeline_ended", _timelineEndedCallable);
            }
        }

        /// <summary>逻辑对话统一接口：每个逻辑状态通过枚举触发对应文本。
        /// 全部文本统一在 p2_hint.dtl 中维护（label 方式）。调用方只写无后缀 key，
        /// PushHint 会自动发现 dtl 中的 _N 数字变体并随机播放（如 ready → ready_1/ready_2...）。
        /// 参数 args 暂时保留：若个别事件想恢复带参数动态文本（PushHintDirect），
        /// 取消对应 case 里被注释的调用即可。</summary>
        public void Speak(P2DialogueEvent evt, params object[] args)
        {
            switch (evt)
            {
                case P2DialogueEvent.Ready:
                    PushHint("ready");
                    break;
                case P2DialogueEvent.Combat:
                    PushHint("combat");
                    break;
                case P2DialogueEvent.QuietScenePickup:
                    PushHint("quiet_scene_pickup");
                    break;
                case P2DialogueEvent.ShieldApplied:
                    // 带参数动态文本（保留备用）：PushHintDirect($"P2 护盾已施加（{args[0]}）");
                    PushHint("shield_applied");
                    break;
                case P2DialogueEvent.Healed:
                    // 带参数动态文本（保留备用）：PushHintDirect($"P2 恢复 +{args[0]}");
                    PushHint("healed");
                    break;
                case P2DialogueEvent.EquipmentBonus:
                    // 带参数动态文本（保留备用）：PushHintDirect($"装备加成额外恢复 +{args[0]}");
                    PushHint("equipment_bonus");
                    break;
                case P2DialogueEvent.ShieldExpired:
                    PushHint("shield_expired");
                    break;
                case P2DialogueEvent.FallbackLowHp:
                    PushHint("fallback_low_hp");
                    break;
                case P2DialogueEvent.FallbackEnemyClose:
                    PushHint("fallback_enemy_close");
                    break;
                case P2DialogueEvent.FallbackGeneric:
                    PushHint("fallback_generic");
                    break;
                case P2DialogueEvent.WeaponFetchStart:
                    PushHint("fetch_weapon");
                    break;
                case P2DialogueEvent.FollowStarted:
                    PushHint("follow_started");
                    break;
                case P2DialogueEvent.FreeRoamStarted:
                    PushHint("free_roam_started");
                    break;
                case P2DialogueEvent.AiChatter:
                    // 带参数动态文本（保留备用）：PushHintDirect(args[0]?.ToString() ?? string.Empty);
                    PushHint("ai_chatter");
                    break;
            }
        }

        /// <summary>从多条 hintKey 中随机选一条播放（显式变体列表，适用 dtl 之外的自定义 timeline）。
        /// 对于 p2_hint.dtl 中的文本建议直接用无后缀 key（PushHint 会自动发现 _N 变体）。</summary>
        public void PushHintRandom(params string[] hintKeys)
        {
            if (hintKeys == null || hintKeys.Length == 0) return;
            string key = hintKeys[GD.RandRange(0, hintKeys.Length - 1)];
            PushHint(key);
        }

        /// <summary>变体解析：读取 HintTimelinePath，若存在 "key_数字" 后缀的 label（如 shield_applied_1/2/3），
        /// 随机返回其一；否则返回原 key。每次调用重新读文件，dtl 编辑保存后即时生效。</summary>
        private string ResolveVariant(string hintKey)
        {
            string prefix = hintKey + "_";
            var variants = new List<string>();

            using var file = FileAccess.Open(HintTimelinePath, FileAccess.ModeFlags.Read);
            if (file == null)
                return hintKey;

            while (!file.EofReached())
            {
                string line = file.GetLine().Trim();
                if (!line.StartsWith("label ", StringComparison.Ordinal))
                    continue;

                string name = line["label ".Length..].Trim();
                if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                bool allDigits = true;
                for (int i = prefix.Length; i < name.Length; i++)
                {
                    if (!char.IsDigit(name[i]))
                    {
                        allDigits = false;
                        break;
                    }
                }

                if (allDigits)
                    variants.Add(name);
            }

            if (variants.Count == 0)
                return hintKey;

            return variants[GD.RandRange(0, variants.Count - 1)];
        }

        /// <summary>播放 p2_hint timeline 中对应 label 的对话气泡。
        /// 调用方始终传无后缀 key（如 "ready"、"shield_applied"）；若 dtl 中存在
        /// key_1..key_N（数字后缀）变体，则自动随机播其一——在 dtl 中持续添加 _N
        /// 变体即可扩展台词，无需修改代码。文本在 dialogic/timeline/p2_hint.dtl 维护。</summary>
        public void PushHint(string hintKey)
        {
            if (string.IsNullOrWhiteSpace(hintKey))
                return;

            hintKey = ResolveVariant(hintKey);

            // 过场播放期间禁止触发 hint
            var cutsceneManager = GetTree().GetFirstNodeInGroup("cutscene_manager");
            if (cutsceneManager is Kuros.Systems.Cutscene.CutsceneManager cm && cm.IsPlaying)
                return;

            _dialogic ??= GetNodeOrNull("/root/Dialogic");
            if (_dialogic == null || !IsInstanceValid(_dialogic))
                return;

            // 如果 Dialogic 正在播放非本 hint 的 Timeline（例如剧情对话），则放弃
            var currentTimeline = _dialogic.Get("current_timeline");
            if (currentTimeline.VariantType != Variant.Type.Nil && !_waitingForHintEnd)
                return;

            // 气泡播放中：入队等待（超队列上限丢弃）
            if (_dialogicBusy)
            {
                if (_hintQueue.Count < Mathf.Max(1, MaxHintQueueSize))
                    _hintQueue.Enqueue(hintKey);
                return;
            }

            StartDialogicHint(hintKey);
        }

        /// <summary>显示运行时动态生成的文本（如 AI 个性台词），文本不在 DTL 中预定义。
        /// 通过 Dialogic 变量 "p2_hint_text" 注入后播放 p2_hint.dtl 的 label:direct。
        /// 需在 Dialogic 编辑器 Variables 中预先定义 "p2_hint_text" 变量（默认值留空即可）。</summary>
        public void PushHintDirect(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return;

            _dialogic ??= GetNodeOrNull("/root/Dialogic");
            if (_dialogic == null || !IsInstanceValid(_dialogic))
                return;

            // 在启动 timeline 前注入变量，label:direct 中的 {p2_hint_text} 会读取该值
            _dialogic.Get("VAR").AsGodotObject()?.Call("set_variable", "p2_hint_text", rawText);
            PushHint("direct");
        }

        /// <summary>取消当前气泡并清空队列（P2 被隐藏/过场时由 Controller 调用）。</summary>
        public void CancelActiveHint()
        {
            _hintQueue.Clear();
            if (!_waitingForHintEnd) return;
            _waitingForHintEnd = false;
            _dialogicBusy = false;
            _dialogic ??= GetNodeOrNull("/root/Dialogic");
            if (_dialogic != null && IsInstanceValid(_dialogic) && _dialogic.HasMethod("end_timeline"))
                _dialogic.Call("end_timeline");
        }

        /// <summary>启动一条 Dialogic 气泡（标记忙碌 → 定位角色 → 到时自动结束）。</summary>
        private void StartDialogicHint(string hintKey)
        {
            if (_dialogic == null || !IsInstanceValid(_dialogic))
                return;

            _dialogicBusy = true;
            _waitingForHintEnd = true;

            // 若还没有激活的 Layout，先加载 textbubble_A 样式
            var styles = _dialogic.Get("Styles").AsGodotObject();
            if (styles != null && !(bool)styles.Call("has_active_layout_node"))
                styles.Call("load_style", "textbubble_A");

            // 以 label 为入口启动 p2_hint timeline（文本全部定义在 dtl 文件中）
            var layoutNode = _dialogic.Call("start", "p2_hint", hintKey).AsGodotObject() as Node;

            // 将气泡定位到 BubbleAnchorPath 指定节点
            if (!string.IsNullOrEmpty(P2CharacterPath) && !BubbleAnchorPath.IsEmpty && layoutNode != null)
            {
                var anchor = GetNodeOrNull<Node2D>(BubbleAnchorPath);
                if (anchor != null)
                    layoutNode.CallDeferred("register_character", P2CharacterPath, anchor);
            }

            // 到时后自动结束（若玩家未手动推进）
            float delay = Mathf.Max(0.5f, HintDisplaySeconds);
            GetTree().CreateTimer(delay).Timeout += () =>
            {
                if (_waitingForHintEnd && _dialogic != null && IsInstanceValid(_dialogic))
                    _dialogic.Call("end_timeline");
            };
        }

        /// <summary>Dialogic timeline 结束回调：解除忙碌并推进队列中的下一条气泡。</summary>
        private void OnDialogicTimelineEnded()
        {
            if (!_waitingForHintEnd)
                return;

            _waitingForHintEnd = false;
            _dialogicBusy = false;

            if (_hintQueue.Count > 0)
                StartDialogicHint(_hintQueue.Dequeue());
        }
    }
}

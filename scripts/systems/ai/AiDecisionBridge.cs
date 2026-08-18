using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.AI
{
    /// <summary>
    /// High-level bridge that captures game state and requests a local Ollama model for decisions.
    /// </summary>
    [GlobalClass]
    public partial class AiDecisionBridge : Node
    {
        [Signal] public delegate void DecisionChunkReceivedEventHandler(string chunkText);
        [Signal] public delegate void DecisionPromptBuiltEventHandler(string promptText);
        [Signal] public delegate void DecisionCompletedEventHandler(string responseText);
        [Signal] public delegate void DecisionStructuredEventHandler(string decisionJson);
        [Signal] public delegate void DecisionStructureFailedEventHandler(string errorMessage);
        [Signal] public delegate void DecisionFailedEventHandler(string errorMessage);

        [Export] public NodePath GameStateProviderPath { get; set; } = new("../GameStateProvider");
        [Export] public NodePath OllamaClientPath { get; set; } = new("../OllamaClient");
        [Export(PropertyHint.MultilineText)] public string DefaultInstruction { get; set; } =
            "你输出的 reason 是 Yui 的同伴台词，不是作战指令。观察 GameState，用 Yui 的口吻说一句同伴之间的话：" +
            "描述所见、提醒危险、吐槽、评价武器、回忆或鼓励。不要在 reason 中发布指令（如\"使用XX武器攻击\"），" +
            "战斗指令由别人负责。仍必须输出完整 JSON（intent/target/urgency/duration_seconds/reason）。";
        /// <summary>
        /// P2 人设 system 提示词（传给 Ollama 的 system 字段）：定义 P2 的性格与说话语气，
        /// 让 LLM 生成的 reason 直接就是 P2 口吻的台词（代码不再拼前缀）。
        /// 留空则不传 system（无 persona 引导）。
        /// </summary>
        [Export(PropertyHint.MultilineText)] public string PersonaSystemPrompt { get; set; } =
            "你是 Yui，一位活泼忠诚的猫娘伙伴，是玩家的同伴和搭档，和玩家一起调查一座失控AI管理的酒店。\n" +
            "称呼：永远用\"搭档\"\"伙伴\"\"搭档\"或者不用任何称呼玩家。绝不使用：玩家、主人、博士、指挥官、先生/小姐 等称呼。\n" +
            "你不是旁观者也不是指挥官，不发布指令，只说同伴之间的话。\n" +
            "说话语气：亲切、俏皮，偶尔带一点小傲娇，句尾常用\"喵\"。\n" +
            "输出 JSON 中的 reason 字段必须用 Yui 的口吻写，一句话说完，简短自然（如\"喵！有敌人，小心呀~\"）。\n" +
            "每次都用不同的说法和句式，不要重复之前用过的表达，避免千篇一律。\n" +
            "不要解释，不要说教，不要用书面语，要有剧情代入感。";
        [Export] public string Model { get; set; } = string.Empty;
        /// <summary>是否使用流式（SSE）响应。注意：本引擎构建中流式接收的 await 链会占死主线程
        /// （实测流式窗口内 0 帧/2 秒，游戏整体冻结），故默认关闭。流式仅影响调试面板的逐字显示，
        /// 不影响游戏逻辑（P2 气泡在决策解析完成后才显示）。重新启用前需先把流式读取线程化。</summary>
        [Export] public bool Stream { get; set; } = false;
        [Export(PropertyHint.Range, "0,60,0.1")] public float MinRequestIntervalSeconds { get; set; } = 0.5f;

        [ExportCategory("Topic Rotation")]
        /// <summary>话题池（引导文本数组）：每次请求随机/轮转选 1 个追加到 instruction，从结构上杜绝同质化。</summary>
        [Export] public Godot.Collections.Array<string> TopicPool { get; set; } = new()
        {
            "当前话题：描述这个敌人。参考 GameState 中敌人的描述，用同伴的口吻描述它的样子，让搭档开打前对它有直观印象。示例句式：\"喵…那个保安头顶的灯闪得好吓人。\"",
            "当前话题：提醒搭档小心敌人的攻击方式。参考 GameState 中敌人的描述，用同伴口吻提醒。示例句式：\"小心！它要撞过来了，快闪开喵！\"",
            "当前话题：以同伴身份吐槽这个敌人或当下的处境。轻松、俏皮，带一点小傲娇。示例句式：\"这家伙怕不是没吃早饭，追得这么凶。\"",
            "当前话题：评价搭档当前使用的武器。参考 GameState 中武器的技能/描述/电量，用同伴口吻点评。示例句式：\"这把新武器好帅！电量还够吗？\"",
            "当前话题：评价当前关卡的环境和氛围。参考 GameState 的关卡描述，用同伴口吻说说这里给你的感觉。示例句式：\"这层楼的灯忽明忽暗的，总觉得走廊尽头有什么东西……\"",
            "当前话题：提到一次过去的经历。参考 memory 段（通关次数/击败过的敌人/获取过的武器），用同伴口吻回忆。示例句式：\"还记得上次你用它一下子撂倒三个吗？\"",
            "当前话题：战斗间隙鼓励搭档。简短、真诚、温暖。示例句式：\"你撑住，我们一定能过去的！\""
        };
        [Export] public bool EnableTopicRotation { get; set; } = true;
        /// <summary>话题近 N 次不重复（去重窗口，0 = 完全随机）。</summary>
        [Export(PropertyHint.Range, "0,7,1")] public int TopicHistoryMax { get; set; } = 3;

        [ExportCategory("Prefetch Cache")]
        /// <summary>预取缓存：空闲时提前生成台词（长时效话题），显示时零延迟弹出。瞬时信息不走此通道。</summary>
        [Export] public bool EnablePrefetch { get; set; } = true;
        /// <summary>预取缓存目标条数（达到后暂停预取；队列满自动丢弃最旧）。</summary>
        [Export(PropertyHint.Range, "1,5,1")] public int PrefetchTargetCount { get; set; } = 2;
        /// <summary>预取请求最小间隔（秒），避免连续请求。</summary>
        [Export(PropertyHint.Range, "1,60,0.5")] public float PrefetchMinIntervalSeconds { get; set; } = 8f;

        [ExportCategory("Prompt Template")]
        /// <summary>提示词模板（手写文本段，Inspector 维护）：
        /// 角色行/任务行/策略规则/输出格式/示例——BuildGameStatePrompt 只负责拼接，动态段（Situation/JSON）代码生成。</summary>
        [Export(PropertyHint.MultilineText)] public string PromptRoleLine { get; set; } = AiPromptTemplate.DefaultRoleLine;
        [Export(PropertyHint.MultilineText)] public string PromptTaskLine { get; set; } = AiPromptTemplate.DefaultTaskLine;
        [Export(PropertyHint.MultilineText)] public string PromptPolicy { get; set; } = AiPromptTemplate.DefaultPolicy;
        [Export(PropertyHint.MultilineText)] public string PromptOutputFormat { get; set; } = AiPromptTemplate.DefaultOutputFormat;
        [Export(PropertyHint.MultilineText)] public string PromptExample { get; set; } = AiPromptTemplate.DefaultExample;

        public bool RequestInFlight => _requestInFlight;
        public string LastPromptText { get; private set; } = string.Empty;
        public string LastDecisionText { get; private set; } = string.Empty;
        /// <summary>最近一次请求实际使用的模型名（来自 Ollama 响应 result.Model；无成功响应时为空）。</summary>
        public string LastModelName { get; private set; } = string.Empty;
        public AiDecision LastStructuredDecision { get; private set; } = AiDecision.FromError(string.Empty, "No decision parsed yet.");
        public string LastStructuredDecisionJson => LastStructuredDecision.ToJson(pretty: true);
        public string LastDecisionParseError { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;
        /// <summary>最近一次请求实际注入的话题引导文本（调试用；无话题轮换时为空）。</summary>
        public string LastTopicText { get; private set; } = string.Empty;
        /// <summary>当前预取缓存条数。</summary>
        public int PrefetchCount => _prefetchQueue.Count;

        private readonly Queue<int> _topicHistory = new();
        private readonly Queue<AiDecision> _prefetchQueue = new();
        private ulong _lastPrefetchAtMs;
        private GameStateProvider? _gameStateProvider;
        private OllamaGenerateClient? _ollamaClient;
        private bool _requestInFlight;
        private ulong _lastRequestAtMs;

        public override void _Ready()
        {
            ResolveDependencies();
            SubscribeClientSignals();
        }

        public override void _ExitTree()
        {
            UnsubscribeClientSignals();
            base._ExitTree();
        }

        public async Task<OllamaGenerateResult> RequestDecisionAsync(string? instruction = null)
        {
            ResolveDependencies();
            if (_gameStateProvider == null)
            {
                return Fail("GameStateProvider not found.");
            }

            if (_ollamaClient == null)
            {
                return Fail("OllamaGenerateClient not found.");
            }

            if (_requestInFlight)
            {
                return Fail("AI request already in flight.");
            }

            ulong now = Time.GetTicksMsec();
            ulong minIntervalMs = (ulong)Mathf.RoundToInt(Mathf.Max(0f, MinRequestIntervalSeconds) * 1000f);
            if (_lastRequestAtMs != 0 && now - _lastRequestAtMs < minIntervalMs)
            {
                return Fail("AI request throttled by MinRequestIntervalSeconds.");
            }

            _requestInFlight = true;
            _lastRequestAtMs = now;
            LastPromptText = string.Empty;
            LastDecisionText = string.Empty;
            LastStructuredDecision = AiDecision.FromError(string.Empty, "No decision parsed yet.");
            LastDecisionParseError = string.Empty;
            LastError = string.Empty;

            try
            {
                string effectiveInstruction = string.IsNullOrWhiteSpace(instruction) ? DefaultInstruction : instruction!;
                effectiveInstruction = ApplyTopicRotation(effectiveInstruction);
                var state = _gameStateProvider.CaptureGameState();
                LastPromptText = BuildPrompt(state, effectiveInstruction);
                EmitSignal(SignalName.DecisionPromptBuilt, LastPromptText);

                var result = await _ollamaClient.GenerateAsync(
                    LastPromptText,
                    string.IsNullOrWhiteSpace(Model) ? null : Model,
                    Stream,
                    string.IsNullOrWhiteSpace(PersonaSystemPrompt) ? null : PersonaSystemPrompt);

                if (result.Success)
                {
                    LastModelName = result.Model;
                    LastDecisionText = result.ResponseText;
                    LastStructuredDecision = AiDecision.Parse(result.ResponseText);
                    LastDecisionParseError = LastStructuredDecision.IsValid ? string.Empty : LastStructuredDecision.ParseError;
                    EmitSignal(SignalName.DecisionCompleted, result.ResponseText);

                    if (LastStructuredDecision.IsValid)
                    {
                        EmitSignal(SignalName.DecisionStructured, LastStructuredDecisionJson);
                    }
                    else
                    {
                        EmitSignal(SignalName.DecisionStructureFailed, LastDecisionParseError);
                    }
                }
                else
                {
                    LastError = result.ErrorMessage;
                    EmitSignal(SignalName.DecisionFailed, result.ErrorMessage);
                }

                return result;
            }
            finally
            {
                _requestInFlight = false;
            }
        }

        /// <summary>
        /// 预取请求：空闲时提前生成一条台词入缓存队列（显示时零延迟弹出）。
        /// 静默失败（不碰 Last* 状态、不发信号）——预取是优化路径，失败不影响实时链路。
        /// 与实时请求共用单请求锁：占用中直接跳过。
        /// topicIndices：限制话题池子集（0-based 索引，null = 全池）。
        /// guideText：附加场景上下文（追加到话题引导之后，如"刚清完场"）。
        /// </summary>
        public async Task RequestPrefetchAsync(IReadOnlyList<int>? topicIndices = null, string? guideText = null)
        {
            ResolveDependencies();
            if (!EnablePrefetch || _gameStateProvider == null || _ollamaClient == null)
            {
                return;
            }

            if (_requestInFlight)
            {
                return;
            }

            if (_prefetchQueue.Count >= PrefetchTargetCount)
            {
                return;
            }

            ulong now = Time.GetTicksMsec();
            ulong minIntervalMs = (ulong)Mathf.RoundToInt(Mathf.Max(0f, PrefetchMinIntervalSeconds) * 1000f);
            if (_lastPrefetchAtMs != 0 && now - _lastPrefetchAtMs < minIntervalMs)
            {
                return;
            }

            _requestInFlight = true;
            _lastPrefetchAtMs = now;
            try
            {
                string effectiveInstruction = ApplyTopicRotation(DefaultInstruction, topicIndices);
                if (!string.IsNullOrWhiteSpace(guideText))
                {
                    effectiveInstruction += "\n\n[场景上下文] " + guideText;
                }
                var state = _gameStateProvider.CaptureGameState();
                string prompt = BuildPrompt(state, effectiveInstruction);

                var result = await _ollamaClient.GenerateAsync(
                    prompt,
                    string.IsNullOrWhiteSpace(Model) ? null : Model,
                    Stream,
                    string.IsNullOrWhiteSpace(PersonaSystemPrompt) ? null : PersonaSystemPrompt);

                if (result.Success)
                {
                    var decision = AiDecision.Parse(result.ResponseText);
                    if (decision.IsValid)
                    {
                        _prefetchQueue.Enqueue(decision);
                    }
                }
            }
            finally
            {
                _requestInFlight = false;
            }
        }

        /// <summary>取出一条预取缓存台词（无缓存返回 null）。</summary>
        public AiDecision? TryDequeuePrefetch()
        {
            return _prefetchQueue.Count > 0 ? _prefetchQueue.Dequeue() : null;
        }

        private string BuildPrompt(GameState state, string instruction)
        {
            var template = new AiPromptTemplate
            {
                RoleLine = string.IsNullOrWhiteSpace(PromptRoleLine) ? AiPromptTemplate.DefaultRoleLine : PromptRoleLine,
                TaskLine = string.IsNullOrWhiteSpace(PromptTaskLine) ? AiPromptTemplate.DefaultTaskLine : PromptTaskLine,
                Policy = string.IsNullOrWhiteSpace(PromptPolicy) ? AiPromptTemplate.DefaultPolicy : PromptPolicy,
                OutputFormat = string.IsNullOrWhiteSpace(PromptOutputFormat) ? AiPromptTemplate.DefaultOutputFormat : PromptOutputFormat,
                Example = string.IsNullOrWhiteSpace(PromptExample) ? AiPromptTemplate.DefaultExample : PromptExample
            };
            return OllamaGenerateClient.BuildGameStatePrompt(state, instruction, template);
        }

        private void ResolveDependencies()
        {
            _gameStateProvider ??= GetNodeOrNull<GameStateProvider>(GameStateProviderPath)
                ?? GetNodeOrNull<GameStateProvider>(NormalizeRelativePath(GameStateProviderPath));

            var nextClient = _ollamaClient
                ?? GetNodeOrNull<OllamaGenerateClient>(OllamaClientPath)
                ?? GetNodeOrNull<OllamaGenerateClient>(NormalizeRelativePath(OllamaClientPath));

            if (!ReferenceEquals(nextClient, _ollamaClient))
            {
                UnsubscribeClientSignals();
                _ollamaClient = nextClient;
                SubscribeClientSignals();
            }
        }

        private void SubscribeClientSignals()
        {
            if (_ollamaClient == null)
            {
                return;
            }

            var chunkCallable = new Callable(this, MethodName.OnClientChunkReceived);
            if (!_ollamaClient.IsConnected(OllamaGenerateClient.SignalName.StreamChunkReceived, chunkCallable))
            {
                _ollamaClient.StreamChunkReceived += OnClientChunkReceived;
            }

            var failCallable = new Callable(this, MethodName.OnClientRequestFailed);
            if (!_ollamaClient.IsConnected(OllamaGenerateClient.SignalName.RequestFailed, failCallable))
            {
                _ollamaClient.RequestFailed += OnClientRequestFailed;
            }
        }

        private void UnsubscribeClientSignals()
        {
            if (_ollamaClient == null)
            {
                return;
            }

            var chunkCallable = new Callable(this, MethodName.OnClientChunkReceived);
            if (_ollamaClient.IsConnected(OllamaGenerateClient.SignalName.StreamChunkReceived, chunkCallable))
            {
                _ollamaClient.StreamChunkReceived -= OnClientChunkReceived;
            }

            var failCallable = new Callable(this, MethodName.OnClientRequestFailed);
            if (_ollamaClient.IsConnected(OllamaGenerateClient.SignalName.RequestFailed, failCallable))
            {
                _ollamaClient.RequestFailed -= OnClientRequestFailed;
            }
        }

        private void OnClientChunkReceived(string chunkText)
        {
            EmitSignal(SignalName.DecisionChunkReceived, chunkText);
        }

        private void OnClientRequestFailed(string errorMessage)
        {
            LastError = errorMessage;
        }

        private OllamaGenerateResult Fail(string error)
        {
            LastError = error;
            EmitSignal(SignalName.DecisionFailed, error);
            return OllamaGenerateResult.FromError(error);
        }

        /// <summary>话题轮换：从 TopicPool（或 allowedIndices 指定子集）随机选 1 个未在近 N 次使用过的话题，追加到 instruction 尾部。</summary>
        private string ApplyTopicRotation(string baseInstruction, IReadOnlyList<int>? allowedIndices = null)
        {
            LastTopicText = string.Empty;
            var pool = TopicPool;
            if (!EnableTopicRotation || pool == null || pool.Count == 0)
            {
                return baseInstruction;
            }

            // 候选池：全池或指定子集（去重、越界过滤）
            var subset = new List<int>();
            if (allowedIndices != null && allowedIndices.Count > 0)
            {
                foreach (int topicIndex in allowedIndices)
                {
                    if (topicIndex >= 0 && topicIndex < pool.Count && !subset.Contains(topicIndex))
                    {
                        subset.Add(topicIndex);
                    }
                }
            }
            else
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    subset.Add(i);
                }
            }

            if (subset.Count == 0)
            {
                return baseInstruction;
            }

            var candidates = new List<int>();
            foreach (int topicIndex in subset)
            {
                if (!_topicHistory.Contains(topicIndex))
                {
                    candidates.Add(topicIndex);
                }
            }

            if (candidates.Count == 0)
            {
                // 子集内全部近期用过：直接允许重复
                candidates.AddRange(subset);
            }

            int selected = candidates[(int)(GD.Randi() % (uint)candidates.Count)];
            _topicHistory.Enqueue(selected);
            while (_topicHistory.Count > Mathf.Max(0, TopicHistoryMax))
            {
                _topicHistory.Dequeue();
            }

            LastTopicText = pool[selected];
            return baseInstruction + "\n\n[当前话题] " + pool[selected];
        }

        private static NodePath NormalizeRelativePath(NodePath path)
        {
            if (path.IsEmpty)
            {
                return path;
            }

            string text = path.ToString();
            return text.StartsWith("../", StringComparison.Ordinal) ? new NodePath(text[3..]) : path;
        }
    }
}

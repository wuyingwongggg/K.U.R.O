using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.AI
{
    /// <summary>
    /// Ollama /api/generate 客户端：调用本地 Ollama 模型做推理。
    /// 同时支持流式（NDJSON 逐行）与非流式（单 JSON）两种响应。
    /// 纯通信层，不包含任何游戏逻辑；供 AiDecisionBridge 编排使用。
    /// </summary>
    [GlobalClass]
    public partial class OllamaGenerateClient : Node
    {
        /// <summary>流式响应时，每收到一个文本块触发一次（chunkText 为增量文本）。</summary>
        [Signal] public delegate void StreamChunkReceivedEventHandler(string chunkText);
        /// <summary>响应完成时触发（fullText 为完整文本）。</summary>
        [Signal] public delegate void StreamCompletedEventHandler(string fullText);
        /// <summary>请求失败时触发（errorMessage 为错误描述）。</summary>
        [Signal] public delegate void RequestFailedEventHandler(string errorMessage);

        /// <summary>Ollama 服务地址（本地默认 11434 端口 /api/generate）。</summary>
        [Export] public string Endpoint { get; set; } = "http://localhost:11434/api/generate";
        /// <summary>默认模型名。</summary>
        [Export] public string DefaultModel { get; set; } = "llama3";
        /// <summary>默认是否流式输出。</summary>
        [Export] public bool DefaultStream { get; set; } = false;
        /// <summary>请求超时（秒）。</summary>
        [Export(PropertyHint.Range, "1,600,1")] public int TimeoutSeconds { get; set; } = 120;
        /// <summary>最大生成 token 数。</summary>
        [Export(PropertyHint.Range, "16,4096,1")] public int MaxPredictTokens { get; set; } = 512;
        /// <summary>采样温度（越低越确定性）。</summary>
        [Export(PropertyHint.Range, "0,2,0.01")] public float Temperature { get; set; } = 0.2f;
        /// <summary>请求关闭思考模式（think=false）。Qwen 系模型可能只输出 thinking 而 response 为空，
        /// 关闭思考可提高直接返回最终答案的概率。</summary>
        [Export] public bool DisableThinking { get; set; } = true;

        /// <summary>共享 HTTP 客户端（复用连接，避免每次请求重建）。</summary>
        private static readonly System.Net.Http.HttpClient SharedHttpClient = new();

        /// <summary>发起一次生成请求。参数缺省时回退到导出默认值。</summary>
        /// <param name="prompt">喂给模型的提示文本。</param>
        /// <param name="model">模型名（空 = DefaultModel）。</param>
        /// <param name="stream">是否流式（null = DefaultStream）。</param>
        /// <param name="system">可选的 system 提示（如角色设定）。</param>
        public async Task<OllamaGenerateResult> GenerateAsync(
            string prompt,
            string? model = null,
            bool? stream = null,
            string? system = null)
        {
            string requestModel = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
            bool requestStream = stream ?? DefaultStream;

            // 组装 Ollama 请求体
            var payload = new Godot.Collections.Dictionary<string, Variant>
            {
                ["model"] = requestModel,
                ["prompt"] = prompt,
                ["stream"] = requestStream
            };

            // 采样参数
            payload["options"] = new Godot.Collections.Dictionary<string, Variant>
            {
                ["num_predict"] = Mathf.Max(16, MaxPredictTokens),
                ["temperature"] = Mathf.Clamp(Temperature, 0f, 2f)
            };

            if (!string.IsNullOrWhiteSpace(system))
            {
                payload["system"] = system;
            }

            if (DisableThinking)
            {
                // Qwen 系模型可能只输出 thinking 而 response 为空。
                // 请求非思考模式，提高直接输出最终答案文本的概率。
                payload["think"] = false;
            }

            return await SendGenerateRequestAsync(payload, requestStream);
        }

        /// <summary>便捷方法：直接从 GameStateProvider 采集状态并生成 prompt 后请求。</summary>
        public async Task<OllamaGenerateResult> GenerateFromGameStateAsync(
            GameStateProvider provider,
            string instruction,
            string? model = null,
            bool? stream = null)
        {
            if (provider == null)
            {
                return OllamaGenerateResult.FromError("GameStateProvider is null.");
            }

            string prompt = BuildGameStatePrompt(provider.CaptureGameState(), instruction);
            return await GenerateAsync(prompt, model, stream);
        }

        /// <summary>把游戏状态快照拼装成模型提示词：说明游戏类型、决策策略、JSON 状态与输出格式要求。</summary>
        public static string BuildGameStatePrompt(GameState state, string instruction)
        {
            string safeInstruction = string.IsNullOrWhiteSpace(instruction)
                ? "Decide the next action for a fast-paced action game and prefer proactive combat behavior."
                : instruction.Trim();

            return string.Join("\n", new[]
            {
                "You are an in-game decision model.",
                "This is a fast-paced action game, not a cautious turn-based tactics game.",
                "Given the following current game state, return one executable decision.",
                string.Empty,
                "Instruction:",
                safeInstruction,
                string.Empty,
                "Decision policy:",
                "- When enemies are present, default to proactive combat behavior.",
                "- Prefer attack, use_skill, or switch_weapon over retreat.",
                "- Do not choose retreat just because enemies are nearby.",
                "- Choose retreat only if the player is in clear lethal danger, such as very low hp or being overwhelmed while under attack.",
                "- Prefer use_skill when pressure is high and a stronger immediate action makes sense.",
                "- Prefer switch_weapon only when it clearly improves the current combat situation.",
                "- Do not choose loot while enemies are actively threatening the player.",
                "- Reposition should stay combat-focused and short-term, not passive avoidance.",
                "- If alive_enemy_count > 0 and player hp is not critically low, usually return attack or use_skill.",
                "- Avoid repeating the same intent too many times in a row when situation is unchanged.",
                "- Under close-range pressure, alternate between attack and reposition to kite instead of face-tanking forever.",
                "- If hp is low and under_attack is true, prefer retreat or reposition over direct attack.",
                string.Empty,
                "GameState(JSON):",
                state.ToAiInputJson(pretty: false),
                string.Empty,
                "Output format:",
                "- Return strict JSON object only",
                "- Do not wrap JSON in markdown code fences",
                "- Required keys: intent, target, urgency, duration_seconds, reason",
                "- intent must be a short snake_case action such as attack, retreat, reposition, loot, switch_weapon, use_skill",
                "- target must be a short target label such as nearest_enemy, lowest_hp_enemy, safe_position, nearby_loot, none",
                "- urgency must be one of: low, medium, high, critical",
                "- duration_seconds must be a non-negative number",
                "- reason must be one short sentence",
                "- Keep the reason grounded in immediate action-game combat logic",
                string.Empty,
                "Example:",
                "{\"intent\":\"attack\",\"target\":\"nearest_enemy\",\"urgency\":\"high\",\"duration_seconds\":1.2,\"reason\":\"A nearby enemy is within range and the player is not under immediate lethal threat.\"}"
            });
        }

        /// <summary>发送 HTTP POST 请求并按流式/非流式分支解析响应。超时与异常统一转成失败结果。</summary>
        private async Task<OllamaGenerateResult> SendGenerateRequestAsync(
            Godot.Collections.Dictionary<string, Variant> payload,
            bool streaming)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = new StringContent(Json.Stringify(payload), Encoding.UTF8, "application/json")
                };

                // 超时控制（读到响应头即开始，流式逐行消费）
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Mathf.Max(1, TimeoutSeconds)));

                using HttpResponseMessage response = await SharedHttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    string errBody = await response.Content.ReadAsStringAsync();
                    string err = $"Ollama request failed ({(int)response.StatusCode}): {errBody}";
                    EmitSignal(SignalName.RequestFailed, err);
                    return OllamaGenerateResult.FromError(err);
                }

                if (streaming)
                {
                    return await ParseStreamingResponseAsync(response, cts.Token);
                }

                return await ParseSingleJsonResponseAsync(response, cts.Token);
            }
            catch (OperationCanceledException)
            {
                string err = $"Ollama request timeout after {Mathf.Max(1, TimeoutSeconds)} second(s).";
                EmitSignal(SignalName.RequestFailed, err);
                return OllamaGenerateResult.FromError(err);
            }
            catch (Exception ex)
            {
                string err = $"Ollama request exception: {ex.Message}";
                EmitSignal(SignalName.RequestFailed, err);
                return OllamaGenerateResult.FromError(err);
            }
        }

        /// <summary>解析非流式响应：整个响应体是一个 JSON 对象，提取 response/thinking 及统计字段。</summary>
        private async Task<OllamaGenerateResult> ParseSingleJsonResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            Variant parsed = Json.ParseString(body);
            if (parsed.VariantType != Variant.Type.Dictionary)
            {
                string err = "Ollama non-stream response is not a JSON object.";
                EmitSignal(SignalName.RequestFailed, err);
                return OllamaGenerateResult.FromError(err);
            }

            var dict = parsed.AsGodotDictionary();
            string text = GetString(dict, "response");
            string thinking = GetString(dict, "thinking");
            var result = new OllamaGenerateResult
            {
                Success = true,
                Model = GetString(dict, "model"),
                CreatedAt = GetString(dict, "created_at"),
                ResponseText = text,
                ThinkingText = thinking,
                Done = GetBool(dict, "done"),
                DoneReason = GetString(dict, "done_reason"),
                Context = GetIntArray(dict, "context"),
                RawFinalObject = dict,
                TotalDuration = GetLong(dict, "total_duration"),
                LoadDuration = GetLong(dict, "load_duration"),
                PromptEvalCount = GetInt(dict, "prompt_eval_count"),
                PromptEvalDuration = GetLong(dict, "prompt_eval_duration"),
                EvalCount = GetInt(dict, "eval_count"),
                EvalDuration = GetLong(dict, "eval_duration")
            };

            ApplyThinkingFallbackIfNeeded(result);

            EmitSignal(SignalName.StreamCompleted, result.ResponseText);
            return result;
        }

        /// <summary>解析流式响应：Ollama 流式输出为 NDJSON（每行一个 JSON 对象），逐行累积 response/thinking 文本。</summary>
        private async Task<OllamaGenerateResult> ParseStreamingResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var result = new OllamaGenerateResult { Success = true };
            var sb = new StringBuilder(1024);
            var thinkingSb = new StringBuilder(1024);

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                Variant parsed = Json.ParseString(line);
                if (parsed.VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                var chunkObj = parsed.AsGodotDictionary();
                result.RawChunks.Add(chunkObj);

                // 文本增量：追加并广播 chunk 信号（供 UI 实时显示）
                string chunkText = GetString(chunkObj, "response");
                if (!string.IsNullOrEmpty(chunkText))
                {
                    sb.Append(chunkText);
                    EmitSignal(SignalName.StreamChunkReceived, chunkText);
                }

                // 思考增量（仅累积，不广播）
                string thinkingChunk = GetString(chunkObj, "thinking");
                if (!string.IsNullOrEmpty(thinkingChunk))
                {
                    thinkingSb.Append(thinkingChunk);
                }

                // 元数据只在首个 chunk 填充
                result.Model = string.IsNullOrEmpty(result.Model) ? GetString(chunkObj, "model") : result.Model;
                result.CreatedAt = string.IsNullOrEmpty(result.CreatedAt) ? GetString(chunkObj, "created_at") : result.CreatedAt;

                // 最后一个 chunk 带 done=true，提取统计字段后结束
                bool done = GetBool(chunkObj, "done");
                if (done)
                {
                    result.Done = true;
                    result.DoneReason = GetString(chunkObj, "done_reason");
                    result.Context = GetIntArray(chunkObj, "context");
                    result.RawFinalObject = chunkObj;
                    result.TotalDuration = GetLong(chunkObj, "total_duration");
                    result.LoadDuration = GetLong(chunkObj, "load_duration");
                    result.PromptEvalCount = GetInt(chunkObj, "prompt_eval_count");
                    result.PromptEvalDuration = GetLong(chunkObj, "prompt_eval_duration");
                    result.EvalCount = GetInt(chunkObj, "eval_count");
                    result.EvalDuration = GetLong(chunkObj, "eval_duration");
                    break;
                }
            }

            result.ResponseText = sb.ToString();
            result.ThinkingText = thinkingSb.ToString();
            ApplyThinkingFallbackIfNeeded(result);
            EmitSignal(SignalName.StreamCompleted, result.ResponseText);
            return result;
        }

        /// <summary>回退逻辑：若 response 为空但 thinking 非空（如 Qwen 系），用 thinking 充当最终答案。</summary>
        private static void ApplyThinkingFallbackIfNeeded(OllamaGenerateResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.ResponseText))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(result.ThinkingText))
            {
                return;
            }

            result.ResponseText = result.ThinkingText;
            result.UsedThinkingFallback = true;
        }

        /// <summary>从响应字典安全取字符串（缺键或类型不符返回空串）。</summary>
        private static string GetString(Godot.Collections.Dictionary dict, string key)
        {
            if (!dict.TryGetValue(key, out Variant value)) return string.Empty;
            return value.VariantType == Variant.Type.String ? value.AsString() : value.ToString();
        }

        /// <summary>从响应字典安全取布尔值。</summary>
        private static bool GetBool(Godot.Collections.Dictionary dict, string key)
        {
            if (!dict.TryGetValue(key, out Variant value)) return false;
            return value.VariantType == Variant.Type.Bool && value.AsBool();
        }

        /// <summary>从响应字典安全取 int（兼容 Int/Float 两种 Variant 类型）。</summary>
        private static int GetInt(Godot.Collections.Dictionary dict, string key)
        {
            if (!dict.TryGetValue(key, out Variant value)) return 0;
            return value.VariantType switch
            {
                Variant.Type.Int => (int)value.AsInt64(),
                Variant.Type.Float => (int)value.AsDouble(),
                _ => 0
            };
        }

        /// <summary>从响应字典安全取 long。</summary>
        private static long GetLong(Godot.Collections.Dictionary dict, string key)
        {
            if (!dict.TryGetValue(key, out Variant value)) return 0;
            return value.VariantType switch
            {
                Variant.Type.Int => value.AsInt64(),
                Variant.Type.Float => (long)value.AsDouble(),
                _ => 0
            };
        }

        /// <summary>从响应字典安全取 int 数组（如对话上下文 context）。</summary>
        private static Godot.Collections.Array<int> GetIntArray(Godot.Collections.Dictionary dict, string key)
        {
            var result = new Godot.Collections.Array<int>();
            if (!dict.TryGetValue(key, out Variant value)) return result;
            if (value.VariantType != Variant.Type.Array) return result;

            var arr = value.AsGodotArray();
            foreach (Variant item in arr)
            {
                if (item.VariantType == Variant.Type.Int)
                {
                    result.Add((int)item.AsInt64());
                }
            }

            return result;
        }
    }

    /// <summary>一次 Ollama 生成请求的结果（成功/失败 + 响应文本 + 统计信息）。</summary>
    public sealed class OllamaGenerateResult
    {
        /// <summary>请求是否成功。</summary>
        public bool Success { get; init; }
        /// <summary>失败时的错误描述。</summary>
        public string ErrorMessage { get; init; } = string.Empty;

        /// <summary>实际使用的模型名。</summary>
        public string Model { get; set; } = string.Empty;
        /// <summary>请求创建时间（Ollama 返回）。</summary>
        public string CreatedAt { get; set; } = string.Empty;
        /// <summary>模型生成的最终答案文本。</summary>
        public string ResponseText { get; set; } = string.Empty;
        /// <summary>模型的思考过程文本（若开启思考模式）。</summary>
        public string ThinkingText { get; set; } = string.Empty;
        /// <summary>是否使用了 thinking 回退（response 为空时用 thinking 充当答案）。</summary>
        public bool UsedThinkingFallback { get; set; }
        /// <summary>生成是否完成。</summary>
        public bool Done { get; set; }
        /// <summary>完成原因（如 stop / length）。</summary>
        public string DoneReason { get; set; } = string.Empty;

        // ── Ollama 统计信息（性能观测用） ──
        /// <summary>总耗时（纳秒）。</summary>
        public long TotalDuration { get; set; }
        /// <summary>模型加载耗时（纳秒）。</summary>
        public long LoadDuration { get; set; }
        /// <summary>提示词 token 数。</summary>
        public int PromptEvalCount { get; set; }
        /// <summary>提示词评估耗时（纳秒）。</summary>
        public long PromptEvalDuration { get; set; }
        /// <summary>生成 token 数。</summary>
        public int EvalCount { get; set; }
        /// <summary>生成耗时（纳秒）。</summary>
        public long EvalDuration { get; set; }

        /// <summary>对话上下文（可回传给下一次请求保持多轮记忆）。</summary>
        public Godot.Collections.Array<int> Context { get; set; } = new();
        /// <summary>流式响应时收集的原始 chunk 列表。</summary>
        public Godot.Collections.Array<Godot.Collections.Dictionary> RawChunks { get; set; } = new();
        /// <summary>最终（或最后一个）原始响应对象。</summary>
        public Godot.Collections.Dictionary RawFinalObject { get; set; } = new();

        /// <summary>构造失败结果。</summary>
        public static OllamaGenerateResult FromError(string error)
        {
            return new OllamaGenerateResult
            {
                Success = false,
                ErrorMessage = error
            };
        }
    }
}

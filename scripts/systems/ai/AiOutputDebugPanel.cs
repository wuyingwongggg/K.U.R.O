using Godot;

namespace Kuros.Systems.AI
{
    /// <summary>
    /// On-screen panel for displaying AI request output from AiDecisionBridge.
    /// </summary>
    [GlobalClass]
    public partial class AiOutputDebugPanel : CanvasLayer
    {
        [Export] public NodePath AiDecisionBridgePath { get; set; } = new("../AiDecisionBridge");
        [Export] public NodePath OutputLabelPath { get; set; } = new("Panel/VBox/OutputText");
        [Export] public NodePath ToggleButtonPath { get; set; } = new("Panel/VBox/ToggleButton");
        [Export] public NodePath ContentNodePath { get; set; } = new("Panel/VBox/OutputText");

        private AiDecisionBridge? _bridge;
        private RichTextLabel? _outputLabel;
        private Button? _toggleButton;
        private Control? _contentNode;
        private bool _contentVisible = true;
        private string _lastPromptText = string.Empty;
        private string _lastResponseText = string.Empty;
        private string _lastDecisionJsonText = string.Empty;
        private string _lastDecisionParseError = string.Empty;
        private string _lastErrorText = string.Empty;
        private bool _clearBeforeNextResult;
        // 流式渲染节流：chunk 高频到达（每个 token 一次），不立即重建 RichText，
        // 累计后按节流间隔渲染一次，避免主线程被 RichText 全量重绘拖死
        private float _renderThrottleTimer;
        private const float RenderThrottleInterval = 0.1f;
        // Prompt 显示截断长度：完整 prompt（GameState JSON + 策略）可达数万字符，
        // 全量拼进 RichText 是渲染大头，仅显示开头用于确认请求内容
        private const int PromptPreviewMaxChars = 800;

        public override void _Process(double delta)
        {
            if (_renderThrottleTimer <= 0f)
            {
                return;
            }

            _renderThrottleTimer -= (float)delta;
            if (_renderThrottleTimer <= 0f)
            {
                RenderText();
            }
        }

        public override void _Ready()
        {
            _bridge = GetNodeOrNull<AiDecisionBridge>(AiDecisionBridgePath)
                ?? GetNodeOrNull<AiDecisionBridge>(NormalizeRelativePath(AiDecisionBridgePath));
            _outputLabel = GetNodeOrNull<RichTextLabel>(OutputLabelPath);
            _toggleButton = GetNodeOrNull<Button>(ToggleButtonPath);
            _contentNode = GetNodeOrNull<Control>(ContentNodePath);

            if (_bridge != null)
            {
                _bridge.DecisionPromptBuilt += OnDecisionPromptBuilt;
                _bridge.DecisionChunkReceived += OnDecisionChunkReceived;
                _bridge.DecisionCompleted += OnDecisionCompleted;
                _bridge.DecisionStructured += OnDecisionStructured;
                _bridge.DecisionStructureFailed += OnDecisionStructureFailed;
                _bridge.DecisionFailed += OnDecisionFailed;

                _lastPromptText = _bridge.LastPromptText;
                _lastResponseText = _bridge.LastDecisionText;
                _lastDecisionJsonText = _bridge.LastStructuredDecisionJson;
                _lastDecisionParseError = _bridge.LastDecisionParseError;
            }

            if (_toggleButton != null)
            {
                _toggleButton.Pressed += OnTogglePressed;
                UpdateToggleButtonText();
            }

            if (_outputLabel != null && string.IsNullOrWhiteSpace(_outputLabel.Text))
            {
                RenderText();
            }
        }

        public override void _ExitTree()
        {
            if (_bridge != null)
            {
                _bridge.DecisionPromptBuilt -= OnDecisionPromptBuilt;
                _bridge.DecisionChunkReceived -= OnDecisionChunkReceived;
                _bridge.DecisionCompleted -= OnDecisionCompleted;
                _bridge.DecisionStructured -= OnDecisionStructured;
                _bridge.DecisionStructureFailed -= OnDecisionStructureFailed;
                _bridge.DecisionFailed -= OnDecisionFailed;
            }

            if (_toggleButton != null)
            {
                _toggleButton.Pressed -= OnTogglePressed;
            }

            base._ExitTree();
        }

        private void OnDecisionPromptBuilt(string promptText)
        {
            _lastPromptText = promptText ?? string.Empty;
            _lastResponseText = string.Empty;
            _lastDecisionJsonText = string.Empty;
            _lastDecisionParseError = string.Empty;
            _lastErrorText = string.Empty;
            _clearBeforeNextResult = true;
            RenderText();
        }

        private void OnDecisionChunkReceived(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            ClearWindowIfNeededForNewResult();

            _lastResponseText += chunk;
            // 流式节流：累计 chunk，_Process 中按 RenderThrottleInterval 渲染一次
            _renderThrottleTimer = RenderThrottleInterval;
        }

        private void OnDecisionCompleted(string text)
        {
            ClearWindowIfNeededForNewResult();

            _lastResponseText = text ?? string.Empty;
            _lastErrorText = string.Empty;
            RenderText();
        }

        private void OnDecisionStructured(string decisionJson)
        {
            ClearWindowIfNeededForNewResult();

            _lastDecisionJsonText = decisionJson ?? string.Empty;
            _lastDecisionParseError = string.Empty;
            RenderText();
        }

        private void OnDecisionStructureFailed(string error)
        {
            ClearWindowIfNeededForNewResult();

            _lastDecisionJsonText = string.Empty;
            _lastDecisionParseError = error ?? string.Empty;
            RenderText();
        }

        private void OnDecisionFailed(string error)
        {
            ClearWindowIfNeededForNewResult();

            _lastErrorText = error ?? string.Empty;
            RenderText();
        }

        private void ClearWindowIfNeededForNewResult()
        {
            if (!_clearBeforeNextResult)
            {
                return;
            }

            _lastResponseText = string.Empty;
            _lastDecisionJsonText = string.Empty;
            _lastDecisionParseError = string.Empty;
            _lastErrorText = string.Empty;

            if (_outputLabel != null)
            {
                _outputLabel.Text = string.Empty;
            }

            _clearBeforeNextResult = false;
        }

        private void OnTogglePressed()
        {
            _contentVisible = !_contentVisible;
            if (_contentNode != null)
            {
                _contentNode.Visible = _contentVisible;
            }

            UpdateToggleButtonText();
        }

        private void UpdateToggleButtonText()
        {
            if (_toggleButton == null)
            {
                return;
            }

            _toggleButton.Text = _contentVisible ? "Hide" : "Show";
        }

        private void RenderText()
        {
            if (_outputLabel == null)
            {
                return;
            }

            string promptText = string.IsNullOrWhiteSpace(_lastPromptText)
                ? "(none)"
                : _lastPromptText.Length <= PromptPreviewMaxChars
                    ? _lastPromptText
                    : $"{_lastPromptText[..PromptPreviewMaxChars]}... (truncated {_lastPromptText.Length - PromptPreviewMaxChars} chars)";

            string responseText = string.IsNullOrWhiteSpace(_lastResponseText)
                ? "(waiting or empty)"
                : _lastResponseText;

            string errorText = string.IsNullOrWhiteSpace(_lastErrorText)
                ? "(none)"
                : _lastErrorText;

            string decisionJsonText = string.IsNullOrWhiteSpace(_lastDecisionJsonText)
                ? "(not parsed yet)"
                : _lastDecisionJsonText;

            string decisionParseText = string.IsNullOrWhiteSpace(_lastDecisionParseError)
                ? "(none)"
                : _lastDecisionParseError;

            string modelName = string.IsNullOrWhiteSpace(_bridge?.LastModelName)
                ? (string.IsNullOrWhiteSpace(_bridge?.Model) ? "(no response yet)" : $"{_bridge!.Model} (not responded)")
                : _bridge!.LastModelName;

            // Persona 走 Ollama 的 system 字段（不在 prompt 文本里），单独截断显示便于确认人设已生效
            string personaText = string.IsNullOrWhiteSpace(_bridge?.PersonaSystemPrompt)
                ? "(none)"
                : _bridge!.PersonaSystemPrompt.Length <= PromptPreviewMaxChars
                    ? _bridge.PersonaSystemPrompt
                    : $"{_bridge.PersonaSystemPrompt[..PromptPreviewMaxChars]}... (truncated)";

            _outputLabel.Text = string.Join("\n", new[]
            {
                $"[Model] {modelName}",
                string.Empty,
                "[Persona]",
                personaText,
                string.Empty,
                "[AI Prompt]",
                promptText,
                string.Empty,
                "[AI Response]",
                responseText,
                string.Empty,
                "[Structured Decision]",
                decisionJsonText,
                string.Empty,
                "[Decision Parse Error]",
                decisionParseText,
                string.Empty,
                "[AI Error]",
                errorText
            });
        }

        private static NodePath NormalizeRelativePath(NodePath path)
        {
            if (path.IsEmpty)
            {
                return path;
            }

            string text = path.ToString();
            return text.StartsWith("../", System.StringComparison.Ordinal)
                ? new NodePath(text[3..])
                : path;
        }
    }
}

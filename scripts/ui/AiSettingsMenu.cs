using System;
using System.Threading.Tasks;
using Godot;
using Kuros.Managers;
using Kuros.Systems.AI;

namespace Kuros.UI
{
    /// <summary>
    /// AI 助手 API 设置菜单：玩家自定义 LLM 平台/端点/密钥/模型。
    /// 从 SettingsMenu 的入口按钮叠加打开（MenuLayer）；改动即保存（GameSettingsManager）
    /// 并广播 AiSettingsChanged 即时生效（OllamaClient 订阅）。
    /// Back = 隐藏自身（UIManager.LoadUI 再次进入时自动 Visible=true），SettingsMenu 垫底。
    /// </summary>
    public partial class AiSettingsMenu : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button BackButton { get; private set; } = null!;
        [Export] public OptionButton AIProviderOption { get; private set; } = null!;
        [Export] public LineEdit AIEndpointInput { get; private set; } = null!;
        [Export] public LineEdit AIApiKeyInput { get; private set; } = null!;
        [Export] public LineEdit AIModelInput { get; private set; } = null!;
        [Export] public Button TestButton { get; private set; } = null!;
        [Export] public Label TestResultLabel { get; private set; } = null!;
        [Export] public CheckButton AIEnableToggle { get; private set; } = null!;

        [Signal] public delegate void BackRequestedEventHandler();

        private bool _suppressSave; // 恢复显示时防递归触发保存
        private bool _testing;      // 测试连接进行中（防重复点击）

        private void ConnectButtonSignal(Button? button, string methodName)
        {
            if (button == null) return;
            var callable = new Callable(this, methodName);
            if (!button.IsConnected(Button.SignalName.Pressed, callable))
            {
                button.Connect(Button.SignalName.Pressed, callable);
            }
        }

        private void ConnectOptionButtonSignal(OptionButton? optionButton, string methodName)
        {
            if (optionButton == null) return;
            var callable = new Callable(this, methodName);
            if (!optionButton.IsConnected(OptionButton.SignalName.ItemSelected, callable))
            {
                optionButton.Connect(OptionButton.SignalName.ItemSelected, callable);
            }
        }

        private void ConnectLineEditSignal(LineEdit? input, string methodName)
        {
            if (input == null) return;
            var callable = new Callable(this, methodName);
            if (!input.IsConnected(LineEdit.SignalName.TextChanged, callable))
            {
                input.Connect(LineEdit.SignalName.TextChanged, callable);
            }
        }

        public override void _Ready()
        {
            // 暂停时也能接收输入（战斗内暂停打开设置）
            ProcessMode = ProcessModeEnum.Always;

            if (BackButton == null)
                BackButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/BackButton");
            if (AIProviderOption == null)
                AIProviderOption = GetNodeOrNull<OptionButton>("MenuPanel/VBoxContainer/AIProviderOption");
            if (AIEndpointInput == null)
                AIEndpointInput = GetNodeOrNull<LineEdit>("MenuPanel/VBoxContainer/AIEndpointInput");
            if (AIApiKeyInput == null)
                AIApiKeyInput = GetNodeOrNull<LineEdit>("MenuPanel/VBoxContainer/AIApiKeyInput");
            if (AIModelInput == null)
                AIModelInput = GetNodeOrNull<LineEdit>("MenuPanel/VBoxContainer/AIModelInput");
            if (TestButton == null)
                TestButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/TestButton");
            if (TestResultLabel == null)
                TestResultLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/TestResultLabel");
            if (AIEnableToggle == null)
                AIEnableToggle = GetNodeOrNull<CheckButton>("MenuPanel/VBoxContainer/AIEnableToggle");

            if (AIProviderOption != null)
            {
                AIProviderOption.Clear();
                AIProviderOption.AddItem("Ollama 原生", 0);
                AIProviderOption.AddItem("OpenAI 兼容", 1);
            }

            ConnectButtonSignal(BackButton, nameof(OnBackPressed));
            ConnectButtonSignal(TestButton, nameof(OnTestPressed));
            ConnectOptionButtonSignal(AIProviderOption, nameof(OnProviderSelected));
            ConnectLineEditSignal(AIEndpointInput, nameof(OnFieldChanged));
            ConnectLineEditSignal(AIApiKeyInput, nameof(OnFieldChanged));
            ConnectLineEditSignal(AIModelInput, nameof(OnFieldChanged));

            if (AIEnableToggle != null)
            {
                var callable = new Callable(this, nameof(OnAiEnableToggled));
                if (!AIEnableToggle.IsConnected(CheckButton.SignalName.Toggled, callable))
                {
                    AIEnableToggle.Connect(CheckButton.SignalName.Toggled, callable);
                }
            }

            RestoreFromSettings();
        }

        /// <summary>从 GameSettingsManager 恢复控件显示（suppress 防 TextChanged 递归触发保存）。</summary>
        private void RestoreFromSettings()
        {
            var settings = GameSettingsManager.Instance;
            if (settings == null) return;

            _suppressSave = true;
            if (AIProviderOption != null)
            {
                AIProviderOption.Selected = settings.AiProvider == "openai_compat" ? 1 : 0;
            }
            if (AIEndpointInput != null) AIEndpointInput.Text = settings.AiEndpoint;
            if (AIApiKeyInput != null) AIApiKeyInput.Text = settings.AiApiKey;
            if (AIModelInput != null) AIModelInput.Text = settings.AiModel;
            if (AIEnableToggle != null) AIEnableToggle.ButtonPressed = settings.AiEnabled;
            _suppressSave = false;
        }

        /// <summary>启用开关：独立于 4 项配置——玩家配置并测试成功后开启，P2 才发 LLM 请求。</summary>
        private void OnAiEnableToggled(bool enabled)
        {
            if (_suppressSave) return;
            GameSettingsManager.Instance?.SetAiEnabled(enabled);
        }

        private void OnProviderSelected(long id)
        {
            if (_suppressSave) return;
            CommitSettings();
        }

        private void OnFieldChanged(string newText)
        {
            if (_suppressSave) return;
            CommitSettings();
        }

        /// <summary>统一提交：从控件读取全部 4 值 → SetAiSettings（保存 cfg + 广播即时应用）。</summary>
        private void CommitSettings()
        {
            var settings = GameSettingsManager.Instance;
            if (settings == null) return;

            string provider = (AIProviderOption != null && AIProviderOption.Selected == 1)
                ? "openai_compat"
                : "ollama";
            settings.SetAiSettings(
                provider,
                AIEndpointInput?.Text ?? string.Empty,
                AIApiKeyInput?.Text ?? string.Empty,
                AIModelInput?.Text ?? string.Empty);
        }

        private void OnBackPressed()
        {
            // 隐藏自身（UIManager.LoadUI 再次进入时自动 Visible=true），SettingsMenu 垫底
            Visible = false;
            EmitSignal(SignalName.BackRequested);
        }

        /// <summary>测试连接：先保存当前输入，再用临时客户端实例（不进树，不依赖 OllamaClient 节点存在）
        /// 发一个最小请求验证 平台/端点/密钥/模型 是否可用。</summary>
        private void OnTestPressed()
        {
            if (_testing) return;
            CommitSettings();

            var settings = GameSettingsManager.Instance;
            if (settings == null || TestButton == null || TestResultLabel == null) return;

            _testing = true;
            TestButton.Disabled = true;
            TestResultLabel.Text = "测试中…（最长等待 20 秒）";
            _ = TestConnectionAsync(settings);
        }

        private async Task TestConnectionAsync(GameSettingsManager settings)
        {
            string resultText = string.Empty;
            try
            {
                var client = new OllamaGenerateClient
                {
                    UseOpenAICompat = settings.AiProvider == "openai_compat",
                    Endpoint = settings.AiEndpoint,
                    ApiKey = settings.AiApiKey,
                    DefaultModel = settings.AiModel,
                    TimeoutSeconds = 20,
                    MaxPredictTokens = 16,
                    DefaultStream = false,
                    DisableThinking = true
                };

                var result = await client.GenerateAsync("回复 OK");
                resultText = result.Success
                    ? $"连接成功！模型 {result.Model} 响应：{result.ResponseText.Trim()}"
                    : $"连接失败：{result.ErrorMessage}";
            }
            catch (Exception ex)
            {
                resultText = $"连接失败：{ex.Message}";
            }
            finally
            {
                _testing = false;
                if (TestButton != null && IsInstanceValid(TestButton)) TestButton.Disabled = false;
                if (TestResultLabel != null && IsInstanceValid(TestResultLabel)) TestResultLabel.Text = resultText;
            }
        }

        public override void _Input(InputEvent @event)
        {
            if (!IsVisibleInTree()) return;

            bool isEscKey = @event.IsActionPressed("ui_cancel")
                || (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape);
            if (isEscKey)
            {
                OnBackPressed();
                GetViewport().SetInputAsHandled();
            }
        }
    }
}

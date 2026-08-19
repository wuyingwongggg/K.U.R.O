using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Kuros.Core;
using Kuros.Managers;
using Kuros.Systems.AI;

namespace Kuros.UI
{
    /// <summary>
    /// 设置菜单（书签式）：顶部 TabContainer 切换 画面/音量/API/输入 四类设置，同一场景内切换不跳转。
    /// - 音量/窗口/CRT/语言：原有逻辑保留（改动即生效）
    /// - API：平台/端点/Key/模型/启用/测试连接（内嵌 ApiTab，原独立菜单已删除）
    /// - 输入：改键设置（白名单动作，捕获按键 → InputMap 即时应用 → GameSettingsManager 持久化）
    /// </summary>
    public partial class SettingsMenu : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button BackButton { get; private set; } = null!;
        [Export] public HSlider MasterVolumeSlider { get; private set; } = null!;
        [Export] public HSlider MusicVolumeSlider { get; private set; } = null!;
        [Export] public HSlider SFXVolumeSlider { get; private set; } = null!;
        [Export] public OptionButton WindowModeOption { get; private set; } = null!;
        [Export] public OptionButton LanguageOption { get; private set; } = null!;
        [Export] public CheckButton? CrtToggle { get; private set; }

        // API 设置
        [Export] public OptionButton AIProviderOption { get; private set; } = null!;
        /// <summary>端点输入框（与右侧常用端点下拉并排成一行，可直接输入或选预设）。</summary>
        [Export] public LineEdit AIEndpointInput { get; private set; } = null!;
        /// <summary>常用端点快捷下拉（选中后自动填端点+切平台）。</summary>
        [Export] public OptionButton AIEndpointPresetOption { get; private set; } = null!;
        [Export] public LineEdit AIApiKeyInput { get; private set; } = null!;
        [Export] public LineEdit AIModelInput { get; private set; } = null!;
        [Export] public CheckButton AIEnableToggle { get; private set; } = null!;
        [Export] public Button TestButton { get; private set; } = null!;
        [Export] public Label TestResultLabel { get; private set; } = null!;

        // 输入改键
        [Export] public VBoxContainer RebindList { get; private set; } = null!;
        [Export] public HSlider HoldThresholdSlider { get; private set; } = null!;
        [Export] public Label HoldThresholdValueLabel { get; private set; } = null!;
        [Export] public Label HintLabel { get; private set; } = null!;
        [Export] public Button ConfirmBindingsButton { get; private set; } = null!;

        // 信号
        [Signal] public delegate void BackRequestedEventHandler();
        [Signal] public delegate void SettingsChangedEventHandler();

        private bool _suppressWindowSelection;
        private bool _suppressSave;      // API 恢复显示时防递归
        private bool _testing;           // 测试连接进行中
        private bool _suppressHoldThreshold; // 长按阈值恢复防递归
        private string _capturingAction = string.Empty; // 当前捕获按键的动作（空 = 未捕获）

        // 改键列表控件缓存：action → (键名Label, 改键Button, 长按开关)
        private readonly Dictionary<string, (Label keyLabel, Button rebindButton, CheckButton longPressToggle)> _rebindRows = new();

        // 改键确认流程：捕获的键先暂存（不立即应用），玩家改完所有按键后
        // 点击列表下方的全局"确认改键"按钮 → 统一冲突检测 → 通过才应用+持久化；
        // 不确认（关闭菜单）自动回退原键位
        private readonly Dictionary<string, int> _pendingBindings = new();

        private void ConnectButtonSignal(Button? button, string methodName)
        {
            if (button == null) return;
            var callable = new Callable(this, methodName);
            if (!button.IsConnected(Button.SignalName.Pressed, callable))
            {
                button.Connect(Button.SignalName.Pressed, callable);
            }
        }

        private void ConnectSliderSignal(Slider? slider, string methodName)
        {
            if (slider == null) return;
            var callable = new Callable(this, methodName);
            if (!slider.IsConnected(Slider.SignalName.ValueChanged, callable))
            {
                slider.Connect(Slider.SignalName.ValueChanged, callable);
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
            ProcessMode = ProcessModeEnum.Always;

            var tabs = GetNodeOrNull<TabContainer>("MenuPanel/VBoxContainer/SettingsTabs");
            if (tabs != null)
            {
                // Tab 标题用中文（默认是节点名 VideoTab/AudioTab/InputTab/ApiTab）
                tabs.SetTabTitle(0, "画面");
                tabs.SetTabTitle(1, "音量");
                tabs.SetTabTitle(2, "输入");
                tabs.SetTabTitle(3, "API");
            }

            // ── 音量 ──
            if (MasterVolumeSlider == null)
                MasterVolumeSlider = GetNodeOrNull<HSlider>("MenuPanel/VBoxContainer/SettingsTabs/AudioTab/Margin/VBox/MasterVolumeSlider");
            if (MasterVolumeSlider != null)
            {
                ConnectSliderSignal(MasterVolumeSlider, nameof(OnMasterVolumeChanged));
                MasterVolumeSlider.Value = 100.0;
            }
            if (MusicVolumeSlider == null)
                MusicVolumeSlider = GetNodeOrNull<HSlider>("MenuPanel/VBoxContainer/SettingsTabs/AudioTab/Margin/VBox/MusicVolumeSlider");
            if (MusicVolumeSlider != null)
            {
                ConnectSliderSignal(MusicVolumeSlider, nameof(OnMusicVolumeChanged));
                MusicVolumeSlider.Value = 100.0;
            }
            if (SFXVolumeSlider == null)
                SFXVolumeSlider = GetNodeOrNull<HSlider>("MenuPanel/VBoxContainer/SettingsTabs/AudioTab/Margin/VBox/SFXVolumeSlider");
            if (SFXVolumeSlider != null)
            {
                ConnectSliderSignal(SFXVolumeSlider, nameof(OnSFXVolumeChanged));
                SFXVolumeSlider.Value = 100.0;
            }

            // ── 画面 ──
            SetupWindowModeOption();

            if (LanguageOption == null)
                LanguageOption = GetNodeOrNull<OptionButton>("MenuPanel/VBoxContainer/SettingsTabs/VideoTab/Margin/VBox/LanguageOption");
            if (LanguageOption != null)
            {
                LanguageOption.Clear();
                ConnectOptionButtonSignal(LanguageOption, nameof(OnLanguageSelected));
                LanguageOption.AddItem("简体中文");
                // English 暂注释：中英文切换已回退（无翻译表），选中无效；恢复翻译时取消注释并接线 SetLocale
                // LanguageOption.AddItem("English");

            }

            if (CrtToggle == null)
                CrtToggle = GetNodeOrNull<CheckButton>("MenuPanel/VBoxContainer/SettingsTabs/VideoTab/Margin/VBox/CrtToggle");
            if (CrtToggle != null)
            {
                CrtToggle.ButtonPressed = GameSettingsManager.Instance?.CrtEnabled ?? false;
                var callable = new Callable(this, nameof(OnCrtToggled));
                if (!CrtToggle.IsConnected(CheckButton.SignalName.Toggled, callable))
                    CrtToggle.Connect(CheckButton.SignalName.Toggled, callable);
            }

            // ── API ──
            if (AIProviderOption == null)
                AIProviderOption = GetNodeOrNull<OptionButton>("MenuPanel/VBoxContainer/SettingsTabs/ApiTab/Margin/VBox/AIProviderOption");
            if (AIEndpointInput == null)
                AIEndpointInput = GetNodeOrNull<LineEdit>("MenuPanel/VBoxContainer/SettingsTabs/ApiTab/Margin/VBox/AIEndpointRow/AIEndpointInput");
            if (AIApiKeyInput == null)
                AIApiKeyInput = GetNodeOrNull<LineEdit>("MenuPanel/VBoxContainer/SettingsTabs/ApiTab/Margin/VBox/AIApiKeyInput");
            if (AIModelInput == null)
                AIModelInput = GetNodeOrNull<LineEdit>("MenuPanel/VBoxContainer/SettingsTabs/ApiTab/Margin/VBox/AIModelInput");
            if (AIEnableToggle == null)
                AIEnableToggle = GetNodeOrNull<CheckButton>("MenuPanel/VBoxContainer/SettingsTabs/ApiTab/Margin/VBox/AIEnableToggle");
            if (TestButton == null)
                TestButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/SettingsTabs/ApiTab/Margin/VBox/TestButton");
            if (TestResultLabel == null)
                TestResultLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/SettingsTabs/ApiTab/Margin/VBox/TestResultLabel");
            if (AIEndpointPresetOption == null)
                AIEndpointPresetOption = GetNodeOrNull<OptionButton>("MenuPanel/VBoxContainer/SettingsTabs/ApiTab/Margin/VBox/AIEndpointRow/AIEndpointPresetOption");

            if (AIProviderOption != null)
            {
                AIProviderOption.Clear();
                AIProviderOption.AddItem("Ollama 原生", 0);
                AIProviderOption.AddItem("OpenAI 兼容", 1);
                AIProviderOption.AddItem("Anthropic 原生", 2);
            }
            ConnectOptionButtonSignal(AIProviderOption, nameof(OnAiProviderSelected));

            // 常用端点快捷下拉：第 0 项是提示占位（禁用），中间对应 AiEndpointPresets 数组，
            // 末尾“自定义”项仅作状态显示（当前端点文本不匹配任何预设时选中）
            if (AIEndpointPresetOption != null)
            {
                AIEndpointPresetOption.Clear();
                AIEndpointPresetOption.AddItem("选择常用端点…", 0);
                AIEndpointPresetOption.SetItemDisabled(0, true);
                for (int i = 0; i < AiEndpointPresets.Length; i++)
                {
                    AIEndpointPresetOption.AddItem(AiEndpointPresets[i].Label, i + 1);
                }
                AIEndpointPresetOption.AddItem("自定义", AiEndpointPresets.Length + 1);
            }
            ConnectOptionButtonSignal(AIEndpointPresetOption, nameof(OnEndpointPresetSelected));
            ConnectLineEditSignal(AIEndpointInput, nameof(OnAiFieldChanged));
            ConnectLineEditSignal(AIApiKeyInput, nameof(OnAiFieldChanged));
            ConnectLineEditSignal(AIModelInput, nameof(OnAiFieldChanged));
            if (AIEnableToggle != null)
            {
                var aiCallable = new Callable(this, nameof(OnAiEnableToggled));
                if (!AIEnableToggle.IsConnected(CheckButton.SignalName.Toggled, aiCallable))
                    AIEnableToggle.Connect(CheckButton.SignalName.Toggled, aiCallable);
            }
            ConnectButtonSignal(TestButton, nameof(OnTestPressed));
            RestoreApiFromSettings();

            // ── 输入改键 ──
            if (RebindList == null)
                RebindList = GetNodeOrNull<VBoxContainer>("MenuPanel/VBoxContainer/SettingsTabs/InputTab/Margin/VBox/Scroll/RebindList");
            if (HintLabel == null)
                HintLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/SettingsTabs/InputTab/Margin/VBox/HintLabel");
            if (ConfirmBindingsButton == null)
                ConfirmBindingsButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/SettingsTabs/InputTab/Margin/VBox/ConfirmBindingsButton");
            ConnectButtonSignal(ConfirmBindingsButton, nameof(OnConfirmBindingsPressed));
            BuildRebindList();

            // 长按判定时长滑块
            if (HoldThresholdSlider == null)
                HoldThresholdSlider = GetNodeOrNull<HSlider>("MenuPanel/VBoxContainer/SettingsTabs/InputTab/Margin/VBox/HoldThresholdSlider");
            if (HoldThresholdValueLabel == null)
                HoldThresholdValueLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/SettingsTabs/InputTab/Margin/VBox/HoldThresholdValueLabel");
            if (HoldThresholdSlider != null)
            {
                ConnectSliderSignal(HoldThresholdSlider, nameof(OnHoldThresholdChanged));
                var hs = GameSettingsManager.Instance;
                _suppressHoldThreshold = true;
                HoldThresholdSlider.Value = hs?.HoldThresholdSeconds ?? 0.35f;
                _suppressHoldThreshold = false;
                UpdateHoldThresholdLabel(hs?.HoldThresholdSeconds ?? 0.35f);
            }

            var settings = GameSettingsManager.Instance;
            if (settings != null)
            {
                settings.InputBindingsChanged += RefreshRebindList;
            }

            // ── 返回 ──
            if (BackButton == null)
                BackButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/BackButton");
            ConnectButtonSignal(BackButton, nameof(OnBackPressed));
        }

        public override void _ExitTree()
        {
            var settings = GameSettingsManager.Instance;
            if (settings != null)
            {
                settings.InputBindingsChanged -= RefreshRebindList;
            }
            base._ExitTree();
        }

        // ══════════════ 音量 ══════════════

        private void OnMasterVolumeChanged(double value)
        {
            AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Master"), (float)(value - 100) / 2.0f);
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnMusicVolumeChanged(double value)
        {
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnSFXVolumeChanged(double value)
        {
            EmitSignal(SignalName.SettingsChanged);
        }

        // ══════════════ 画面 ══════════════

        private void SetupWindowModeOption()
        {
            if (WindowModeOption == null)
            {
                WindowModeOption = GetNodeOrNull<OptionButton>("MenuPanel/VBoxContainer/SettingsTabs/VideoTab/Margin/VBox/WindowModeOption");
            }
            if (WindowModeOption == null) return;

            var settings = GameSettingsManager.Instance;
            if (settings == null) return;

            WindowModeOption.Clear();
            var presets = settings.Presets;
            for (int i = 0; i < presets.Length; i++)
            {
                WindowModeOption.AddItem(presets[i].DisplayName, i);
            }
            ConnectOptionButtonSignal(WindowModeOption, nameof(OnWindowModeSelected));

            _suppressWindowSelection = true;
            WindowModeOption.Selected = settings.GetPresetIndex(settings.CurrentPreset.Id);
            _suppressWindowSelection = false;
        }

        private void OnWindowModeSelected(long index)
        {
            if (_suppressWindowSelection || WindowModeOption == null) return;

            var settings = GameSettingsManager.Instance;
            if (settings == null) return;

            if (index < 0 || index >= settings.Presets.Length) return;

            var preset = settings.GetPresetByIndex((int)index);
            settings.SetPreset(preset.Id, applyImmediately: true);
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnLanguageSelected(long index)
        {
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnHoldThresholdChanged(double value)
        {
            if (_suppressHoldThreshold) return;
            GameSettingsManager.Instance?.SetHoldThresholdSeconds((float)value);
            UpdateHoldThresholdLabel((float)value);
        }

        private void UpdateHoldThresholdLabel(float seconds)
        {
            if (HoldThresholdValueLabel != null)
            {
                HoldThresholdValueLabel.Text = $"{seconds:0.00} 秒";
            }
        }

        private void OnCrtToggled(bool enabled)
        {
            GameSettingsManager.Instance?.SetCrtEnabled(enabled);
            EmitSignal(SignalName.SettingsChanged);
        }

        // ══════════════ API 设置（内嵌 ApiTab） ══════════════

        private void RestoreApiFromSettings()
        {
            var settings = GameSettingsManager.Instance;
            if (settings == null) return;

            _suppressSave = true;
            if (AIProviderOption != null)
            {
                AIProviderOption.Selected = settings.AiProvider switch
                {
                    "openai_compat" => 1,
                    "anthropic" => 2,
                    _ => 0
                };
            }
            if (AIEndpointInput != null) AIEndpointInput.Text = settings.AiEndpoint;
            if (AIApiKeyInput != null) AIApiKeyInput.Text = settings.AiApiKey;
            if (AIModelInput != null) AIModelInput.Text = settings.AiModel;
            if (AIEnableToggle != null) AIEnableToggle.ButtonPressed = settings.AiEnabled;
            SyncEndpointPresetDisplay();
            _suppressSave = false;
        }

        private void OnAiProviderSelected(long id)
        {
            if (_suppressSave) return;
            CommitApiSettings();
        }

        private void OnAiFieldChanged(string newText)
        {
            if (_suppressSave) return;
            CommitApiSettings();
            // 手动编辑端点后同步下拉显示：命中预设显示预设名，否则显示“自定义”
            SyncEndpointPresetDisplay();
        }

        private void OnAiEnableToggled(bool enabled)
        {
            if (_suppressSave) return;
            GameSettingsManager.Instance?.SetAiEnabled(enabled);
        }

        private void CommitApiSettings()
        {
            var settings = GameSettingsManager.Instance;
            if (settings == null) return;

            string provider = "ollama";
            if (AIProviderOption != null)
            {
                provider = AIProviderOption.Selected switch
                {
                    1 => "openai_compat",
                    2 => "anthropic",
                    _ => "ollama"
                };
            }
            settings.SetAiSettings(
                provider,
                AIEndpointInput?.Text ?? string.Empty,
                AIApiKeyInput?.Text ?? string.Empty,
                AIModelInput?.Text ?? string.Empty);
        }

        /// <summary>常用端点预设表：(显示名, 平台选项索引 0=ollama/1=openai_compat/2=anthropic, 完整请求 URL)。</summary>
        private static readonly (string Label, int ProviderIndex, string Url)[] AiEndpointPresets =
        {
            ("DeepSeek", 1, "https://api.deepseek.com/chat/completions"),
            ("ChatGPT", 1, "https://api.openai.com/v1/chat/completions"),
            ("Claude", 2, "https://api.anthropic.com/v1/messages"),
            ("Gemini", 1, "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions"),
            ("Grok", 1, "https://api.x.ai/v1/chat/completions"),
            ("Kimi", 1, "https://api.moonshot.cn/v1/chat/completions"),
            ("通义千问", 1, "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions"),
            ("智谱 GLM", 1, "https://open.bigmodel.cn/api/paas/v4/chat/completions"),
            ("Ollama 本地（兼容）", 1, "http://localhost:11434/v1/chat/completions"),
            ("Ollama 本地（原生）", 0, "http://localhost:11434/api/generate"),
        };

        /// <summary>选择常用端点预设：自动填端点 + 切换对应平台 + 提交配置。手动输入不受影响。
        /// “自定义”项与占位项仅作状态显示，不覆盖输入框内容。</summary>
        private void OnEndpointPresetSelected(long index)
        {
            if (_suppressSave || AIProviderOption == null || AIEndpointInput == null) return;

            int presetIndex = (int)index - 1;   // 第 0 项是占位提示
            if (presetIndex < 0 || presetIndex >= AiEndpointPresets.Length) return;

            var (_, providerIndex, url) = AiEndpointPresets[presetIndex];

            _suppressSave = true;
            AIProviderOption.Selected = providerIndex;
            AIEndpointInput.Text = url;
            _suppressSave = false;

            CommitApiSettings();
        }

        /// <summary>根据当前端点文本同步下拉显示：命中预设则选中对应项，否则落到“自定义”。</summary>
        private void SyncEndpointPresetDisplay()
        {
            if (AIEndpointPresetOption == null || AIEndpointInput == null) return;

            string current = AIEndpointInput.Text.Trim();
            for (int i = 0; i < AiEndpointPresets.Length; i++)
            {
                if (string.Equals(AiEndpointPresets[i].Url, current, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (AIEndpointPresetOption.Selected != i + 1)
                    {
                        _suppressSave = true;
                        AIEndpointPresetOption.Selected = i + 1;
                        _suppressSave = false;
                    }
                    return;
                }
            }

            int customIndex = AiEndpointPresets.Length + 1;
            if (AIEndpointPresetOption.Selected != customIndex)
            {
                _suppressSave = true;
                AIEndpointPresetOption.Selected = customIndex;
                _suppressSave = false;
            }
        }

        private void OnTestPressed()
        {
            if (_testing) return;
            CommitApiSettings();

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
            bool useOpenAiCompat = settings.AiProvider == "openai_compat";
            bool useAnthropic = settings.AiProvider == "anthropic";

            // 端点校验：Ollama 留空回退本地默认端点；Anthropic 留空回退官方端点；
            // OpenAI 兼容模式必须填完整路径（平台众多无法猜测）
            string endpoint = settings.AiEndpoint;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                if (useAnthropic)
                {
                    endpoint = "https://api.anthropic.com/v1/messages";
                }
                else if (!useOpenAiCompat)
                {
                    endpoint = "http://localhost:11434/api/generate";
                }
                else
                {
                    resultText = Tr("连接失败：OpenAI 兼容模式需要填写完整服务地址（如 https://api.deepseek.com/chat/completions）");
                    _testing = false;
                    if (TestButton != null && IsInstanceValid(TestButton)) TestButton.Disabled = false;
                    if (TestResultLabel != null && IsInstanceValid(TestResultLabel)) TestResultLabel.Text = resultText;
                    return;
                }
            }

            try
            {
                var client = new OllamaGenerateClient
                {
                    UseOpenAICompat = useOpenAiCompat,
                    UseAnthropicProtocol = useAnthropic,
                    Endpoint = endpoint,
                    ApiKey = settings.AiApiKey,
                    DefaultModel = settings.AiModel,
                    TimeoutSeconds = 20,
                    MaxPredictTokens = 16,
                    DefaultStream = false,
                    DisableThinking = true
                };

                var result = await client.GenerateAsync("回复 OK");
                resultText = result.Success
                    ? string.Format(Tr("连接成功！模型 {0} 响应：{1}"), result.Model, result.ResponseText.Trim())
                    : string.Format(Tr("连接失败：{0}"), result.ErrorMessage);
            }
            catch (Exception ex)
            {
                resultText = string.Format(Tr("连接失败：{0}"), ex.Message);
            }
            finally
            {
                _testing = false;
                if (TestButton != null && IsInstanceValid(TestButton)) TestButton.Disabled = false;
                if (TestResultLabel != null && IsInstanceValid(TestResultLabel)) TestResultLabel.Text = resultText;
            }
        }

        // ══════════════ 输入改键 ══════════════

        /// <summary>构建改键列表（每动作一行：中文名 + 当前键名 + 改键按钮）。</summary>
        private void BuildRebindList()
        {
            if (RebindList == null) return;

            // 清空旧行（避免重复构建）
            foreach (Node child in RebindList.GetChildren())
            {
                child.QueueFree();
            }
            _rebindRows.Clear();

            foreach (var (action, displayName) in InputActions.RebindableActions)
            {
                var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                var nameLabel = new Label
                {
                    Text = displayName,
                    // 固定宽度（容器内锚点无效，用固定最小宽控制列宽，防弹性压缩）
                    CustomMinimumSize = new Vector2(130, 28),
                    SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var keyLabel = new Label
                {
                    Text = GetActionDisplayKey(action),
                    CustomMinimumSize = new Vector2(100, 28),
                    SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var button = new Button
                {
                    Text = "改键",
                    CustomMinimumSize = new Vector2(100, 28),
                    // 固定宽度：捕获时的长文本被裁剪为省略号，不挤占左右排布
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
                };
                button.Pressed += () => OnRebindPressed(action);

                // 长按触发开关：仅在该动作与其他动作共享同一键时显示（独立键动作无需长短按区分）
                bool hasConflict = IsActionKeyShared(action);
                var longPressToggle = new CheckButton
                {
                    Text = "长按",
                    Visible = hasConflict,
                    ButtonPressed = GameSettingsManager.Instance?.IsActionLongPress(action) ?? action == "place",
                    Disabled = action == "place", // place 语义固定长按，不可改
                    // 固定紧凑宽度 + 不扩展：避免按文本展开挤占行内排布
                    CustomMinimumSize = new Vector2(72, 28),
                    SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
                };
                longPressToggle.Toggled += (bool on) => OnLongPressToggled(action, on);

                row.AddChild(nameLabel);
                row.AddChild(keyLabel);
                row.AddChild(button);
                row.AddChild(longPressToggle);
                RebindList.AddChild(row);

                _rebindRows[action] = (keyLabel, button, longPressToggle);
            }
        }

        /// <summary>刷新全部行显示的当前键名（改键/恢复时调用）。</summary>
        private void RefreshRebindList()
        {
            foreach (var (action, row) in _rebindRows)
            {
                if (IsInstanceValid(row.keyLabel))
                {
                    row.keyLabel.Text = GetActionDisplayKey(action);
                }
                if (IsInstanceValid(row.rebindButton))
                {
                    row.rebindButton.Text = _capturingAction == action ? "监听中…" : "改键";
                }
                if (IsInstanceValid(row.longPressToggle))
                {
                    // 改键后同键关系可能变化：开关仅在与其他动作共享同一键时显示
                    row.longPressToggle.Visible = IsActionKeyShared(action);
                }
            }
        }

        /// <summary>长按触发开关变化：持久化 + 广播（仲裁器下次注册时生效）。</summary>
        private void OnLongPressToggled(string action, bool on)
        {
            GameSettingsManager.Instance?.SetActionLongPress(action, on);
        }

        /// <summary>动作当前是否与其他白名单动作共享同一键（同键才需要长短按分流开关）。
        /// 用最终键位判断（暂存优先）——捕获新键后立即按新键位显示开关，无需等确认。</summary>
        private bool IsActionKeyShared(string action)
        {
            int key = GetResolvedKey(action);
            if (key == 0) return false;

            foreach (var (otherAction, _) in InputActions.RebindableActions)
            {
                if (otherAction == action) continue;
                int otherKey = GetResolvedKey(otherAction);
                if (otherKey == key) return true;
            }
            return false;
        }

        /// <summary>动作当前键名（暂存优先，否则已保存/InputMap 默认；正数键盘/负数鼠标）。</summary>
        private string GetActionDisplayKey(string action)
        {
            int keycode = GetResolvedKey(action);
            if (keycode > 0)
            {
                return OS.GetKeycodeString((Key)keycode);
            }
            if (keycode < 0)
            {
                return GetMouseButtonDisplayName((MouseButton)(-keycode));
            }
            return "—";
        }

        /// <summary>鼠标按键中文名（-1 左键 / -2 右键 / -3 中键 / -4 X1 / -5 X2，其他回退数值）。</summary>
        private static string GetMouseButtonDisplayName(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => "鼠标左键",
                MouseButton.Right => "鼠标右键",
                MouseButton.Middle => "鼠标中键",
                _ => $"鼠标键{(int)button}"
            };
        }

        private void OnRebindPressed(string action)
        {
            if (_capturingAction == action)
            {
                // 捕获中再点：取消捕获
                ClearCapturing();
                return;
            }

            // 开始捕获
            _capturingAction = action;
            RefreshRebindList();
        }

        /// <summary>清空捕获状态（ESC/再点取消；暂存键保留等待全局确认）。</summary>
        private void ClearCapturing()
        {
            _capturingAction = string.Empty;
            RefreshRebindList();
        }

        /// <summary>捕获到候选键：暂存（不立即应用），等待列表下方全局"确认改键"统一校验应用。</summary>
        private void CaptureKey(string action, int keycode)
        {
            _pendingBindings[action] = keycode;
            _capturingAction = string.Empty;
            ClearConflictHighlight();
            RefreshRebindList();
        }

        /// <summary>动作的最终键位（暂存优先，否则已保存/默认）：正数键盘/负数鼠标。</summary>
        private int GetResolvedKey(string action)
        {
            if (_pendingBindings.TryGetValue(action, out int pending))
            {
                return pending;
            }
            var settings = GameSettingsManager.Instance;
            int saved = settings != null ? settings.GetActionKeycode(action) : 0;
            if (saved != 0) return saved;
            foreach (var e in InputMap.ActionGetEvents(action))
            {
                int key = e switch
                {
                    InputEventKey k => (int)k.PhysicalKeycode,
                    InputEventMouseButton m => -(int)m.ButtonIndex,
                    _ => 0
                };
                if (key != 0) return key;
            }
            return 0;
        }

        /// <summary>全局确认：对所有动作的最终键位做冲突检测（同键 ≥2 短按 或 ≥2 长按）。
        /// 无冲突 → 逐个应用（SetActionBinding 持久化）并清暂存；有冲突 → 标红提示，不应用。</summary>
        private void ConfirmAllBindings()
        {
            var conflicts = GetConflicts();
            if (conflicts.Count > 0)
            {
                ShowConflict(conflicts);
                return;
            }

            var settings = GameSettingsManager.Instance;
            if (settings != null)
            {
                foreach (var (action, keycode) in _pendingBindings)
                {
                    settings.SetActionBinding(action, keycode);
                }
            }
            _pendingBindings.Clear();
            ClearConflictHighlight();
            RefreshRebindList();
        }

        /// <summary>全量冲突检测：返回冲突动作列表（同键动作中 ≥2 短按或 ≥2 长按的参与动作）。</summary>
        private List<string> GetConflicts()
        {
            var settings = GameSettingsManager.Instance;
            var keyToActions = new Dictionary<int, List<string>>();
            foreach (var (action, _) in InputActions.RebindableActions)
            {
                int key = GetResolvedKey(action);
                if (key == 0) continue;
                if (!keyToActions.TryGetValue(key, out var list))
                {
                    keyToActions[key] = list = new List<string>();
                }
                list.Add(action);
            }

            var conflicts = new List<string>();
            foreach (var list in keyToActions.Values)
            {
                var shorts = new List<string>();
                var longs = new List<string>();
                foreach (var action in list)
                {
                    bool isLong = settings != null && settings.IsActionLongPress(action);
                    if (isLong) longs.Add(action); else shorts.Add(action);
                }
                if (shorts.Count >= 2) conflicts.AddRange(shorts);
                if (longs.Count >= 2) conflicts.AddRange(longs);
            }
            return conflicts;
        }

        /// <summary>冲突提示：冲突动作行键名标红 + HintLabel 显示提示（保持标红直到下次操作）。</summary>
        private void ShowConflict(List<string> conflicts)
        {
            foreach (var a in conflicts)
            {
                if (_rebindRows.TryGetValue(a, out var row) && IsInstanceValid(row.keyLabel))
                {
                    row.keyLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f, 1f));
                }
            }

            var names = new List<string>();
            foreach (var a in conflicts) names.Add(InputActions.GetDisplayName(a));
            if (HintLabel != null)
            {
                string typeText = GetLongPressCount(conflicts) == conflicts.Count ? "长按" : "短按";
                HintLabel.Text = "⚠ 冲突：" + string.Join("、", names)
                    + " 绑定了同一按键且同为" + typeText + "，请改用不同按键后再确认。";
                HintLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f, 1f));
            }
        }

        private int GetLongPressCount(List<string> actions)
        {
            var settings = GameSettingsManager.Instance;
            int count = 0;
            foreach (var a in actions)
            {
                if (settings != null && settings.IsActionLongPress(a)) count++;
            }
            return count;
        }

        /// <summary>清除冲突标红与提示。</summary>
        private void ClearConflictHighlight()
        {
            foreach (var (action, row) in _rebindRows)
            {
                if (IsInstanceValid(row.keyLabel))
                {
                    row.keyLabel.RemoveThemeColorOverride("font_color");
                }
            }
            if (HintLabel != null)
            {
                HintLabel.RemoveThemeColorOverride("font_color");
                HintLabel.Text = "点击\"改键\"后按下新按键（支持键盘与鼠标键）。改完后点击下方\"确认改键\"生效。";
            }
        }

        /// <summary>全局确认改键按钮：校验并应用全部暂存键位。</summary>
        private void OnConfirmBindingsPressed()
        {
            ConfirmAllBindings();
        }

        public override void _Input(InputEvent @event)
        {
            if (!IsVisibleInTree()) return;

            // 改键捕获模式
            if (!string.IsNullOrEmpty(_capturingAction))
            {
                if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
                {
                    if (keyEvent.Keycode == Key.Escape || @event.IsActionPressed("ui_cancel"))
                    {
                        // ESC 取消捕获（不关菜单）
                        ClearCapturing();
                    }
                    else
                    {
                        // 捕获到候选键：暂存待全局确认，不立即应用
                        CaptureKey(_capturingAction, (int)keyEvent.PhysicalKeycode);
                    }
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
                {
                    // 任意动作接受鼠标键：绑定存负数（-buttonIndex）
                    CaptureKey(_capturingAction, -(int)mouseEvent.ButtonIndex);
                    GetViewport().SetInputAsHandled();
                    return;
                }
                return;
            }

            // 常规 ESC 关闭
            bool isEscKey = @event.IsActionPressed("ui_cancel")
                || (@event is InputEventKey escKey && escKey.Pressed && escKey.Keycode == Key.Escape);
            if (isEscKey)
            {
                OnBackPressed();
                GetViewport().SetInputAsHandled();
            }
        }

        private void OnBackPressed()
        {
            EmitSignal(SignalName.BackRequested);
        }
    }
}

using Godot;
using System;

namespace Kuros.Managers
{
	/// <summary>
	/// 游戏设置管理器：负责加载/保存窗口模式配置并在启动时应用
	/// </summary>
	public partial class GameSettingsManager : Node
	{
		public static GameSettingsManager Instance { get; private set; } = null!;

		[Signal] public delegate void CrtEnabledChangedEventHandler(bool enabled);
		/// <summary>AI 助手 API 配置变化时触发（OllamaClient 订阅以即时应用）。</summary>
		[Signal] public delegate void AiSettingsChangedEventHandler();

		private const string ConfigPath = "user://config/window_settings.cfg";
		private const string WindowSection = "Window";
		private const string PresetKey = "Preset";
		private const string CrtKey = "CrtEnabled";
		private const string AiSection = "AI";
		private const string AiProviderKey = "AiProvider";
		private const string AiEndpointKey = "AiEndpoint";
		private const string AiApiKeyKey = "AiApiKey";
		private const string AiModelKey = "AiModel";
		private const string AiEnabledKey = "AiEnabled";

		/// <summary>AI 提供商默认值（"openai_compat" = OpenAI 兼容协议，玩家场景主流是云 API；
		/// 开发用本地 Ollama 时在游戏内设置中切换为 "ollama" 并填本地地址）。</summary>
		public const string DefaultAiProvider = "openai_compat";
		/// <summary>端点/模型默认为空 = 未配置状态（玩家需自行填写自己的 API 配置；不回退开发默认值）。</summary>
		public const string DefaultAiEndpoint = "";
		public const string DefaultAiModel = "";

		private readonly WindowPreset[] _presets =
		{
			// 三种预设：全屏 1920x1080、窗口 1920x1080、窗口 1280x720
			new WindowPreset("fullscreen_1080p", "全屏 1920x1080", DisplayServer.WindowMode.ExclusiveFullscreen, new Vector2I(1920, 1080)),
			new WindowPreset("window_1080p", "窗口 1920x1080", DisplayServer.WindowMode.Windowed, new Vector2I(1920, 1080)),
			new WindowPreset("window_720p", "窗口 1280x720", DisplayServer.WindowMode.Windowed, new Vector2I(1280, 720)),
		};

		private string _currentPresetId = "window_1080p";
		private bool _crtEnabled = false;
		private string _aiProvider = DefaultAiProvider;
		private string _aiEndpoint = DefaultAiEndpoint;
		// 注意：ApiKey 明文存储于 user://config（单机 demo 可接受；联网发布时需自行加密）
		private string _aiApiKey = string.Empty;
		private string _aiModel = DefaultAiModel;
		private bool _aiEnabled = false;

		public WindowPreset CurrentPreset => GetPresetById(_currentPresetId);
		public WindowPreset[] Presets => _presets;
		public bool CrtEnabled => _crtEnabled;
		public string AiProvider => _aiProvider;
		public string AiEndpoint => _aiEndpoint;
		public string AiApiKey => _aiApiKey;
		public string AiModel => _aiModel;
		/// <summary>AI 助手是否启用（默认关——玩家未配置 API 时 P2 纯规则模式，不发 LLM 请求）。</summary>
		public bool AiEnabled => _aiEnabled;

		public override void _Ready()
		{
			if (Instance != null && Instance != this)
			{
				QueueFree();
				return;
			}

			Instance = this;
			EnsureConfigDirectoryExists();
			LoadSettings();
			ApplyCurrentPreset();
		}

		/// <summary>
		/// 确保配置目录存在
		/// </summary>
		private void EnsureConfigDirectoryExists()
		{
			var dirAccess = DirAccess.Open("user://");
			if (dirAccess == null)
			{
				GD.PrintErr("GameSettingsManager: 无法打开 user:// 目录");
				return;
			}

			if (!dirAccess.DirExists("config"))
			{
				var err = dirAccess.MakeDir("config");
				if (err != Error.Ok)
				{
					GD.PrintErr($"GameSettingsManager: 无法创建 config 目录，错误: {err}");
				}
				else
				{
					GD.Print("GameSettingsManager: 已创建 config 目录");
				}
			}
		}

		public void ApplyCurrentPreset()
		{
			var preset = CurrentPreset;
			DisplayServer.WindowSetMode(preset.Mode);

			if (preset.Mode == DisplayServer.WindowMode.Windowed)
			{
				DisplayServer.WindowSetSize(preset.Size);
				CenterWindow();
			}

			ApplyProjectSettings(preset);
		}

		public void SetPreset(string presetId, bool applyImmediately)
		{
			if (string.IsNullOrEmpty(presetId))
				return;

			_currentPresetId = presetId;
			SaveSettings();

			if (applyImmediately)
			{
				ApplyCurrentPreset();
			}
		}

		public void SetCrtEnabled(bool value)
		{
			if (_crtEnabled == value) return;
			_crtEnabled = value;
			SaveSettings();
			EmitSignal(SignalName.CrtEnabledChanged, _crtEnabled);
		}

		/// <summary>保存 AI 助手 API 配置（改内存 → 存 cfg → 广播 AiSettingsChanged 即时应用）。
		/// provider："ollama" / "openai_compat"；端点/模型允许空值（未配置状态，玩家自行填写）。</summary>
		public void SetAiSettings(string provider, string endpoint, string apiKey, string model)
		{
			_aiProvider = string.IsNullOrWhiteSpace(provider) ? DefaultAiProvider : provider;
			_aiEndpoint = endpoint ?? string.Empty;
			_aiApiKey = apiKey ?? string.Empty;
			_aiModel = model ?? string.Empty;
			SaveSettings();
			EmitSignal(SignalName.AiSettingsChanged);
		}

		/// <summary>启用/停用 AI 助手（存 cfg + 广播 AiSettingsChanged）。</summary>
		public void SetAiEnabled(bool enabled)
		{
			if (_aiEnabled == enabled) return;
			_aiEnabled = enabled;
			SaveSettings();
			EmitSignal(SignalName.AiSettingsChanged);
		}

		private WindowPreset GetDefaultPreset()
		{
			return _presets[0];
		}

		private int FindPresetIndex(string presetId)
		{
			for (int i = 0; i < _presets.Length; i++)
			{
				if (_presets[i].Id == presetId)
				{
					return i;
				}
			}
			return -1;
		}

		public int GetPresetIndex(string presetId)
		{
			var index = FindPresetIndex(presetId);
			return index >= 0 ? index : 0;
		}

		public WindowPreset GetPresetByIndex(int index)
		{
			if (index < 0 || index >= _presets.Length)
			{
				return GetDefaultPreset();
			}
			return _presets[index];
		}

		private WindowPreset GetPresetById(string presetId)
		{
			var index = FindPresetIndex(presetId);
			return index >= 0 ? _presets[index] : GetDefaultPreset();
		}

		private void LoadSettings()
		{
			var config = new ConfigFile();
			var result = config.Load(ConfigPath);

			if (result == Error.Ok)
			{
				_currentPresetId = (string)config.GetValue(WindowSection, PresetKey, _currentPresetId);
				_crtEnabled = (bool)config.GetValue(WindowSection, CrtKey, false);
				_aiProvider = (string)config.GetValue(AiSection, AiProviderKey, DefaultAiProvider);
				_aiEndpoint = (string)config.GetValue(AiSection, AiEndpointKey, DefaultAiEndpoint);
				_aiApiKey = (string)config.GetValue(AiSection, AiApiKeyKey, string.Empty);
				_aiModel = (string)config.GetValue(AiSection, AiModelKey, DefaultAiModel);
				_aiEnabled = (bool)config.GetValue(AiSection, AiEnabledKey, false);
			}
			else
			{
				GD.PushWarning($"GameSettingsManager: 无法加载配置文件 ({ConfigPath})，使用默认窗口模式。错误: {result}");
				SaveSettings();
			}
		}

		private void SaveSettings()
		{
			var config = new ConfigFile();
			config.SetValue(WindowSection, PresetKey, _currentPresetId);
			config.SetValue(WindowSection, CrtKey, _crtEnabled);
			config.SetValue(AiSection, AiProviderKey, _aiProvider);
			config.SetValue(AiSection, AiEndpointKey, _aiEndpoint);
			config.SetValue(AiSection, AiApiKeyKey, _aiApiKey);
			config.SetValue(AiSection, AiModelKey, _aiModel);
			config.SetValue(AiSection, AiEnabledKey, _aiEnabled);

			var err = config.Save(ConfigPath);
			if (err != Error.Ok)
			{
				GD.PushWarning($"GameSettingsManager: 保存配置失败 ({err})，路径: {ConfigPath}");
			}
		}

		private void CenterWindow()
		{
			var screenSize = DisplayServer.ScreenGetSize();
			var windowSize = DisplayServer.WindowGetSize();
			DisplayServer.WindowSetPosition((screenSize - windowSize) / 2);
		}

		private void ApplyProjectSettings(WindowPreset preset)
		{
			ProjectSettings.SetSetting(
				"display/window/size/mode",
				(preset.Mode == DisplayServer.WindowMode.ExclusiveFullscreen || preset.Mode == DisplayServer.WindowMode.Fullscreen) ? 2 : 0
			);
			ProjectSettings.SetSetting("display/window/size/viewport_width", preset.Size.X);
			ProjectSettings.SetSetting("display/window/size/viewport_height", preset.Size.Y);
			ProjectSettings.SetSetting("display/window/size/initial_position_type", 2);
			ProjectSettings.SetSetting("display/window/size/resizable", true);
		}

		public readonly record struct WindowPreset(string Id, string DisplayName, DisplayServer.WindowMode Mode, Vector2I Size);
	}
}

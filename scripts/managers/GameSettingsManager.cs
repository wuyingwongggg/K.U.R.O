using Godot;
using System;
using System.Collections.Generic;

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
		/// <summary>按键绑定变化时触发（改键 UI 刷新显示）。</summary>
		[Signal] public delegate void InputBindingsChangedEventHandler();

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
		private const string InputSection = "Input";
		private const string HoldThresholdKey = "HoldThreshold";

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
		// 按键绑定：action → keycode int（正数=键盘 physical_keycode，负数=鼠标键）
		private readonly Dictionary<string, int> _inputBindings = new();
		// 长按触发标志：action → 是否通过长按触发（同键与其他动作形成长短按分流）
		private readonly Dictionary<string, bool> _longPressActions = new();
		private float _holdThresholdSeconds = 0.35f;

		public WindowPreset CurrentPreset => GetPresetById(_currentPresetId);
		public WindowPreset[] Presets => _presets;
		public bool CrtEnabled => _crtEnabled;
		public string AiProvider => _aiProvider;
		public string AiEndpoint => _aiEndpoint;
		public string AiApiKey => _aiApiKey;
		public string AiModel => _aiModel;
		/// <summary>AI 助手是否启用（默认关——玩家未配置 API 时 P2 纯规则模式，不发 LLM 请求）。</summary>
		public bool AiEnabled => _aiEnabled;
		/// <summary>已自定义的按键绑定（action → keycode；未自定义的动作用 project.godot 默认）。</summary>
		public IReadOnlyDictionary<string, int> InputBindings => _inputBindings;
		/// <summary>长按触发标志（action → 是否长按触发；take_up 恒 true）。</summary>
		public IReadOnlyDictionary<string, bool> LongPressActions => _longPressActions;
		/// <summary>长按判定阈值（秒）：短按/长按（如拾取/放置）的分界时长。</summary>
		public float HoldThresholdSeconds => _holdThresholdSeconds;

		/// <summary>动作是否通过长按触发（配置优先，未配置时 place 默认长按——放置=长按，拾取=短按）。</summary>
		public bool IsActionLongPress(string action)
		{
			if (_longPressActions.TryGetValue(action, out bool lp)) return lp;
			return action == "place";
		}

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

		/// <summary>设置动作是否长按触发：存 cfg + 广播 InputBindingsChanged（改键 UI/仲裁器刷新）。
		/// 同键多个动作时，勾选长按的动作在阈值时触发，同键其他动作短按触发（松开确认）。</summary>
		public void SetActionLongPress(string action, bool isLongPress)
		{
			if (action == "place")
			{
				_longPressActions[action] = true; // place 恒长按（放置=长按，与拾取短按同键时长短按分流）
				SaveSettings();
				EmitSignal(SignalName.InputBindingsChanged);
				return;
			}

			if (isLongPress)
			{
				_longPressActions[action] = true;
			}
			else
			{
				_longPressActions.Remove(action);
			}
			SaveSettings();
			EmitSignal(SignalName.InputBindingsChanged);
		}

		/// <summary>设置长按判定阈值（秒）：存 cfg + 广播 InputBindingsChanged（改键 UI 同步刷新）。</summary>
		public void SetHoldThresholdSeconds(float seconds)
		{
			_holdThresholdSeconds = Mathf.Clamp(seconds, 0.1f, 1f);
			SaveSettings();
			EmitSignal(SignalName.InputBindingsChanged);
		}

		/// <summary>获取动作当前绑定的物理键（自定义优先，否则回退 InputMap 默认首个键盘事件；无返回 0）。</summary>
		public int GetActionKeycode(string action)
		{
			if (_inputBindings.TryGetValue(action, out int custom))
			{
				return custom;
			}

			foreach (var e in InputMap.ActionGetEvents(action))
			{
				if (e is InputEventKey keyEvent)
				{
					return (int)keyEvent.PhysicalKeycode;
				}
			}
			return 0;
		}

		/// <summary>设置动作按键绑定：改内存 → InputMap 即时应用 → 存 cfg → 广播 InputBindingsChanged。
		/// keycode = physical_keycode int（0 表示重置回默认）。</summary>
		public void SetActionBinding(string action, int keycode)
		{
			if (!InputMap.HasAction(action)) return;

			if (keycode <= 0)
			{
				_inputBindings.Remove(action);
			}
			else
			{
				_inputBindings[action] = keycode;
			}
			ApplyActionBinding(action, keycode);
			SaveSettings();
			EmitSignal(SignalName.InputBindingsChanged);
		}

		/// <summary>把自定义绑定应用到 InputMap。keycode 语义：正数 = 键盘 physical_keycode；
		/// 负数 = 鼠标键（-1 左键 / -2 右键 / -3 中键 / -4+ 侧键）；0 = 清空自定义回退默认。
		/// 先擦除该动作全部事件再添加新事件（避免键盘/鼠标事件并存）。</summary>
		private static void ApplyActionBinding(string action, int keycode)
		{
			InputMap.ActionEraseEvents(action);

			if (keycode > 0)
			{
				InputMap.ActionAddEvent(action, new InputEventKey
				{
					PhysicalKeycode = (Key)keycode,
					Pressed = true
				});
			}
			else if (keycode < 0)
			{
				InputMap.ActionAddEvent(action, new InputEventMouseButton
				{
					ButtonIndex = (MouseButton)(-keycode),
					Pressed = true
				});
			}
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
				_holdThresholdSeconds = (float)config.GetValue(InputSection, HoldThresholdKey, 0.35f);

				// 加载按键绑定并即时应用（覆盖 project.godot 默认）
				if (config.HasSection(InputSection))
				{
					_inputBindings.Clear();
					_longPressActions.Clear();
					foreach (string action in config.GetSectionKeys(InputSection))
					{
						if (action.StartsWith("LP_", System.StringComparison.Ordinal))
						{
							bool lp = (bool)config.GetValue(InputSection, action, false);
							_longPressActions[action["LP_".Length..]] = lp;
							continue;
						}

						int keycode = (int)config.GetValue(InputSection, action, 0);
						if (InputMap.HasAction(action))
						{
							_inputBindings[action] = keycode;
							ApplyActionBinding(action, keycode);
						}
					}
				}
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

			config.SetValue(InputSection, HoldThresholdKey, _holdThresholdSeconds);
			foreach (var pair in _inputBindings)
			{
				config.SetValue(InputSection, pair.Key, pair.Value);
			}
			foreach (var pair in _longPressActions)
			{
				config.SetValue(InputSection, $"LP_{pair.Key}", pair.Value);
			}

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

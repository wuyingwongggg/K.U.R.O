# 游戏内 AI 助手 API 配置（独立 AiSettingsMenu）

## Context

游戏是面向玩家的 demo，LLM 功能（P2/Yui 决策）使用外部 API（本地 Ollama 或 OpenAI 兼容平台）。
当前 API 配置（`UseOpenAICompat`/`Endpoint`/`ApiKey`/`DefaultModel`）是 P2.tscn 上 OllamaClient 节点的 Inspector 导出值——**玩家无法在游戏内修改**。
需求：**独立 AiSettingsMenu 场景**（从 SettingsMenu 按钮进入），玩家自定义平台/端点/密钥/模型，持久化保存并运行时生效。

**为什么独立界面**：ApiKey 是敏感信息、Endpoint 是长 URL——与音量/窗口等常规设置隔离（进阶设置）；空间充裕可放平台说明/示例端点/使用指引；未来可扩展测试连接等。SettingsMenu 的 MenuPanel 已满（800×700），塞 4 个 LineEdit 会拥挤。

## 现有模式（照抄）

- **SettingsMenu**（scenes/ui/menus/SettingsMenu.tscn + scripts/ui/SettingsMenu.cs）：分区 = `HSeparator + Label标题(20号字) + 项目Label(16号字) + 控件`，全部堆在 `MenuPanel/VBoxContainer`；脚本用 `[Export] 字段 + _Ready 里 GetNodeOrNull("MenuPanel/VBoxContainer/X")` 路径回退；信号用 Godot 原生 Connect 包装器（`ConnectButtonSignal`/`ConnectSliderSignal`/`ConnectOptionButtonSignal`，需新增 `ConnectLineEditSignal`）；**无保存按钮、全部改动即生效**
- **GameSettingsManager**（autoload，scripts/managers/GameSettingsManager.cs）：ConfigFile 持久化（`user://config/window_settings.cfg`）；`SetXxx` 模式 = 改内存 → `SaveSettings()` → 可选 EmitSignal
- **CRT 链路**：UI → `SetCrtEnabled` → autoload 保存 → 信号广播 → 消费者即时应用（本次同样用信号广播）
- **运行时找 OllamaClient**：`GetTree().Root.FindChild("OllamaClient", recursive: true, owned: false)`（项目惯例 owned:false）；**必须判空**——主菜单打开设置时 P2/OllamaClient 不存在

## 关键时序问题（必须解决）

主菜单改配置时 OllamaClient 不存在（P2 在战斗场景）。方案：**保存进 GameSettingsManager + OllamaClient `_Ready` 启动时从 GameSettingsManager 拉取覆盖导出值**——主菜单配置 → 进战斗场景自动生效；战斗内改配置 → `AiSettingsChanged` 信号广播即时生效。

模型名两处重复（`OllamaClient.DefaultModel` + `AiDecisionBridge.Model`，P2.tscn 都设了 "qwen3.5:latest"）——统一为单一来源（见 AiDecisionBridge 改动）。

## 实现

### 1. GameSettingsManager.cs（加 AI 配置段）

- 4 个属性：`AiProvider`（"ollama"/"openai_compat"）、`AiEndpoint`、`AiApiKey`、`AiModel`（含默认值：ollama / http://localhost:11434/api/generate / 空 / qwen3.5:latest）
- `SetAiSettings(provider, endpoint, apiKey, model)`：改内存 → SaveSettings → **EmitSignal(AiSettingsChanged)**
- `LoadSettings`/`SaveSettings` 加 section `"AI"`（同一 cfg 文件，key：AiProvider/AiEndpoint/AiApiKey/AiModel）
- 注释说明：ApiKey 明文存 user:// config（单机 demo 可接受）

### 2. OllamaGenerateClient.cs（启动拉取 + 信号订阅）

- `_Ready`：`ApplySettingsFromManager()`——读 GameSettingsManager 的 4 个值覆盖导出属性（留空 ApiKey = 无鉴权，合法）
- 订阅 `GameSettingsManager.AiSettingsChanged` 信号 → 重新 Apply（战斗内从设置菜单改时即时生效）
- `_ExitTree` 退订

### 3. AiSettingsMenu 新场景（scenes/ui/menus/AiSettingsMenu.tscn + scripts/ui/AiSettingsMenu.cs）

场景结构照抄 SettingsMenu 模板（Background + MenuPanel 800×700 + VBox + BackButton），内容：

```
Title ("AI 助手 API 设置")
HSeparator
说明 Label（多行小字：配置说明——本地 Ollama 无需密钥；OpenAI 兼容平台填平台 Key）
AIProviderLabel / AIProviderOption (OptionButton: "Ollama 原生"(ollama) / "OpenAI 兼容"(openai_compat))
AIEndpointLabel / AIEndpointInput (LineEdit，placeholder 提示本地 Ollama 默认地址)
AIApiKeyLabel / AIApiKeyInput (LineEdit, secret=true 掩码)
AIModelLabel / AIModelInput (LineEdit)
HSeparator
平台示例说明 Label（多行小字：Ollama=http://localhost:11434/api/generate；OpenAI 兼容示例端点）
BackButton ("返回")
```

AiSettingsMenu.cs（照抄 SettingsMenu.cs 模式）：
- 4 个控件 `[Export]` + GetNodeOrNull 回退；`ConnectLineEditSignal` 新增（TextChanged）
- `_Ready` 恢复显示：`GameSettingsManager.Instance` 的 4 个值写入控件（suppress 标志防递归）
- handler：ItemSelected / TextChanged → `GameSettingsManager.Instance.SetAiSettings(...)`（即时保存 + 广播 AiSettingsChanged）
- Back → 关闭自己（Hide/QueueFree），SettingsMenu 一直在下层垫着

### 4. 入口接线（SettingsMenu + UIManager）

- SettingsMenu.tscn 加一个 Button "AI 助手 API 设置"（照抄现有按钮模式）
- SettingsMenu.cs：按钮 Pressed → `UIManager.Instance.LoadAiSettingsMenu()`（叠加在 MenuLayer，SettingsMenu 不隐藏）
- UIManager.cs：`LoadAiSettingsMenu()` 照抄 `LoadSettingsMenu()`（`LoadUI<AiSettingsMenu>(AI_SETTINGS_MENU_PATH, UILayer.Menu, "AiSettingsMenu")`）

### 5. AiDecisionBridge.cs（模型单一来源）

- `Model` 不再单独配置——P2.tscn 的 AiDecisionBridge.Model 清空，`GenerateAsync` 回退 `OllamaClient.DefaultModel`（已实现），模型名唯一来源 = OllamaClient（由 GameSettingsManager 驱动）

## 涉及文件

| 文件 | 改动 |
|---|---|
| scripts/managers/GameSettingsManager.cs | AI 配置属性 + SetAiSettings + cfg 读写 + 信号 |
| scripts/systems/ai/OllamaGenerateClient.cs | _Ready 拉取 + 订阅信号 + _ExitTree 退订 |
| scenes/ui/menus/AiSettingsMenu.tscn（新） | 独立 API 设置界面（照抄 SettingsMenu 模板） |
| scripts/ui/AiSettingsMenu.cs（新） | 4 控件绑定 + 恢复 + Set 处理 + Back |
| scenes/ui/menus/SettingsMenu.tscn | 加"AI 助手 API 设置"入口按钮 |
| scripts/ui/SettingsMenu.cs | 入口按钮接线 |
| scripts/managers/UIManager.cs | LoadAiSettingsMenu() |
| scenes/actors/characters/P2.tscn | AiDecisionBridge.Model 清空（单一来源） |

## 验证

1. `dotnet build` 通过
2. 主菜单 → 设置 → "AI 助手 API 设置" → 改平台/端点/Key/模型 → 返回 → 进战斗场景 → AI Output 面板确认新配置生效（`[Model]` 显示新模型名、请求打到新端点）
3. 战斗内暂停 → 设置 → AI 设置改配置 → 立即生效（下次 LLM 请求用新值）
4. 重启游戏 → 配置保留（user://config 读取）
5. 回归：默认配置（不改设置）= 现有 Ollama 本地行为不变
6. 本地 OpenAI 兼容端点实测：Endpoint 填 `http://localhost:11434/v1/chat/completions` + Provider=openai_compat → 请求正常（此前已验证该端点）

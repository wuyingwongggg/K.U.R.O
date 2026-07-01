# InputHoldTracker — 按键长短按识别系统

独立的按键时长追踪器，注册即用，区分短按/长按，每个 action 独立追踪，阈值可单独配置。

## 文件

| 文件 | 说明 |
|------|------|
| `scripts/systems/input/InputHoldTracker.cs` | 核心追踪器类 |
| `scripts/actors/heroes/SamplePlayer.cs` | 集成入口：`_Process` 驱动 + 公共查询方法 |
| `scripts/actors/heroes/states/PlayerState.cs` | 状态基类暴露的查询方法 |

## 快速使用

### 1. 注册 action

在 `SamplePlayer._Ready()` 中注册需要追踪的按键：

```csharp
_holdTracker.Register("run", longPressThreshold: 0.4f);
_holdTracker.Register("attack", longPressThreshold: 0.6f);
```

### 2. 在 State 中查询

所有 `PlayerState` 子类可直接调用：

```csharp
// 短按 — 松开帧触发，时长 < 阈值  (Edge-triggered)
WasActionShortPressed("run")

// 长按持续中 — 超过阈值后每帧为 true  (Level-triggered)
IsActionLongPressHeld("run")

// 长按刚触发 — 仅在达到阈值那帧为 true  (Edge-triggered)
WasActionLongPressTriggered("run")

// 当前按住时长（秒）
GetActionHoldDuration("run")
```

## 三种检测模式

| 方法 | 触发时机 | 适用场景 |
|------|----------|----------|
| `WasShortPressed` | 松开时 + 时长 < 阈值 | 闪避、取消、轻击 |
| `WasLongPressTriggered` | 时长达到阈值的瞬间 | 蓄力完成、长按菜单 |
| `IsLongPressHeld` | 超过阈值后每帧 | 奔跑、持续蓄力 |

## 已有绑定

| Action | 阈值 | 短按效果 | 长按效果 |
|--------|------|----------|----------|
| `run` (Shift) | 0.4s | 闪避 (DodgeSpeed=1200) | 奔跑 |

## 添加新按键

只需两步：

1. `SamplePlayer._Ready()` 中加一行：
   ```csharp
   _holdTracker.Register("your_action", longPressThreshold: 0.5f);
   ```

2. 对应 State 的 `PhysicsUpdate` 中查询：
   ```csharp
   if (WasActionShortPressed("your_action"))
   {
       // 短按逻辑
   }
   ```

## 设计要点

- **按需追踪**：只有注册过的 action 才被追踪，零开销
- **AI 兼容**：AI 覆写模式下 `GetControlledMovementInput()` 等仍然正常工作，`InputHoldTracker` 追踪的是物理输入不冲突
- **未注册安全**：查询未注册的 action 永远返回 `false`
- **不入侵现有代码**：原有 `IsActionPressed` / `IsActionJustPressed` 完全不受影响

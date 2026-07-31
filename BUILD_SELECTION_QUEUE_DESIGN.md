# N选1 取消队列 + "加号" UI 设计

## Context

当前 N选1 按 ESC 后窗口保留，暂停菜单覆盖在上层（P1 已实施）。但多次按 ESC 取消选择后，玩家可能积累多组未处理的卡牌选项。P2 在 P1 基础上增加队列存储和召回机制。

## 核心流程

```
TriggerSelection → PauseCount=1 → N选1 窗口打开
  → ESC → 设置菜单 Layer 3 覆盖 → PauseCount=2
  → 但窗口仍在 Layer 2，玩家可在设置菜单关闭后继续选择

P2 增强：
  → ESC → N选1 的 _options 存入队列 → CloseWindow(取消)
  → 屏幕右上角 "+N" 按钮出现（N = 队列积压数量）
  → 玩家随时点击 "+" → 从队列取出最早的 _options → 重新 ShowWindow
```

## 数据结构

```
BuildSelectionManager:
  Queue<List<BuildEffectDefinition>> _deferredSelections = new();
  // 新触发优先队列还是先进先出？ → FIFO，先取消的先补选
```

## UI 组件

**DeferredSelectionButton** (`scenes/ui/components/DeferredSelectionButton.tscn`)
- 小圆形/方形按钮，固定在屏幕右上角
- 显示队列积压数量（如 "+3"）
- 队列为空时隐藏
- 点击出队并打开 N选1 窗口
- CanvasLayer = 2（和 N选1 同级），设置菜单（Layer 3）覆盖时隐藏

## 行为规则

| 场景 | 行为 |
|---|---|
| 新分数触发 + 队列为空 | 直接打开 N选1（现有逻辑） |
| 新分数触发 + 队列非空 | 新 _options 追加到队尾，不打断当前选择 |
| ESC 取消当前 N选1 | _options 入队尾，窗口关闭，PauseCount-1 |
| 正常选择卡牌 | 正常关闭，队列不受影响 |
| 点击 "+" 按钮 | 出队，打开 N选1，队列数-1 |
| 队列全部处理完毕 | "+" 按钮隐藏 |

## 涉及文件

| 文件 | 改动 |
|---|---|
| `scripts/managers/BuildSelectionManager.cs` | 队列存储、出队入队、新触发等待 |
| `scenes/ui/components/DeferredSelectionButton.tscn` | 新建，小按钮场景 |
| `scripts/ui/DeferredSelectionButton.cs` | 新建，按钮逻辑 |

## 风险评估

- 跨场景队列持久化：BuildSelectionManager 是 autoload，队列天生跨场景
- 队列中 _options 引用的 EffectDefinition 在场景切换后是否有效？EffectDefinition 是 Resource，自动内存管理
- 按钮位置可能与 HUD 重叠：放在右上角，和 BattleHUD 坐标错开

## 实施状态

**暂缓。** 待构筑系统和卡牌效果全部完善后再实施。当前阶段（P1）玩家按 ESC 后游戏设置界面（Layer 3）覆盖在三选一窗口（Layer 2）上层，关闭设置后继续选择，选项不丢失。

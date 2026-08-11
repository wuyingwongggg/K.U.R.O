# P2（2P 伴随角色）架构文档

> 本文档描述 P2 的当前场景结构、各节点职责、数据流，以及未来添加新台词/新动作时的扩展指南。

## 1. 概述

P2 是一个 AI 伴随角色（Spine 骨骼 `Enemy_A1_yui`），独立场景挂载在 Stage 场景的 World 下（与玩家 MainCharacter 是兄弟节点）。核心能力：

- **移动**：自由游走（环形区域）+ 跟随模式（双模式切换）+ 射线碰撞（自由游走时）
- **动画**：状态机驱动 5 个 Spine 动画（walk/move/action/hit/stun）
- **决策**：LLM（本地 Ollama）优先 + 规则脚本兜底
- **动作**：治疗（食物）、护盾、气泡提示、武器拾取拖拽、移动
- **受击**：可被敌人攻击（碰撞体 + HitArea + TakeDamage）

## 2. 场景结构（scenes/actors/characters/P2.tscn）

```
CharacterBody2D (P2CompanionController)           ← 总控制器（移动/朝向/动画/受击/气泡）
├── Anchor_Hint (Marker2D)                       ← 气泡锚点（Dialogic 气泡显示位置）
├── Anchor_ItemDrop (Marker2D)                   ← 武器放置锚点（拖拽武器生成位置）
├── Shadow (Sprite2D)                            ← 地面阴影
├── OutlineLayer (Node2D)                        ← 描边层（4 个 OutlineSpineSprite 同骨骼）
│   └── OutlineSpineSprite_Left/Right/Up/Down
├── SpineSprite (SpineController.gd)             ← 主精灵（Spine 播放接口）
│   └── SpineBoneNode (bone5)                    ← 武器挂载骨骼
├── HintBubble (P2HintBubble.tscn)               ← 世界空间气泡（预留，实际气泡走 Dialogic）
├── AI_Brain (P2SupportBrain)                    ← 决策层（规则评估 + LLM 请求）
├── AI_Executor (P2SupportExecutor)              ← 执行层（意图白名单 → 动作）
├── AI_DecisionBridge (P2SupportDecisionBridge)  ← LLM 意图 → 本地意图映射 + 校验
├── AI_WeaponCarrier (P2WeaponCarrier)           ← 武器搬运（拾取/骨骼挂载/拖回/放置）
├── HitArea (Area2D)                             ← 受击判定区
│   └── CollisionShape2D (RectangleShape2D)
├── StateMachine (StateMachine.cs)               ← 行为状态机（动画驱动）
│   ├── Idle (P2IdleState)    → walk 循环
│   ├── Walk (P2WalkState)    → walk/move（跟随模式播 move）
│   ├── Action (P2ActionState) → action（决策动作播放）
│   ├── Hit (P2HitState)      → hit（受击硬直）
│   └── Stun (P2StunState)    → stun（预留）
├── DebugPanel (P2DebugPanel.tscn)               ← 调试面板（隐藏）
└── LoadoutPanel (P2LoadoutPanel.tscn)           ← 技能/装备面板（隐藏）
```

## 3. 各节点职责详解

### 3.1 P2CompanionController（根节点脚本）

**移动系统**（`_PhysicsProcess`，位移唯一驱动者，状态机不参与位置计算）：
- **双模式**：自由模式（`FreeRoam`，环形区域游走）与跟随模式（`Follow`，超 `MoveRangeMax` 后接近玩家）
- **移动目标四层优先级**：显式决策目标 → 环形范围约束 → 常态游走（定时随机点）→ 跟随点
- **速度区分**：`FreeRoamSpeed`（游走）< `FollowSpeed`（跟随，保证追上玩家）
- **跟随超时**：`FollowMaxDuration` 后强制退出跟随
- **边界碰撞**：自由游走时向移动方向发射线（`ApplyRayCollision`），命中墙体贴墙停住；拾取/跟随/action 期间不检测（可穿墙执行任务）

**朝向**：统一跟随移动 X 轴（Scale.X 翻转主精灵 + 描边层），不跟随玩家朝向

**受击**：`TakeDamage`（DamageDispatcher 通过 `HasMethod` 命中）→ 扣 `ReportedCurrentHp` + 免疫窗口 + 切 Hit 状态

**气泡**：`PushHint`（预定义 dtl 文本）/ `PushHintDirect`（动态文本），队列 + 过场抑制 + 隐藏取消

### 3.2 AI_Brain（决策层）

- 每 `EvaluateIntervalSeconds(0.5s)` 评估一次
- **LLM 优先**（`EnableAiDecisionBridge`）：每 1s 请求本地 Ollama → 决策经映射+校验
- **规则兜底**（按优先级）：
  1. `low_hp_under_attack`（玩家 ≤35% HP 且被攻击 → 治疗）
  2. `enemy_too_close`（敌人 ≤320px → 护盾）
  3. `weapon_nearby`（CarryRange 内有武器 → 拾取）
  4. `quiet_scene_pickup`（无敌人 → 提示）
- **冷却**：全部导出（`HealRuleCooldownSeconds`/`ShieldRuleCooldownSeconds`/`WeaponFetchCooldownSeconds`/`QuietSceneReminderSeconds`）
- **武器拾取 CD 特殊**：决策发出时设置的 CD 会被"放置完成"信号（`AI_WeaponCarrier.WeaponPlaced`）覆盖——实际语义 = 放置完成 + `WeaponFetchCooldownSeconds`

### 3.3 AI_Executor（执行层）

意图白名单（`TryExecute`）：

| 意图 | 动作 |
|---|---|
| `trigger_support_skill` | 护盾技能（ApplyShield 24 点/6s）+ 播 action 动画 |
| `use_support_item` | 食物治疗（ApplyHeal 18 点 × 装备倍率）+ 播 action 动画 |
| `show_hint` / `show_hint_raw` | Dialogic 气泡 |
| `move_to` | 移动决策（`away_enemy` / `offset:x:y`） |
| `fetch_weapon` | 武器拾取（交给 AI_WeaponCarrier） |
| `hold` | 无动作 |

- 治疗/护盾执行成功 → `TriggerAction()`（两阶段：切跟随接近玩家 → 距离 ≤ `FollowRangeMin` → 播 action 动画）
- 技能/物品各自 CD（导出）

### 3.4 AI_DecisionBridge（LLM 映射）

| LLM 意图 | 本地意图 |
|---|---|
| `retreat` / `reposition` | `move_to`（远离敌人） |
| `loot` | `fetch_weapon` |
| `use_skill` | `trigger_support_skill` |
| `heal` / `use_item` / `use_support_item` | `use_support_item` |
| `attack` / `switch_weapon` | `hold` |

映射后过本地校验（满血禁治疗、无敌人禁技能）。

### 3.5 AI_WeaponCarrier（武器搬运）

状态机：`Idle → GoingToWeapon → Returning → 放置 → Idle`
- 前往武器（`SetMoveTarget`）→ 拾取（读实体 ItemDefinition，实体 QueueFree）→ 骨骼挂载（HoldScenePath 实例 / Icon 回退，挂 SpineBoneNode）→ 拖回（每帧追踪玩家）→ 放置（`Anchor_ItemDrop` 位置生成世界武器实体）→ 发 `WeaponPlaced` 信号
- 拾取期间 `IgnoreMoveRange = true`（忽略范围约束 + 空气墙，可穿墙执行）
- 检测：`world_items` 组，两类实体（WorldItemEntity / RigidBodyWorldItemEntity）都支持；`IsThrowWeapon` 或 `Category=="Weapon"`；范围 `[CarryRangeMin, min(CarryRangeMax, MoveRangeMax)]`；取最远

### 3.6 StateMachine（状态机）

- 复用项目通用 StateMachine（`Initialize(Node)` 签名，P2 状态用 `Owner` 访问控制器）
- 状态只做**行为/动画层**，不计算位移；`UpdateMotionState` 只在 Walk/Idle 间切换，不打断 Action/Hit/Stun

## 4. 数据流总览

```
LLM（每 1s Ollama）→ AI_DecisionBridge 映射+校验 ─┐
规则（每 0.5s，AI_Brain）─────────────────────────┤
                                                ▼
                                    AI_Executor.TryExecute（白名单）
                                                │
                    ┌───────────────┬───────────┴──────────────┐
                    ▼               ▼                          ▼
              触发 action     PushHint 气泡              SetMoveTarget / 拾取
            （两阶段接近玩家）  （Dialogic）            （Controller 移动 → 状态机动画）
```

## 5. 命名规范

- `Anchor_*`：Marker2D 锚点（Anchor_Hint / Anchor_ItemDrop）
- `AI_*`：决策/执行组件（AI_Brain / AI_Executor / AI_DecisionBridge / AI_WeaponCarrier）
- 视觉（Shadow/OutlineLayer/SpineSprite/SpineBoneNode）、StateMachine 状态、UI（DebugPanel/LoadoutPanel）保持原名
- 脚本内相对 NodePath 约定：组件在 P2 子节点下引用兄弟用 `../名字`；引用 P2 根用 `..`；引用玩家用 `../MainCharacter`（Stage 中兄弟）

## 6. 未来扩展指南

### 6.1 添加新台词（对话）

**方式 A：预定义文本（推荐）**
1. 在 `dialogic/timeline/p2_hint.dtl` 添加 label（如 `"encourage"`）
2. 代码调用 `P2.PushHint("encourage")`（Brain 规则 / Executor 动作中）
3. 受 `GlobalHintCooldownSeconds` + 队列上限（`MaxHintQueueSize`）约束

**方式 B：动态文本**
1. `P2.PushHintDirect("任意中文文本")`——通过 Dialogic 变量 `p2_hint_text` 注入
2. 无需改 dtl，适合 LLM 个性台词

**兼容要点**：气泡锚点已配置（`Anchor_Hint`），新台词零场景改动。

### 6.2 添加新动作（行为）

**标准五步**（现有 fetch_weapon / move_to 即此模式）：
1. **`SupportDecision.cs`**：新增工厂方法（如 `SupportDecision.Defend(...)`）——设置 `Intent` 字符串
2. **`AI_Executor.cs`**：白名单加 `case "defend"` → 执行方法（或转发给专用组件）
3. **可选：新组件**——动作复杂时新建 `AI_XxxCarrier` 类（挂 `AI_` 前缀节点），Executor 转发调用
4. **`AI_Brain.cs`**：加规则分支（带条件 + 导出冷却）
5. **`AI_DecisionBridge.cs`**：如需 LLM 触发，加意图映射

**兼容要点**：
- 新组件相对路径引用：兄弟用 `../AI_Xxx`；Controller 用 `..`；玩家用 `../MainCharacter`
- 需要播动作动画 → `P2.TriggerAction()`（两阶段接近玩家）
- 需要移动 → `P2.SetMoveTarget(...)`；需要穿墙执行 → 配合 `IgnoreMoveRange`（在 Carrier 流程中管理）

### 6.3 添加新状态/动画

1. Spine 骨骼加动画 → `P2State` 子类（挂 StateMachine 子节点，命名如 `Defend`）
2. `UpdateMotionState` 目前只认 Walk/Idle——新状态若需移动动画，在对应状态内处理（如 P2WalkState 按模式切 walk/move 的先例）
3. 新状态不会被 `UpdateMotionState` 打断（Action/Hit/Stun 同理）

### 6.4 兼容性注意事项

| 项 | 注意 |
|---|---|
| **相对路径** | 节点重命名需同步所有脚本默认 NodePath 与场景配置（5 处脚本 + 场景） |
| **速度参数** | 自由游走 `FreeRoamSpeed` < 跟随 `FollowSpeed`（保证追上玩家） |
| **CD** | 新规则冷却用导出参数（如 `XxxCooldownSeconds`），不写死 |
| **决策优先级** | 新规则插入 Evaluate 顺序时注意 return 短路（生存 > 防御 > 资源 > 提示） |
| **边界/范围** | 新动作需要穿墙时管理 `IgnoreMoveRange`；自由游走才被空气墙拦 |
| **实体类型** | 涉及世界物品时同时支持 WorldItemEntity 与 RigidBodyWorldItemEntity（无继承，需分别处理） |

## 7. 已知预留/未接

- **Stun 状态**：就位无触发源（未来接眩晕/受控效果）
- **P2HintBubble**：独立场景存在但无调用方（气泡走 Dialogic）
- **P2 自愈**：P2 受击扣 HP 但无治疗自身的手段（治疗目标是玩家）

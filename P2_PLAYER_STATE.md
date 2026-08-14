# P2 玩家状态感知与死亡行为需求分析

> 本文档分析 P2（2P 伴随角色）对玩家状态的检测能力现状，以及"玩家死亡感知"需求
> （决策抑制 / action 防御 / 对话接口 / 守尸行为）的实施方案。
> 相关架构总览见 [P2_ARCHITECTURE.md](./P2_ARCHITECTURE.md)。
>
> **实施状态标记**：✅ 已实施 / 🆕 文档未规划的新增 / 无标记 = 未实施（待办）。

## 1. 背景与问题

P2 目前对玩家状态只感知血量与距离，**不感知玩家死亡**：

- 玩家进入 Dying/Dead 后，P2 仍按正常逻辑执行——治疗（`ApplyHeal` 走完全流程：飘字/特效/气泡，但 `RestoreHealth` 已被死亡保护静默拦截，血量不动）、护盾（`ApplyShield` 上盾无意义）、ai_chatter 等。
- 玩家死亡流程：`Die()` → `PlayerDyingState`（约 1 秒倒地动画）→ `PlayerDeadState`（`UIManager.LoadGameOverScreen` + `FinalizeDeath`）→ `SamplePlayer.OnDeathFinalized` → **同帧 `ReloadCurrentScene()`**（玩家节点从不 QueueFree，场景整树重载）。

## 2. 现状架构速览

| 层 | 组件 | 职责 |
|---|---|---|
| 决策 | `P2SupportBrain`（AI_Brain） | 每 0.5s `Evaluate`，if 链按优先级（生存 > 防御 > 资源 > 提示） |
| 执行 | `P2SupportExecutor`（AI_Executor） | 意图白名单 `TryExecute`（trigger_support_skill / move_to / fetch_weapon / show_hint / hold） |
| 对话 | `P2DialogueController`（AI_Dialogue） | `Speak(P2DialogueEvent)` → `PushHint(key)` → dtl label 自动变体 |
| 移动 | `P2CompanionController` | FreeRoam/Follow 双模式 + 距离带 |
| 搬运 | `P2WeaponCarrier`（AI_WeaponCarrier） | 武器拾取/拖拽/放置 |

## 3. 玩家状态检测现状（快照矩阵）

`GameStateProvider.CaptureGameState()`（每 0.5s 由 Brain 调用）已捕获：

| 检测项 | GameState 字段 | Brain 是否使用 |
|---|---|---|
| 血量比例 | `PlayerHp / PlayerMaxHp` | ✅（heal ≤50% / 敌人威胁） |
| 被攻击中 | `PlayerUnderAttack` | 已移除（食物路径废弃） |
| **状态机状态名** | `PlayerStateName` | ❌ **未使用**（死亡检测的现成通道） |
| 场上敌人 | `AliveEnemyCount / NearestEnemyDistance / AverageEnemyDistance` | ✅ |
| 背包 | `BackpackItemCount / BackpackOccupiedSlots` | ❌ 计数；语义列表见下 ✅ |
| **当前选中武器** | `SelectedQuickBarSlotIndex / SelectedQuickBarItemId / SelectedQuickBarItemName / QuickBarSlots` | ❌ 规则未消费；LLM 经字典消费 ✅ |
| 同伴 | `Companions`（P2 自身 HP） | ❌ |
| 场上武器掉落 | `WeaponNearby()`（Carrier 独立查询，非快照） | ✅ |

**✅ 武器语义扩展（已实施，LLM 经 `ToAiInputDictionary`/`ToAiPromptText` 自动消费）**：

- `QuickBarSlotState` 每槽：`category`、`description`（80 字符截断）、`attack_power`、`skill_name`、`skill_description`（首个武器技能）、`is_throw_weapon`、**`throw_cooldown_remaining`**（投掷 CD 秒数，0=不在冷却）、**`battery_charge`/`battery_max`**（-1=无电池系统，经 `WeaponBatteryManager` 按技能 SkillId 查询，实时电量）
- `BackpackSlotState` 列表：`item_id/name/category/quantity/is_weapon/throw_cooldown_remaining`（单次遍历产出计数+列表）
- `FlyingThrowWeaponCount`：飞行中的投掷武器数（`ReservedQuickBarSlots.Count`——投掷期间槽位为空占位、CD 尚未计时）
- 兼容性：**零硬编码武器 ID**——新武器（普通/带技能/电池/投掷）全部数据驱动自动进快照；已知缺口：多技能武器只暴露首个技能、属性只读 attack_power

**结论**：血量归零检测已实现（`PlayerStateName == "Dying"/"Dead"` 或 `PlayerHp <= 0` 的死亡规则已加，见第 4 章）。

## 4. 需求一：玩家死亡感知 ✅ 已实施

> **实施总结**：死亡检测 = Brain 快照规则（`PlayerStateName in (Dying,Dead) || PlayerHp<=0`，最高优先级 return，抑制 LLM 桥/个性台词/全部支持规则）；
> 死亡台词 = 规则路径 dtl 文本（`player_dying_N` 随机变体，整个死亡窗口只说一次，**不依赖 LLM，有/无 LLM 接入均正常**），
> **抢占式播放**（`PushHintUrgent`：清空队列 + 打断当前气泡立即播放，解决"死亡窗口 1 秒 + 气泡排队"导致的台词被吞问题；
> 代际令牌 `_hintGeneration` 防止旧气泡自动结束计时器误杀抢占后的新气泡）；
> 执行层 = `ApplyHeal`/`ApplyShield` 显式 reject（`player_dead`）；
> 守尸 = Controller 死亡时唯一移动目标为玩家位置 + Carrier 取消搬运。
> 下方 4.1-4.6 为设计原文（实施按此落地）。

### 4.1 死亡时 P2 应停止什么

| 行为 | 现状 | 期望 |
|---|---|---|
| 治疗（ApplyHeal） | 走完全流程但被 `RestoreHealth` 拦截（静默 no-op） | 决策/执行层直接拒绝（reject） |
| 护盾（ApplyShield） | 正常上盾（无意义，`TakeDamage` 已有死亡守卫） | 拒绝 |
| ai_chatter / 拾取 / 移动 | 照常 | 可保留（守尸移动）或抑制 |
| 对话 | 无死亡台词 | 新增 `PlayerDying` 钩子 |

### 4.2 死亡检测机制

- **主通道（推荐）**：Brain `Evaluate` 最高优先级规则，读 `state.PlayerStateName`（`"Dying"` / `"Dead"`）或 `state.PlayerHp <= 0` → 死亡规则 `return`，抑制后续一切支持决策（需同时覆盖 AI 桥分支——死亡时 AI 决策也禁）。
- **注意**：`PlayerStateName` 来自 `StateMachine.CanTransitionTo` 硬约束（死亡后禁止切入非 Dying/Dead 状态），状态名稳定可轮询。

### 4.3 决策层（P2SupportBrain）

在 `Evaluate` 的 `PlayerMaxHp <= 0` 守卫之后、`heal_low_hp` 之前插入：

```
player_dying 规则（最高优先级，return）：
  state.PlayerStateName in ("Dying", "Dead") || state.PlayerHp <= 0
  → 触发死亡对话钩子（节流）+ return（抑制 heal/shield/fetch/chatter 等全部规则）
```

### 4.4 执行层防御（P2SupportExecutor）

防御性检查（决策层遗漏/时序竞态时兜底）：

- `ApplyHeal`（L632 附近，`_player == null` 检查旁）：`_player.IsDeathSequenceActive || _player.IsDead` → reject
- `ApplyShield`（L604 附近）：同上
- 注：`GameActor.RestoreHealth` 已有死亡保护（`IsDeathSequenceActive || IsDead` return），执行层防御的意义是**显式拒绝**（走 reject → Brain fallback），避免"成功流程但无效果"的虚假反馈。

### 4.5 对话接口（P2DialogueController + dtl）✅ 已实施（含抢占式）

- ✅ `P2DialogueEvent.PlayerDying` 枚举 + `Speak` case → **`PushHintUrgent("player_dying")`**（抢占式：清空队列 + 打断当前气泡立即播放，不排队）
- ✅ `dialogic/timeline/p2_hint.dtl` label `player_dying_1/2`（当前 2 条变体，`ResolveVariant` 自动随机；台词简短适配 1 秒死亡窗口）
- ✅ `PushHintUrgent` 实现细节：剧情 timeline 播放中不打断（保护剧情流程）；`_hintGeneration` 代际令牌——旧气泡的自动结束计时器在抢占后失效，避免误杀新气泡
- 命名与现有 `follow_started` / `shield_applied` 一致

### 4.6 守尸移动（可选，需死亡流程配合）

- **约束**：Dying 仅 1 秒，Dead 进入的同帧即场景重载——"守尸"（P2 停在玩家旁/播放专属动画）实际窗口只有 Dying 的 1 秒。
- 若要完整的守尸行为，需先改造死亡流程（延迟 `FinalizeDeath` / 不立即重载，给守尸与对话留时间）。
- 移动侧：P2 对玩家节点销毁的防御已完备（引用解析带 `IsInstanceValid` + 组回退 + null 早退），但玩家实际从不 QueueFree（场景重载兜底），不存在"玩家消失但 P2 存活"的窗口。

## 5. 需求二：更多玩家状态检测

### 5.1 快照型扩展（推荐主路径，与现有架构一致）

`GameState.cs` 加字段 + `GameStateProvider.CaptureGameState()` 填充 + Brain 规则消费。**LLM 的 AI 输入字典（`ToAiInputDictionary`）自动带上新字段**，Ollama 决策同步受益：

| 新字段 | 数据来源 |
|---|---|
| `WeaponItemCount`（背包武器数） | 背包按 `Category == "Weapon"` 过滤 |
| `PickedBuildEffectIds`（Build 卡牌 ID 列表） | `BuildSelectionManager.Instance`（`PickedEffectsChanged` 事件已有） |
| `LevelName`（当前关卡） | `GetTree().CurrentScene.Name` |
| `WorldItemCount / NearestDropDistance`（场上掉落） | `world_items` 组统计（与 Carrier 过滤口径对齐：`IsThrowWeapon \|\| Category=="Weapon"`） |

### 5.2 事件型扩展（对话钩子/即时反应）

新建 `P2PlayerEventMonitor` 组件（P2 子节点），订阅玩家事件，回调触发对话钩子/更新内部状态：

| 事件 | 订阅源 |
|---|---|
| 拾取完成 | `PlayerInventoryComponent.InventoryChanged` / `QuickBarSlotChanged` |
| 武器切换 | `QuickBarSlotChanged` / `ActiveBackpackSlotChanged` |
| Build 卡牌获得 | `BuildSelectionManager.PickedEffectsChanged` |
| 玩家死亡 | `HealthChanged`（到 0）或快照轮询 |

### 5.3 推荐组合

- **快照型**承载全部规则决策（卡牌效果、关卡、掉落、死亡守尸）
- **事件型**只做对话钩子与即时反应（玩家刚拾取/刚获得卡牌的台词）

## 6. 实施步骤（死亡感知）✅ 全部完成

1. ✅ `P2DialogueEvent.PlayerDying` 枚举 + `Speak` case + dtl label `player_dying_1/2`（台词简短适配 1 秒死亡窗口）+ **抢占式播放 `PushHintUrgent`**（清队列/打断当前气泡/代际令牌防旧计时器误杀）
2. ✅ `P2SupportBrain`：死亡规则（`Evaluate` 最顶部、最高优先级 return，抑制 LLM 桥 + 个性台词 + 全部支持规则）+ `DialogueControllerPath` 导出 + 死亡台词 once 标志
3. ✅ `P2SupportExecutor`：`ApplyHeal` / `ApplyShield` 死亡 reject（`rejectReason = "player is dying or dead"`）
4. ✅ 守尸：`P2CompanionController.ComputeMovementTarget` 死亡时唯一移动目标 = 玩家位置；`P2WeaponCarrier` 搬运中死亡 → `Cancel()`
5. ✅ `dotnet build` 通过（游戏内验证待做：死亡瞬间 P2 台词立即弹出 + 停止治疗/护盾 + 走向玩家）

## 7. 注意事项与已知约束

| 项 | 约束 |
|---|---|
| **Dying 仅 1 秒** | 死亡台词显示 2.2s 会被场景重载截断——台词简短化 + **抢占式播放已解决"排队被吞"问题**（台词立即弹出，1 秒窗口足够看到开头）；气泡完整显示仍受重载限制 |
| **AI 桥** | 死亡规则已放在 `Evaluate` 最顶部（`TryEmitAiDecision` 之前），LLM 决策不会穿透 ✅ |
| **护盾拦截** | 护盾全吸收不触发 `HealthChanged`（`DamageIntercepted` 阶段拦截），死亡前被盾挡的伤害不产生虚血 |
| **治疗防御链** | `RestoreHealth` 死亡保护（基类）→ `ApplyHeal` 显式 reject（执行层）→ Brain 死亡规则（决策层）三层兜底 ✅ |
| **场景重载** | 玩家死亡 = 场景重载，P2 一并重置，无需清理残留状态 |
| **已废弃路径** | 玩家背包食物治疗已废弃（清理完毕），治疗统一走技能路径 |
| **死亡台词抢占边界** | 剧情 timeline 播放中死亡：不打断剧情（保护性选择），死亡台词按普通路径被门控丢弃 |
| **边缘残留（已知未修）** | ① 死亡瞬间护盾恰好过期仍弹 `shield_expired`（Executor 护盾计时不感知死亡，1 秒窗口小概率）；② 死亡前已发出的两阶段 action 可能播一次动作动画（视觉残留，1 秒内消失） |

## 8. 需求三：外部大模型 API 的信息处理与情绪引导

> 现状：LLM 决策（本地 Ollama，`EnableAiDecisionBridge`）通过 `GameStateProvider.GetAiInputJson()/GetAiPromptText()` 获取状态快照，
> 经 `AiDecisionBridge` 映射意图 → 本地白名单校验 → Executor 执行。
> 本节分析：接入外部大模型后，如何把玩家状态**有引导地**提供给 API，让情绪反应符合 P2 人设（如玩家死亡时伤心而非幸灾乐祸）。

### 8.1 三层信息管线

**第 1 层：事实层（已有）**——`GameState` 快照转 JSON / Prompt 文本喂给 LLM。
问题：裸数字（`hp=0, state=Dying`）对模型没有情感引导，反应不可控。

**第 2 层：情境解读层（需加）**——把原始状态翻译成带情感含义的情境描述，而非裸数据
（距离分带 + 接近方向判定的设计已规划，见 8.6）：

```
[GameState]
玩家倒地濒死！(hp 0/150，state=Dying)
玩家正被敌人包围（3 个敌人，最近距离 120px）
```

死亡等稀有事件用**事件钩子触发**（非 0.5s 轮询），调用时附完整情境。

**第 3 层：人格层（system prompt，最关键）**——情绪由 persona 设定决定，不随调用随机：

```
[Persona]
你是 P2，一只猫娘伙伴，与玩家是亲密搭档。
性格：活泼、忠诚、略微傲娇。
玩家倒下时：你感到惊慌和难过，会焦急鼓励玩家"别倒下"。
禁忌：绝不嘲讽、绝不冷漠、绝不说风凉话。
```

### 8.2 情绪反应的决定因素：人设而非技术

| P2 设定 | 玩家死亡反应 |
|---|---|
| 亲密伙伴（默认） | 伤心、焦急、鼓励 |
| 傲娇/毒舌吐槽役 | 嘴上损两句但实际着急（"笨蛋！快起来！"） |
| 中立/冷漠 AI | 平淡陈述 |
| 敌对/反派 | 才可能幸灾乐祸（P2 非此定位） |

技术上：**情绪倾向写死在 system prompt 的 persona 里**，模型在该范围内发挥台词。

### 8.3 情绪引导的三种强度

| 方案 | 做法 | 可控性 |
|---|---|---|
| A. 自由发挥 | 只给 persona，让模型自然反应 | 低（可能摇摆/跑偏） |
| B. **情境指令（推荐）** | 死亡情境块内显式写"玩家死亡 → 你应伤心并鼓励，台词 1-2 句，≤20 字" | 中高 |
| C. 情绪量表 | P2 维护心情值（担忧度/好感度），随状态变化写入 prompt | 最高（人格连贯） |

推荐 **B + 轻微 C**：死亡等稀有事件用显式情境指令，日常状态用 persona + 情绪值保持连贯。

**✅ 已落地部分**（persona 驱动 + 台词多样性）：

- `AiDecisionBridge.PersonaSystemPrompt` 导出（P2 人设：活泼忠诚猫娘、俏皮傲娇、reason 用 P2 口吻一句话、每次换说法避免重复），请求时传入 Ollama `system` 字段
- `BuildPersonalityText` 直接用 LLM 生成的 reason（**代码前缀表已删除**，不再拼"我觉得可以主动压一下"等前缀）
- 台词多样性三重保障：`Temperature` 0.7（原 0.2 高确定性导致雷同）+ persona 换说法引导 + **5 条签名历史去重**（`_personalitySignatureHistory` 队列，与最近 5 条决策签名都不同才说）

### 8.4 落地要点

| 项 | 做法 |
|---|---|
| 触发时机 | 死亡瞬间事件钩子触发一次（`P2PlayerEventMonitor` 订阅 → 调 LLM），非轮询 |
| 输出约束 | 复用 `AiDecisionBridge` 意图白名单 + 台词长度限制（死亡台词走 `show_hint`/`PushHintDirect`） |
| 一致性 | 情绪状态持久化在 P2 组件（心情/关系变量），跨调用连贯 |
| 回退 | LLM 不可用时落规则台词（`player_dying_N` dtl label，见 4.5） |
| 情境注入点 | `GameStateProvider` 增补情境描述字段，或 LLM 调用前在 Prompt 组装处拼接 persona + 情境块 |

### 8.5 与现有代码的衔接

- `GameState.ToAiInputDictionary()/ToAiPromptText()` 是事实层出口——**情境描述与 persona 不进快照**，在 LLM 调用组装处拼接（避免污染 AI 决策字典的结构化数据）
- 死亡检测复用 4.2 的 `PlayerStateName` 快照通道 + 事件钩子触发 LLM
- 情绪量表（方案 C）可挂 `P2PlayerEventMonitor`：受击/治疗/拾取/死亡事件增减心情值

### 8.6 ✅ 距离与接近方向的情境解读（已实施）

**背景**：LLM 生成的 reason 用词与距离数据脱节——JSON 里只有裸像素数（`nearest_distance: 120`），
模型对"120px 是近是远"无直觉（不知道 AttackRange≈120、危险圈=320），会出现"敌人在远处"与事实矛盾的台词；
且**仅距离无法区分"玩家主动接近"（进攻语气）与"敌人接近"（威胁语气）**，战术含义相反。

**设计（代码侧确定性计算，不靠模型理解数字）**：

| 层 | 内容 |
|---|---|
| 距离分带 | `<200` 极近（贴脸）/ `<600` 近（危险范围）/ `<1200` 中等 / `≥1200` 远（阈值对齐游戏数值） |
| 接近方向判定 | ① Δd/dt 距离变化率（Provider 保存上次快照差分）② 玩家 `Velocity`·敌方向 > 阈值 → 玩家在接近 ③ 敌人 `Velocity`·玩家方向 > 阈值 → 敌人在接近 |
| 判定结论 | `enemy_approaching`（敌人来袭）/ `player_approaching`（玩家主动）/ `mutual`（对冲）/ `receding`（拉开）/ `static`（僵持）/ `none`（无敌人） |
| 新快照字段 | `nearest_enemy_distance_delta`（px/s，正=接近）、`approach_situation`（枚举字符串）——GameState + ToAiInputDictionary 的 enemies 段 |
| Prompt 注入 | `Situation: 最近敌人 320px（近——危险范围），敌人正在接近玩家` + 决策规范"reason 中的距离/接近描述必须与 Situation 一致，禁止矛盾说法" |

**预期效果**：

| 场景 | P2 台词（LLM 有锚点后） |
|---|---|
| 敌人冲向玩家 | "小心！敌人冲过来了喵！"（威胁语气） |
| 玩家冲脸敌人 | "冲呀！揍它喵！"（进攻语气） |
| 双方拉开 | "距离拉开了，安全~" |

**涉及文件（均已落地）**：`GameState.cs`（2 字段 + 字典/prompt 文本输出）、`GameStateProvider.cs`（`_lastNearestDistance` 差分状态 +
`ResolveEnemyMetrics` 返回最近敌人节点 + `ResolveApproachSituation` 速度投影判定，阈值常量 `ApproachVelocityThreshold=50`/`ClosingDeltaThreshold=30`）、
`OllamaGenerateClient.cs`（`BuildSituationText` 情境行 + `ClassifyDistanceBand` 分带 + 决策规范"reason 必须与 Situation 一致"）、本文档 8.1（情境解读层首块落地）。

## 9. 需求四：P2 如何调用外部 LLM API

### 9.1 现状：LLM 调用链路（已存在，本地 Ollama）

```
P2SupportBrain._Process（0.5s tick）
  └─ TryEmitAiDecision(state)                    # Evaluate 前置，LLM 优先
      └─ RequestLiveAiDecisionIfNeeded()         # AiRequestIntervalSeconds 节流 + RequestInFlight 防重入
          └─ AiDecisionBridge.RequestDecisionAsync()
              ├─ GameStateProvider.CaptureGameState()   # 事实层快照
              ├─ OllamaGenerateClient.BuildGameStatePrompt(state, instruction)  # 决策策略 + JSON 状态 + 输出格式
              └─ OllamaGenerateClient.GenerateAsync(prompt, model, stream, system)  # HTTP POST
                    └─ AiDecision.Parse(响应) → LastStructuredDecision
下一 tick：TryEmitAiDecision 消费 LastStructuredDecision
  └─ P2SupportDecisionBridge.TryBuildDecisionFromAiDecision（LLM 意图 → 本地意图映射）
      └─ TryValidateDecision（白名单 + 情境校验）
          └─ TryEmitDecision → Executor 执行
```

关键文件：
- `OllamaGenerateClient`（scripts/systems/ai/OllamaGenerateClient.cs）：纯通信层，`Endpoint` 导出（默认 `http://localhost:11434/api/generate`）、流式/非流式、超时、`think=false`、**已有 `system` 参数通道**（payload["system"]）
- `AiDecisionBridge`（scripts/systems/ai/AiDecisionBridge.cs）：状态采集 + prompt 组装 + 响应解析编排；`DefaultInstruction` 导出
- `P2SupportBrain.TryEmitAiDecision`（scripts/companions/P2SupportBrain.cs:247-309）：LLM 决策消费、去重（signature）、映射、校验

### 9.2 外部 API 适配现状：目前不能直接接

当前请求体是 **Ollama 专属协议**（`/api/generate`：`model/prompt/stream/options.num_predict/think`，响应 `response/thinking`），而 OpenAI 兼容 API（OpenAI/DeepSeek/Qwen 开放平台/中转站）用的是 `/chat/completions`（`messages:[{role,content}]`、`max_tokens`、`temperature` 顶层）。差异：

| 项 | Ollama（现状） | OpenAI 兼容（外部 API） |
|---|---|---|
| 端点 | `/api/generate` | `/v1/chat/completions` |
| 请求体 | `prompt` + `system` 字段 | `messages` 数组（system/user 角色） |
| 采样参数 | `options.num_predict/temperature` | 顶层 `max_tokens/temperature` |
| 响应字段 | `response/thinking/done_reason` | `choices[0].message.content` |

### 9.3 接入方案（三选一）

| 方案 | 做法 | 权衡 |
|---|---|---|
| **A. Ollama 网关（推荐起步）** | 继续走本地 Ollama，Ollama 已支持拉取外部模型/中转配置；零代码改动 | 依赖本地 Ollama 进程 |
| **B. OpenAI 兼容客户端（推荐正式）** | 新增 `OpenAICompatClient`（或给 OllamaGenerateClient 加协议切换：`UseOpenAICompat` 导出，按端点格式组请求体/解析响应） | 一次实现，之后任意 API 换 BaseUrl/Key/Model 即可 |
| **C. 客户端抽象** | 抽 `IGenerateClient` 接口，Ollama/OpenAI 两个实现，`AiDecisionBridge` 面向接口 | 最干净，改动面略大 |

推荐 **B**：`OllamaGenerateClient` 加 `UseOpenAICompat` 开关 + `ApiKey`/`BaseUrl` 导出，同一套调用方（AiDecisionBridge/Brain）零改动。

### 9.4 persona 注入点（与第 8 章衔接）

- ✅ `OllamaGenerateClient.GenerateAsync` 的 `system` 参数通道已接入：`AiDecisionBridge.PersonaSystemPrompt` 导出，`RequestDecisionAsync` 请求时传入（Ollama 协议映射 `payload["system"]`）；OpenAI 兼容协议下映射为 `messages[0]`（system 角色）
- 情境块（第 8.1 层 2）与 persona 分离：情境随状态动态生成（死亡/危险等），persona 静态不变

### 9.5 死亡等事件触发的 LLM 调用（与 4.5/8.4 衔接）

现状 LLM 是 **0.5s 轮询 + 节流**（决策用）；死亡对话需要**事件触发的一次性调用**：

```
P2PlayerEventMonitor（订阅死亡事件）
  └─ AiDecisionBridge.RequestDecisionAsync(instruction: "玩家死亡，P2 应表达伤心与鼓励，输出 1-2 句台词")
      └─ 结果 → PushHintDirect（动态台词）或映射 show_hint
```

注意 `RequestInFlight`/`MinRequestIntervalSeconds` 与轮询决策共用——事件触发时若决策请求在途会失败（返回 error），需要独立的"台词请求"通道或事件优先级提升。

## 10. 🆕 已实施的新增项（文档未规划，实施于本需求推进过程中）

### 10.1 AI 组件迁移（玩家 → P2）

LLM 基础设施（原挂在玩家 MainCharacter 上，玩家代码引用已全部注释）整体迁入 P2.tscn：

| 节点 | 迁移后状态 |
|---|---|
| `GameStateProvider` | P2 子节点，`PlayerPath = "../MainCharacter"`（组回退 `player` 兜底） |
| `OllamaClient` | P2 子节点（`DefaultModel = "qwen3.5:latest"`、流式） |
| `AiDecisionBridge` | P2 子节点（`Model = "qwen3.5:latest"`） |
| `GameStateDebugPanel` / `AiOutputDebugPanel` | P2 子节点（CanvasLayer，默认 `visible = false`） |
| `AiDecisionExecutor` | **节点已删除**（自动接管逻辑废弃）；脚本保留（AiOutputDebugPanel 编译依赖类型，且其 executor 段已清理） |

脚本路径同步：`P2SupportBrain`/`P2DebugPanel` 的 `GameStateProviderPath`/`AiDecisionBridgePath` 改为 P2 内兄弟路径。

### 10.2 LLM 启用与调优

- P2.tscn AI_Brain 配置：`EnableAiDecisionBridge = true`、`AiRequestIntervalSeconds = 10`（原 1s，推理争抢 GPU/显存导致卡顿）
- `MaxPredictTokens` 512→128；`BuildGameStatePrompt` 策略规则精简（12→5 行）——qwen3.5 本地推理时间 ~30s → ~10-15s
- 卡顿根因排查结论：debug 面板渲染（流式 chunk 每 token 全量重建 RichText）+ 面板默认可见——已修复（见 10.3）；LLM 推理期间 GPU/显存争抢是残余卡顿（模型 5.6GB 显存常驻），缓解手段 = 加大请求间隔 / 换小模型

### 10.3 调试面板优化

- `AiDecisionBridge.LastModelName` + 面板顶部常驻 `[Model] xxx` 显示（实际响应模型名）
- 流式渲染节流：chunk 只累积文本，`_Process` 每 0.1s 渲染一次（原每 token 一次全量重建）
- AI Prompt 显示截断 800 字符（完整 GameState JSON 可达数万字符，是渲染大头）
- `GameStateDebugPanel` 隐藏时停止 AutoRefresh（原隐藏仍每秒生成大 JSON）
- `AiOutputDebugPanel` 清理 executor 死代码（Autopilot/Execution 段、F6/\| 提示——对应按键处理已在 SamplePlayer 注释）

### 10.4 P2 台词体系清理

| 删除 | 位置 |
|---|---|
| `equipment_bonus` label | dtl（`EquipmentBonus` 枚举 + case 同步删；食物路径废弃后无调用方） |
| `fallback_low_hp` label | dtl（`FallbackLowHp` 枚举 + case + `BuildFallbackHint` 死分支同步删） |
| `ai_chatter` label | dtl（`AiChatter` 枚举 + case 同步删；个性台词走 `PushHintDirect` 动态文本） |

dtl 现存 label 全部有真实调用链：`ready_N`/`quiet_scene_pickup_N`/`fallback_generic_N`/`fetch_weapon_N`/`follow_started_N`/`free_roam_started_N`/`shield_applied_N`/`healed_N`/`shield_expired_N`（自动变体）+ `fallback_enemy_close` + `combat`（调试热键）+ `suggest_retreat`/`ai_received`（LLM 路径）+ `direct`（动态文本）。

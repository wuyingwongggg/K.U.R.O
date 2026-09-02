# 关卡推进与电梯会话设计（Level Progression & Elevator Session）

> 状态图例：✅ 已实施（PoC 已验证）｜🟡 部分/讨论过未落地｜❌ 待办
> 相关代码：`scripts/systems/stage/`（StageConfig/StageSession）、`scripts/environments/ElevatorController.cs`、
> `scenes/Stage_hotel.tscn`（壳）、`resources/stages/*.tres`（关卡配置）、`scripts/managers/StageGeneratorManager.cs`

---

## 1. 目标与范围

**已实现（PoC：Stage_hotel）**：
- ✅ 关卡壳（Stage_hotel.tscn）常驻，玩家/P2/相机不重建——**房间级 streaming**：换关 = 清空 World 房间 → 注入 StageConfig → 从 x=0 重新生成 → 玩家传送到落点
- ✅ 关卡配置化：`StageConfig`(.tres) 承载房间池/名称/楼层；新增关卡 = 新建 .tres + 注册进 StageSession，壳场景零改动
- ✅ 电梯会话选关：进舱 → 数字键选关 → 门关骑行 → 开门 → 出舱换关
- ✅ 多入口统一：全部换关收敛到 `StageSession`（电梯/F1F2 调试/隐藏入口 GoToStage）

**❌ 未做（后续）**：
- 电梯多功能（商店 UI/剧情 Dialogic 接入）
- 隐藏入口过渡遮罩（Fade）；存读档语义迁移；旧 Stage_1~4 场景文件退役
- 地下/彩蛋示例关卡数据（API 就绪，缺房间池与 config）

---

## 2. 现状切关链（历史基线——新推进模型替代前的架构）

### 场景三层
```
关卡壳 Stage_X.tscn（Stage_1~4 + test）：BattleScene/CameraZoneManager/CutsceneManager
  /StageGeneratorManager（export 池）/BuildSelectionManager/World{玩家+相机+P2}/CanvasLayer 黑边
房间 scenes/levels/（A/B/C/D 系）：含 EnemySpawnManager、家具、CutsceneTrigger；B/C/D_end 带电梯占位
Loading_elevator.tscn：ElevatorController + close/loading/open 动画 + Interact/ExitArea
```

### 切换路径（原 4 条，均 ChangeSceneToPacked）
| 路径 | 说明 | 状态 |
|---|---|---|
| 电梯（ElevatorSceneLoader 按需实例化） | 关底房间占位 → 玩家接近 → 加载电梯 → 交互骑行 → 出舱切场景 | 🟡 会话模式已接管；ChangeScene 分支保留给无 StageSession 场景 |
| 出租车 TaxiController | 同构 + ConsumePreloadedScene | 保留 |
| 过场 ChangeSceneStep | Cutscene 序列尾部切场景 | 保留（未来剧情切关可改 GoToStage） |
| UI 直达（MainMenu/GameOver） | 新游戏/重试/标题 | 保留 |

### 切换边界清理协议（原内聚 LeaveElevator）
`WorldItemSpawner.ClearCache` / `DialogicUtils.CleanupPersistentState` / `PauseManager.ClearAllPauses` /
`AutosaveCurrentSlot` + `CaptureInventoryTransit`（玩家每关重建时代）——**会话模式（world 内换关）下玩家常驻，Transit 快照不再需要**；
其余清理在换关边界同样适用（清空房间子树自动触发 _ExitTree 链）。

### 关键事实（支撑配置化）
- Stage_1~4 壳结构**完全同构**，差异只有房间池（字母难度系）与 LevelName/描述 → 关卡 = 数据
- `GameSaveData` 无场景路径字段（MaxStageReached/StoryFlags 等）→ 存档天然适配选关化
- `ElevatorSceneLoader.NextStagePath` 是房间文件内静态配置（关卡链写死）——🟡 会话模式已使其失效，Stage_1~4 保留原值（B_end 已恢复 Stage_3）

---

## 3. 目标架构（当前实现）

```
StageSession（壳场景节点，group "stage_session"）—— 换关唯一执行点
  AllConfigs：全量关卡注册表（StageConfig[]）
  StartStage：起始关（Stage_hotel = stage_2）
  _currentConfig：当前关（决定电梯选项的推导基准）
  API：
    GetOptionCount/DescribeOptions/SelectStage/CommitPending ← 电梯会话
    GoToStage(config, landingPosition?) ← 隐藏入口/机关/剧情（落点=密道出口坐标）
    CycleDebugConfig ← F1/F2 调试
  ↓
StageGeneratorManager（GenerateOnReady=false 纯引擎）
  Regenerate(config?, relocateActors, landingPosition?)：清空房间(Free) → x=0 拼接 → 相机边界 → 玩家落点 → StageGenerated 信号
  ↓
ElevatorController（会话模式 = 电梯 UI 载体，可替换）
  group 找到 StageSession → interact 进 Selecting（HintLabel 文字+数字键）→ SelectStage → 骑行(纯动画) → Arrived → 出舱 CommitPending
```

**多入口统一**：任何层间移动最终调 `Regenerate`；电梯/隐藏门只是触发方式 + 掩护不同。

---

## 4. 关卡推进模型

**已实施**：
- `StageConfig.Floor` = **真实楼层**（支持负值=地下）；注册：home(住宅)、stage_2 大堂(**1**)、stage_3a/b/c 餐厅(**2**)、stage_4a/b/c 办公(**3**)
- 电梯选项推导 = **上行：Floor == 当前+1 的分支**（大堂 1 → 餐厅 2 的 3a/b/c；餐厅 2 → 办公 3 的 4a/b/c）
- `DownFloorTarget`：大堂已配 = -1（地下入口预留，面板待地下 config 出现后显示）

**方向出口（✅ 已实施）**：
- `StageConfig.UpFloorTarget`（0 = 默认 Floor+1 推导）/ `DownFloorTarget`（0 = 无向下出口）——主链外的层（彩蛋/地下）靠它挂回主链
- 选项**排除当前关自身**；彩蛋分支只在**非彩蛋层**电梯出现（彩蛋层靠 UpFloorTarget 回程——不再自循环困死）
- 用法：地下关 Floor=-1 + `UpFloorTarget=1` → 回程面板回大堂；大堂 `DownFloorTarget=-1` → 面板含地下1层

**彩蛋三态（✅ 已实施，运行时状态）**：
- 默认**关闭**（未激活不出现；`RequiredStoryFlag` 为空不再等于"始终可选"）
- 触发激活：`ActivateEasterEgg(config)`（机关/剧情事件调用）或 `RequiredStoryFlag` 存档旗标满足 → 面板尾部出现
- 进入即**消费**（`_consumedEggs`，`ApplyStage` 统一处理）→ 永不再出现（防重复触发）
- 机关直达玩法：`GoToStage(彩蛋, 落点)` 不需先激活（找到即解锁），进入同样消费
- ⚠️ 激活/消费状态在内存，会话内有效——重启重置（持久化见 §8）

---

## 5. 电梯会话流程（当前实现时序）

```
Idle(舱内 interact)
 → EnterSelecting：HintLabel 显示 "1=餐厅a区 2=餐厅b区 3=餐厅c区"（DescribeOptions）
 → 数字键：SelectStage(index)
 → StartClosing：门关(close)
 → Loading：骑行动画（会话模式不 LoadThreadedRequest 场景）；_rideTimer ≥ MinRideDuration → Arrived
 → open（门开）→ 玩家走出 ExitArea
 → LeaveElevator：_session.CommitPending() → Regenerate(选中 config) → 旧房间卸载+新关生成+玩家落新关起点+相机 Snap
```
无 StageSession 的场景（Stage_1~4）：原 ChangeSceneToPacked 流程不变。

---

## 6. 商店与剧情接入 ❌（内容侧新系统，未做）

- 商店：无现有实现；需商品表（ItemDefinition 价格）、货币核查、ShopWindow（参考 InventoryWindow 窗口模式）
- 剧情：Dialogic timeline 现成；电梯剧情 = 新 .dtl + 会话窗口触发编排
- 并行语义（商店/剧情/加载互不阻塞）已由"骑行纯动画 + 出舱才换关"架构自然支持

---

## 7. 实施状态

| 项 | 状态 |
|---|---|
| StageConfig.cs（池/名称/真实楼层 Floor/方向出口/彩蛋标记） | ✅ |
| StageGeneratorManager.Regenerate（注入/清空/落点/GenerateOnReady） | ✅ |
| StageSession（AllConfigs/StartStage/层级推导/GoToStage/ActivateEasterEgg/F1F2） | ✅ |
| ElevatorController 会话模式（Selecting/数字键/出舱 Commit/骑行免加载） | ✅ |
| 关卡配置资源（stage_2/3a/b/c/4a/b/c/home .tres，真实楼层） | ✅（3x/4x 缺中间房间池，待填充） |
| 壳场景 Stage_hotel.tscn | ✅ |
| 方向出口（UpFloorTarget/DownFloorTarget）+ 选项排除自身 | ✅ |
| 彩蛋三态（默认关 → 激活 → 进入消费） | ✅（内存态；持久化 ❌） |
| 隐藏入口场景触发器组件 | 🟡 API 就绪，组件待建 |
| 过渡遮罩（Fade） | ❌ |
| 商店/剧情接入 | ❌ |
| 旧 Stage_1~4 迁移/退役 | ❌（新链路验证后） |

---

## 8. 未决问题

- [ ] 地下（B1/B2）与彩蛋的示例 config 数据（API 就绪，缺房间池资源）
- [ ] 彩蛋激活/消费状态的**存档持久化**（当前内存态，重启重置）
- [ ] 隐藏入口触发组件形态（机关 Area2D？与 CutsceneTrigger 合并？）
- [ ] 存档语义：当前关/到达最高层如何记录（MaxStageReached 语义 vs Floor）
- [ ] `PendingNextStagePath` 遗留 API 清理
- [ ] Loading_elevator 纹理内存预算（常驻壳场景下电梯延迟加载仍有效）
- [ ] 电梯到达音/视觉反馈；选关面板正式 UI（当前 HintLabel 文字）

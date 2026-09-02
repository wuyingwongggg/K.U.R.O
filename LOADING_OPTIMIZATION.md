# 加载优化指南（Loading & Performance Optimization）

> 场景切换 / 敌人生成时的卡顿问题说明与实施步骤。
> 相关现象：每次加载新地图、敌人生成时出现严重卡顿，但 profiler 平均负载正常（一次性主线程同步阻塞）。

---

## 1. 卡顿根因

卡顿均为**一次性主线程同步阻塞**（非持续热点，profiler 平均负载看不出），按占比排列：

| 卡顿源 | 时机 | 机制 |
|---|---|---|
| **资源树首次加载**（大头） | 地图加载 / 敌人生成 | 房间场景、敌人 Spine 骨骼数据（1~3MB atlas）、家具纹理、材质 **首次实例化时才同步加载 + Shader 首次渲染才编译** |
| **建树 + C# _Ready** | 地图加载 | 数百节点一帧建树、各脚本 `_Ready` 初始化、物理注册 |
| **导航烘焙** | 地图加载后 1~2 帧 | 家具/屏障带 `navigation_polygon_source_geometry_group` → 防抖后**同步全场景 `BakeNavigationPolygon`**（主线程） |
| **运行时日志输出** | 全程 | 每家具/敌人生成打印多条（`RandomLitterPartDisplay`、`保存原始碰撞设置` 等）——日志 IO 本身有开销 |

---

## 2. 已完成的优化

### 2.1 启动预热：敌人 Spine + 材质 Shader（EnemyStageWarmer）

- 文件：`scripts/fx/EnemyStageWarmer.cs`（场景 Autoload：`scenes/managers/EnemyStageWarmer.tscn`）
- 机制：启动时静默实例化全部 14 个 `Enemy_*.tscn`（透明、渲染两帧后销毁）——把 Spine 数据解析 + 敌人材质 Shader 编译从"战斗中首次生成敌人"挪到开局
- 开关：`WarmOnReady`（默认 true）
- 注意：`GetViewport().GetCamera2D()` / `GetFirstNodeInGroup("player")` 在 Autoload（root）上下文不可靠——预热不依赖相机（无 Camera2D 时 CanvasItem 照常渲染，Shader 编译照常触发）

### 2.2 关卡切换异步预加载（已有）

- `ElevatorController` / `TaxiController` / `ChangeSceneStep`：切换前 `LoadThreadedRequest` 整关场景（后台线程）→ 等待 → `ChangeSceneToPacked`
- **缺口**：只预加载整关场景本身——房间池（`StageGeneratorManager` 的 ext_resource）是**惰性解析**，房间资源树在运行时 Instantiate 才首次加载——卡顿大头未被覆盖

---

## 3. 已撤回的优化（教训）

### 分帧生成房间（StageGeneratorManager `await ProcessFrame`）——已撤回

- 曾尝试每帧实例化 1 间房间摊平建树——**导致相机/过场异常**（A_begin 前相机停在原点、A_end 过场 zoom 失效）
- **根因**：`await` 把关卡生成从原子操作拆成跨帧流程——`CutsceneTrigger`（物理 `BodyEntered`）在生成期间触发 → 过场与生成并发 → 相机/区域/zoom 在中间状态被错误初始化；重定位提前 + `SnapToTarget` 的边界依赖也无法修复（Snap 会打断已接管相机的过场）
- **教训**：关卡生成是原子操作——相机/区域/过场依赖"生成完成 = 场景已就位"——**不要用跨帧 await 拆分生成流程**

---

## 4. 正确的加载优化原则

### 4.1 后台加载边界（Godot 官方模式）

| 操作 | 线程 | 说明 |
|---|---|---|
| 资源加载（PackedScene/纹理/Spine） | ✅ 后台 | `ResourceLoader.LoadThreadedRequest`（引擎内部线程池）——不卡主线程 |
| 实例化 / 建树 | ❌ 主线程 | 场景树操作非线程安全 |
| Shader 编译 | 主线程（首次渲染） | 后台加载不编译——需预热（渲染触发） |
| 手动开 Thread | ❌ 禁止 | 用引擎的 `LoadThreadedRequest`，不要自定义 Thread |

### 4.2 场景切换正确流程

```
1. 显示加载界面 / 遮罩（LoadingScreen）
2. LoadThreadedRequest(场景 + 其资源树 + 房间池)   ← 后台线程
3. 轮询 LoadThreadedGetStatus 等全部完成
4. ChangeSceneToPacked（主线程）——资源已缓存，切换瞬间成本 = 实例化（远小于加载）
5. 卸载加载界面
```

### 4.3 预加载原则（不用 Autoload 滥用 / 不用 preload）

- **组件自包含**：预加载由"持有资源配置的组件"自己发起（如 `StageGeneratorManager` 在 `_Ready` 后台预加载自己的房间池）——不引入全局单例
- **不用 `preload`**：编译时加载、启动即占内存、无法按需释放——用 `LoadThreadedRequest` / `ResourceLoader.Load`
- **Autoload 收敛**：保留真正全局的（SaveManager/PauseManager/UIManager），预热器等"启动执行一次"的组件应挂加载界面/场景内驱动

---

## 5. 实施步骤（待做，按性价比）

### Step 1：清理运行时日志（低风险，立即见效）——✅ 已完成

- ✅ 移除 `RigidBodyWorldItemEntity` 常态逐条打印：`保存原始碰撞设置`（每家具 `_Ready`）、投掷碰撞设置 3 处 trace（每次投掷 3 条）、`在落点销毁物品`（每次落地销毁）
- ✅ 移除 `RandomLitterPartDisplay` 逐条打印（每家具 `_Ready` 随机显示明细）
- ✅ 移除交互操作级打印（每次拾取/投掷必打，一次操作 ≈10 行）：
  `PlayerItemInteractionComponent`（投掷按键 5 连、take_up、TryHandlePickup 全流程 ~8 行、状态切换 2 行）、
  `PickupProperty`/`ItemPickupProperty`/`DroppablePickupProperty`（每次拾取 1~3 行）、`PlayerIdleState` 持握切换
- ✅ 保留：全部 `PushWarning`/`PrintErr`（仅异常路径触发）、`GameLogger` 系统日志、碰撞设置被改检测（仅异常触发）、
  初始化一次性打印（MainCharacter/PlayerItemInteractionComponent 启动确认）、攻击特效类打印
  （CriticalStrike 系列/SlowOnHit/MechGlove/DiscoFlashStun/CameraShake/EnemyAttackTemplate——按概率/条件触发非每击必打，用户确认保留）、
  低频事件日志（GameMemoryService session/P2SupportExecutor AI 决策）
- ✅ 移除 `EnemySpawnManager` 无条件打印：`Trigger ignored`/`Trigger entered by`（每次 body 进出触发器必打）；`LogSpawnEffectPositions` 开关默认值 true→false（该开关内的生成时序日志全部保留但默认静默——所有关卡实例本就显式 false，调试时临时开启即可）
- 未处理（可选继续）：`EnemySpawnConsole`（测试控制台工具）、`WaveSpawnManager` 每波次数条事件日志——量级低

### Step 2：导航烘焙统一 + 延迟（低风险）——✅ 已完成

- ✅ 新建共享静态类 `NavigationRebakeCoordinator`（scripts/core/）：统一 `RigidBodyWorldItemEntity` + `DestructibleObject` 的烘焙请求（两套重复的静态字段/递归烘焙/调度逻辑已删除，共 4 处调用点改为 `NavigationRebakeCoordinator.RequestRebake(this)`）
- ✅ 防抖 `Timer(0.0)` → debounce 窗口（0.5s）：窗口内新请求滑动续期，停顿 0.5s 后统一烘焙一次——地图加载时数百家具 `_Ready` 的连续请求合并为加载后一次烘焙
- 未做（可选）：多个 NavigationRegion2D 每帧烘焙 1 个（分帧烘焙）——当前地图加载卡顿大头已在 Step 3 资源预加载层面解决，烘焙分帧收益低，暂不实施

### Step 3：房间池资源预加载（核心，解决卡顿大头）

- **方案 A（推荐）**：切换流程（电梯/出租车）在加载界面期间，`LoadThreadedRequest` 下一关的房间池（房间路径从场景配置读取或由生成器提供）
- **方案 B（简单）**：`StageGeneratorManager` 在 `_Ready`（生成前）后台 `LoadThreadedRequest` 自己的房间池，等完成再生成——组件自包含，不依赖切换流程
- 效果：Instantiate 不再触发首次加载（IO/解析/Shader 已缓存）——卡顿降为纯建树

### Step 4：启动预热扩展（可选）

- 把 EnemyStageWarmer 的模式扩展到**地图/家具材质 Shader**（shadow/outline/scanline/hitflash）——消除切换时的 Shader 编译尖峰
- 或改为非 Autoload（挂加载界面/主场景内驱动）——遵循"Autoload 收敛"原则

### Step 5：验证

- 对比：优化前后，地图加载瞬间的帧时间尖峰（Godot Debugger → Monitors → Frame Time）
- 关卡切换期间卡顿是否被加载界面吸收；敌人生成是否顺滑

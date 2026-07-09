# 构筑系统具体实现文档 v4.0

---

## 一、系统架构

### 数据流

```
游戏开局 → 构筑核心 N选1 → 选择 MachineCore / WaiterCore / ThrowCore
  → EffectController.AddEffect(coreEffect)  激活核心机制
  → CoreHUD 绑定对应 UI
  → 后续所有效果按 BuildClass 过滤（核心类型 + Generic）

击杀敌人 → 分数增加 → ScoreThresholdCurve 触发
  → 从 EffectPool 筛选 match 当前核心 BuildClass 的效果
  → 三选一弹窗 → 选择 BuildEffectDefinition
  → BuildStatBonusEffect 加入 EffectController
```

### 过滤逻辑

```
筛选条件：effect.BuildClass == _playerCoreClass || effect.BuildClass == "Generic"
```

### 核心效果统一架构

**所有核心机制均为 ActorEffect 子类，通过 EffectController 统一管理。MainCharacter 只提供基础设施 hook，不包含核心逻辑。**

```
MainCharacter（通用层）
├── EffectController              ← 统一管理所有效果的生命周期
├── HitboxEditorPreview           ← 通用投掷指示器（节点不动，开关由 ThrowCoreEffect 控制）
│
选择核心后 EffectController.AddEffect：
├── MachineCoreEffect : ActorEffect   ← 热量值、攻击/移动监听、释放逻辑
├── WaiterCoreEffect  : ActorEffect   ← 药剂计数、定时器、回血逻辑
├── ThrowCoreEffect   : ActorEffect   ← 激活 HitboxEditorPreview、监听核心键、生成家具
```

### 核心效果生命周期（统一模式）

| 生命周期 | MachineCoreEffect | WaiterCoreEffect | ThrowCoreEffect |
|---|---|---|---|
| `OnApply` | 初始化热量=0，连接 MainCharacter 事件 | 启动药剂生成定时器 | `mc.EnableThrowIndicator(true)` |
| `OnTick` | 热量自然衰减 | 定时器到期 → +1 药剂 | 可选：CD 冷却 |
| 核心键输入 | 清空热量 → 计算增伤 → 应用 | 消耗 1 药剂 → 回血 | 读取指示器位置 → 生成家具 |
| `OnRemoved` | 清热量，断开事件，清理 Buff | 停定时器，清药剂 | `mc.EnableThrowIndicator(false)` |

### MainCharacter 基础设施 hook

```csharp
public void EnableThrowIndicator(bool enable);  // HitboxEditorPreview 开关（ThrowCore 专用）
public event Action<float> OnAttackLanded;       // MachineCore 监听累积热量
public event Action<float> OnMoved;              // MachineCore 监听累积热量
```

---

## 二、新增文件

### 2.1 BuildCoreDefinition

| 文件 | 类型 | 说明 |
|---|---|---|
| `scripts/systems/BuildCoreDefinition.cs` | Resource (`[GlobalClass]`) | 构筑核心定义 |

**字段：**

| 字段 | 类型 | 说明 |
|---|---|---|
| `CoreId` | string | 唯一标识 |
| `DisplayName` | string | 核心名称 |
| `Description` | string (MultilineText) | 核心效果描述 |
| `BuildClass` | string | 所属构筑类别（Machine / Waiter / Throw） |
| `CoreEffectScene` | PackedScene | 核心机制 ActorEffect 场景 |
| `Icon` | Texture2D | 核心图标 |

### 2.2 BuildStatBonusEffect（已实现）

| 文件 | 类型 | 说明 |
|---|---|---|
| `scripts/core/effects/BuildStatBonusEffect.cs` | ActorEffect 子类 | 构筑效果的属性修正包装 |

**行为：**
- `OnApply`: 首次保存原始值（存入 `Dictionary<string, float> _originals`），应用增量
- `OnStackRefreshed`: 追加一套增量（叠加 N 层 = N 倍增量）
- `OnRemoved`: 遍历 `_originals` 恢复所有原始值
- 新增属性只需在 `SaveOriginals` / `ApplyDelta` / `RevertDelta` 各加一行

### 2.3 Core Effect 类

| 文件 | 类型 | 说明 |
|---|---|---|
| `scripts/effects/MachineCoreEffect.cs` | ActorEffect 子类 | 热量表机制 |
| `scripts/effects/WaiterCoreEffect.cs` | ActorEffect 子类 | 药剂机制 |
| `scripts/effects/ThrowCoreEffect.cs` | ActorEffect 子类 | 家具生成机制 |

### 2.4 Core HUD

| 文件 | 类型 | 说明 |
|---|---|---|
| `scripts/ui/CoreHUD.cs` | Control | 核心机制 HUD 容器，根据核心类型实例化对应子 UI |
| `scripts/ui/HeatGaugeHUD.cs` | Control | 热量槽 UI |
| `scripts/ui/MedicineHUD.cs` | Control | 药剂计数 UI |
| `scripts/ui/FurnitureHUD.cs` | Control | 家具生成相关 UI（CD / 计数等） |

---

## 三、修改文件

### 3.1 BuildSelectionManager

| 变更 | 说明 |
|---|---|
| 新增 `_playerCoreClass` 字段 | 玩家已选核心的 BuildClass，null = 未选核心 |
| 新增 `SetPlayerCoreClass(string buildClass)` | 设置核心类型，激活效果过滤 + 通知 CoreHUD |
| 修改 `PickRandomEffects` | 按核心类型 + Generic 筛选 EffectPool |
| 修改 `ApplyEffectBonuses` | 用 BuildStatBonusEffect 替代直接字段修改（已实现） |
| 新增 `CorePool` 导出 | Array<BuildCoreDefinition>，核心 N选1 的池 |

### 3.2 MainCharacter

| 变更 | 说明 |
|---|---|
| 新增 `EnableThrowIndicator(bool enable)` | HitboxEditorPreview 的 setter，开关由 ThrowCoreEffect 调用 |
| 新增 `OnAttackLanded` 事件 | MachineCoreEffect 订阅以累积热量 |
| 新增 `OnMoved` 事件 | MachineCoreEffect 订阅以累积热量 |

### 3.3 BuildEffectDefinition

| 变更 | 说明 |
|---|---|
| 现有字段不变 | BuildClass 用于过滤，Generic 表示通用效果 |
| 可选新增 `EffectScene` | PackedScene?，复杂效果的自定义 ActorEffect 场景 |

### 3.4 输入映射

新增核心技能键绑定（如 Key Q 或 Key R），各 CoreEffect 在 `_Input` 中各自监听。

---

## 四、MachineCoreEffect 详细设计

### 机制

```
攻击命中 / 移动中 → 累计 heat 值
  - MainCharacter.OnAttackLanded → +15 heat
  - MainCharacter.OnMoved → frame heat 累积
heat 上限: 100
heat 自然衰减: -2 heat / 秒（OnTick 中处理，停止攻击/移动后生效）

按下核心技能键:
  - 消耗全部 heat（不能为零）
  - damage_buff = heat * 0.01（100 热量 = +100% 增伤）
  - 持续 buffDuration 秒
```

### HUD

- HeatGaugeHUD：填充式条形图，0-100
- 颜色从蓝 → 橙 → 红渐变
- 释放后清空 + 闪烁动画

---

## 五、WaiterCoreEffect 详细设计

### 机制

```
每 medicineInterval 秒获得 1 针药剂（OnTick 定时器）
最大携带: maxDoses 针

按下核心技能键:
  - 消耗 1 针药剂
  - 回复 healPerDose 血量
  - 无 CD（连按可连续消耗）
```

### HUD

- MedicineHUD：药剂图标 + 数字
- 有药高亮，无药灰色
- 获取药剂时弹跳动画

---

## 六、ThrowCoreEffect 详细设计

### 机制

```
OnApply: MainCharacter.EnableThrowIndicator(true)
  - 激活 HitboxEditorPreview（进入投掷状态时显示指示器）

按下核心技能键:
  - 读取 HitboxEditorPreview 当前指示位置
  - 在该位置生成一个一次性家具（RigidBodyWorldItemEntity）
  - 有 CD：furnitureCooldown 秒内不可再次生成

OnRemoved: MainCharacter.EnableThrowIndicator(false)
  - 关闭指示器
```

### HUD

- FurnitureHUD：CD 冷却环 + 可生成状态指示

---

## 七、场景配置步骤

1. 将 `BuildSelectionManager` 场景拖入战斗场景
2. 配置 `ThresholdCurve`、`CorePool`（核心定义）、`EffectPool`（效果定义）
3. 配置核心出现的触发时机（场景 × 分数门槛）
4. 确认玩家节点下有 `EffectController`
5. 在 HUD 场景中添加 `CoreHUD` 容器

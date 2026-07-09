# 构筑系统具体实现文档 v4.0

---

## 一、系统架构

### 数据流

```
游戏开局 → 构筑核心 N选1 → 选择 MachineCore / WaiterCore / ThrowCore
  → 激活核心机制（热量表 / 药剂） + 应用 HUD
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

### 2.2 BuildStatBonusEffect

| 文件 | 类型 | 说明 |
|---|---|---|
| `scripts/core/effects/BuildStatBonusEffect.cs` | ActorEffect 子类 | 构筑效果的属性修正包装 |

**字段：**

| 字段 | 类型 | 说明 |
|---|---|---|
| `StatBonuses` | Dictionary<string, float> | 属性修正字典 |
| `MaxStacks` | int | 最大可叠加次数（export，默认 6） |

**行为（遵循 SimpleSpeedEffect 模式）：**
- `OnApply`: 首次保存原始值（Speed, AttackDamage, MaxHealth），应用增量
- `OnStackRefreshed`: 追加一套增量
- `OnRemoved`: 恢复所有原始值

### 2.3 Core Effect 类

| 文件 | 类型 | 说明 |
|---|---|---|
| `scripts/effects/MachineCoreEffect.cs` | ActorEffect 子类 | 热量表机制 |
| `scripts/effects/WaiterCoreEffect.cs` | ActorEffect 子类 | 药剂机制 |

### 2.4 Core HUD

| 文件 | 类型 | 说明 |
|---|---|---|
| `scripts/ui/CoreHUD.cs` | Control | 核心机制 HUD 容器 |
| `scripts/ui/HeatGaugeHUD.cs` | Control | 热量槽 UI |
| `scripts/ui/MedicineHUD.cs` | Control | 药剂计数 UI |

---

## 三、修改文件

### 3.1 BuildSelectionManager

| 变更 | 说明 |
|---|---|
| 新增 `_playerCoreClass` 字段 | 玩家已选核心的 BuildClass，null = 未选核心 |
| 新增 `SetPlayerCoreClass(string buildClass)` | 设置核心类型，触发效果过滤 |
| 修改 `PickRandomEffects` | 按核心类型 + Generic 筛选 EffectPool |
| 修改 `ApplyEffectBonuses` | 用 BuildStatBonusEffect 替代直接字段修改 |
| 新增 `CorePool` 导出 | Array<BuildCoreDefinition>，核心 N选1 的池 |

### 3.2 BuildEffectDefinition

| 变更 | 说明 |
|---|---|
| 现有字段不变 | BuildClass 用于过滤，Generic 表示通用效果 |

### 3.3 BuildEffectDefinition .tres 文件

所有效果需要设置 `BuildClass`：
- `Machine` 开头 → 仅 Machine 核心可见
- `Waiter` 开头 → 仅 Waiter 核心可见
- `Throw` 开头 → 仅 Throw 核心可见
- `Generic` → 所有核心可见（需新建 Generic 效果）

### 3.4 输入映射

新增核心技能键绑定（如 Key Q 或 Key R）。

---

## 四、MachineCoreEffect 详细设计

### 机制

```
攻击命中 / 移动中 → 累计 heat 值
heat 上限: 100
累计速率:
  - 攻击命中: +15 heat
  - 移动每帧: +0.5 heat * delta
  - heat 自然衰减: -2 heat / 秒（停止攻击/移动后）

按下核心技能键:
  - 消耗全部 heat（不能为零）
  - 获得 damage_buff = heat * 0.01 * base_attack（即 100 热量 = 100% 增伤）
  - 持续 buffDuration 秒
  - CD 期间无法再次释放
```

### HUD

- 热量槽：填充式条形图，0-100
- 颜色从蓝 → 橙 → 红渐变
- 释放后动画：清空 + 闪烁

---

## 五、WaiterCoreEffect 详细设计

### 机制

```
每 medicineInterval 秒获得 1 针药剂
最大携带: maxDoses 针

按下核心技能键:
  - 消耗 1 针药剂
  - 回复 healPerDose 血量
  - 无 CD（连按可连续消耗）
```

### HUD

- 药剂图标 + 数字
- 有药时高亮，无药时灰色
- 获取药剂时图标弹跳动画

---

## 六、场景配置步骤

1. 将 `BuildSelectionManager` 场景拖入战斗场景
2. 配置 `ThresholdCurve`、`CorePool`（核心定义）、`EffectPool`（效果定义）
3. 配置核心出现的触发时机（场景 × 分数门槛）
4. 确认玩家节点下有 `EffectController`
5. 在 HUD 场景中添加 `CoreHUD` 容器

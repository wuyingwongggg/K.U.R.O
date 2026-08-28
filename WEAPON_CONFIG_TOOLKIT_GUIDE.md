# WeaponConfigToolkit 使用教程与注意事项

本文档介绍如何使用一键配置武器 ItemDefinition 的工具脚本 `WeaponConfigToolkit`，并总结常见问题与排查方式。

## 1. 工具目标

`WeaponConfigToolkit` 用于减少手工配置武器资源的重复操作，主要提供三类能力：

- 一键创建/更新武器资源包（ItemDefinition + WeaponSkillDefinition）
- 一键校验全部武器资源配置
- 一键修复武器攻击力与堆叠等历史异常数据

---

## 2. 前置准备

请确保以下文件和路径有效：

- 武器场景 `.tscn` 已存在，且包含 `AttackArea`
- `AttackArea` 下至少有一个可用 `CollisionShape2D`
- 目标输出目录可写（默认：`res://resources/items`、`res://resources/items/skills`）

---

## 3. 如何使用（一步一步）

### 3.1 挂载工具节点

1. 在任意编辑器场景里新建一个普通 `Node`。
2. 将脚本设置为 `res://scripts/tools/WeaponConfigToolkit.cs`。
3. 选中该节点，在 Inspector 填写参数。

### 3.2 配置创建参数

在 `Create Weapon Package` 分组填写：

- `ItemId`：例如 `Weapon_Stab_drill`
- `ItemDisplayName`：武器显示名
- `ItemDescription`：描述文本
- `WeaponScenePath`：武器场景路径（必须是 `res://...`）
- `ItemIcon`：图标（可选）
- `ItemCategory`：通常为 `Weapon`
- `WeaponAttackPower`：攻击力，限制范围 `0-999`
- `OverwriteAttackPowerIfExists`：已有攻击力是否覆盖

技能相关参数：

- `SkillId`：例如 `weapon_stab_drill`
- `SkillDisplayName`
- `SkillAnimationName`
- `SkillCooldownSeconds`
- `SkillDamageMultiplier`
- `SkillActivationAction`

### 3.3 一键生成/更新

将 `CreateOrUpdateWeaponPackage` 勾选为 `true`，工具会自动执行并恢复为 `false`。

执行后将自动：

- 创建或更新 ItemDefinition 资源
- 创建或更新 WeaponSkillDefinition 资源
- 绑定 `WorldScenePath`
- 绑定 `WeaponSkillResources`
- 强制 `MaxStackSize = 1`
- 写入或修正 `attack_power`

---

## 4. 校验与修复

### 4.1 校验全部武器配置

将 `ValidateAllWeaponItems` 勾选为 `true`。

会检查以下问题：

- `ItemId` 为空
- `WorldScenePath` 为空、不是 `res://`、或资源不存在
- 武器场景缺少 `AttackArea`
- `AttackArea` 缺少有效 `CollisionShape2D`
- `WeaponSkillResources` 为空或类型错误
- `SkillId` 为空
- `MaxStackSize` 非 1
- 缺失 `attack_power` 或 `attack_power < 0`

### 4.2 一键修复历史异常

将 `RepairAllWeaponAttackPowerValues` 勾选为 `true`。

会自动修复：

- `MaxStackSize != 1`
- 缺失 `attack_power`
- `attack_power` 非法值（负数、异常值）

---

## 5. attack_power 特别说明

当前系统对 `attack_power` 做了双重保护：

- 工具层：写入前会将值限制为 `0-999`
- 数据模型层（`ItemAttributeEntry`）：当 `AttributeId = attack_power` 时，任何来源写入都会被限制为 `0-999`

这意味着即使手动输入异常值，也会被自动修正。

---

## 6. 常见错误与排查

### 问题 A：点击创建时报类型转换异常

症状：`Unable to cast object of type 'Godot.Resource' to WeaponSkillDefinition`

原因：历史资源类型与当前脚本类型不一致。

处理：

- 已在工具中做了安全加载与重建逻辑
- 再次执行 `CreateOrUpdateWeaponPackage` 即可自动重建

### 问题 B：武器判定与预期不一致

优先检查：

- `ItemDefinition.WorldScenePath` 是否绑定到正确武器场景
- 武器场景 `AttackArea` 与 `CollisionShape2D` 是否存在
- 是否使用校验功能发现错误

### 问题 C：攻击力出现异常值

处理顺序：

1. 执行 `RepairAllWeaponAttackPowerValues`
2. 再执行一次 `CreateOrUpdateWeaponPackage`
3. 打开对应 `.tres` 确认 `attack_power` 在 `0-999`

---

## 7. 推荐工作流（团队）

每次新增武器建议按以下顺序：

1. 先做武器场景并配置 `AttackArea`
2. 用 `WeaponConfigToolkit` 一键创建资源
3. 运行 `ValidateAllWeaponItems`
4. 提交前再执行一次 `RepairAllWeaponAttackPowerValues`

这样可以最大程度避免“场景做了但资源绑定漏项”的问题。

---

## 8. 相关脚本

- `res://scripts/tools/WeaponConfigToolkit.cs`
- `res://scripts/items/attributes/ItemAttributeEntry.cs`

如需进一步升级为顶部菜单插件（无需挂节点），可在后续迭代中实现 EditorPlugin 入口。

---

## 9. 冲刺攻击配置（手动补充，可选）

工具生成的是武器基础资源；**冲刺攻击**（EnableDashMovement）是攻击模板（`PlayerAttackTemplate`）的可选行为，在 `main_character.tscn` 的武器攻击模板节点上手动配置。**无冲刺攻击的武器不配任何 Dash 字段（零耦合）**。

### 9.1 启用

在武器攻击模板节点（`StateMachine/Attack/<武器模板>`）勾选：

```gdscript
EnableDashMovement = true
```

### 9.2 字段说明

**模板节点**（`StateMachine/Attack/<武器模板>`）：

| 字段 | 默认 | 说明 |
|---|---|---|
| `EnableDashMovement` | false | 冲刺攻击总开关 |
| `AllowBackwardDashAttack` | false | 后撤闪避（dashback）后冲刺方向：true = 沿后撤方向（向后）；false = 反方向（面朝向前） |
| `RecoverySpeed` | 0 | 冲刺结束后 Recovery 阶段滑行速度（0 = 立即停） |
| `ContactShapePath` | 空 | 碰敌归零检测形状（可选）。空 = 默认玩家 `AttackArea/CollisionShape2D`——冲刺期间该形状碰到敌人 HitArea → 前冲速度立即归零 |

**武器技能定义**（`WeaponSkill_*.tres`，**支持 CSV 导出导入**）：

| 字段 | 默认 | 说明 |
|---|---|---|
| `DashAnimationName` | "" | 冲刺攻击动画名（Spine）。空 = 用普通攻击动画（`AnimationName`） |
| `DashWarmupDuration` / `DashActiveDuration` / `DashRecoveryDuration` | -1 | 冲刺动画的阶段时长（秒）。**-1 = 用普通阶段**。冲刺动画阶段与普通不同时必须配置，否则动画与阶段错位、hit 窗口错位（伤害丢失/错时） |
| `DashWarmupAnimationSpeed` / `DashActiveAnimationSpeed` / `DashRecoveryAnimationSpeed` | -1 | 冲刺动画各阶段播放速度（阶段计时按此缩放 + 全局攻速倍率）。**-1 = 用普通动画速度** |
| `DashDamageMultiplier` | -1 | 冲刺攻击伤害倍率。**-1 = 用普通 `DamageMultiplier`** |
| `RecoverySpeed` | 0 | 冲刺结束后 Recovery 阶段滑行速度（0 = 立即停） |
| `AllowBackwardDashAttack` | false | 后撤闪避（dashback）后冲刺方向：true = 沿后撤方向（向后）；false = 反方向（面朝向前） |
| `ContactShapePath` | 空 | 碰敌归零检测形状（可选）。空 = 默认玩家 `AttackArea/CollisionShape2D`——冲刺期间该形状碰到敌人 HitArea → 前冲速度立即归零 |

### 9.3 行为语义

- **惯性衰减（默认）**：Warmup 开始沿冲刺方向移动，速度从攻击前玩家速度（`CurrentMoveSpeed`）在 **Warmup 内**线性衰减到 0（Active 前归零）→ Recovery 按 `RecoverySpeed` 滑行
- **冲刺方向**：默认沿攻击前移动方向（含 Y）；不允许向后时——站立回退面朝、纯 Y 移动沿 Y、有 X 分量则面朝 X + 移动 Y
- **hit 数量差异**：Spine hit 事件驱动自动适配（每个动画自己的 hit 事件）——只需保证 Dash 阶段时长匹配冲刺动画
- **连击**：Recovery 打断重启时冲刺继承上一段衰减结果（不回到初始速度）

### 9.4 配置示例（BrawlRiotBracer）

```gdscript
[node name="BrawlRiotBracer" type="Node" parent="StateMachine/Attack"]
EnableDashMovement = true
DashSpeed = 2500.0          # Brawl 专属：固定冲刺速度（子类重载 ResolveDashStartSpeed）
RecoverySpeed = 0.0
ContactShapePath = NodePath("../../../AttackArea/CollisionShape2D")
```

Brawl 为**阶段前冲模式**（子类重载）：Warmup 停 → Active 固定 `DashSpeed` 匀速冲刺 → Recovery 滑行 → 碰敌归零。

### 9.5 动画配置约定

普通攻击动画与冲刺攻击动画**各自独立**（阶段/速度/hit 数量可以不同），**同属武器技能定义**（`WeaponSkill_*.tres`，CSV 可配）：

- 普通攻击动画：`WeaponSkillDefinition.AnimationName` + `WarmupDuration/ActiveDuration/RecoveryDuration` + 三阶段 `*AnimationSpeed`
- 冲刺攻击动画：`WeaponSkillDefinition.DashAnimationName` + `Dash*Duration` + `Dash*AnimationSpeed`（-1 = 用普通）

不需要为冲刺攻击新建独立的 `WeaponSkill_*_RunAttack.tres`——冲刺是同一武器攻击的变体，配置在同一技能定义内（数据驱动、CSV 可维护）。无冲刺攻击的武器技能 Dash 字段留空/-1 即可。

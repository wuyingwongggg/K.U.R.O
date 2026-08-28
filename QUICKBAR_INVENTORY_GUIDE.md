# QuickBar 背包（当前使用中的背包系统）指南

## 概述

当前游戏实际使用的背包 UI 是 **BattleHUD 左下角的 QuickBarPanel（快捷栏面板）**，常驻显示玩家携带的 5 个槽位物品。

**InventoryWindow（16 格物品栏窗口）已暂时放弃使用**（代码保留，不加载/不打开）。本文档描述当前在用的背包体系，其架构细节见 `SYSTEM_OVERVIEW.md` §2。

## 架构与容器归属

```
BattleHUD（UI 节点）
├── QuickBarPanel（快捷栏面板，常驻展示）
│   └── QuickBarContainer (HBoxContainer)
│       └── QuickSlot1..5（每个：SlotFrame 边框 + Icon + Label）
└── _quickBarContainer（InventoryContainer, 5 格）← BattleHUD.InitializeInventory() 创建
	 └── 经 SetQuickBar() 注入玩家 PlayerInventoryComponent.QuickBar

PlayerInventoryComponent（玩家组件）
├── QuickBar（↑ BattleHUD 注入，玩家操作的目标容器）
├── Backpack（玩家场景 "Backpack" 子节点或组件自建，默认 5 格）← AddItemSmart 溢出目标
├── FurnitureSlotStack（家具槽，隐藏第 6 格语义）
├── SpecialSlots（特殊装备槽：主武器 WeaponSlot）
└── SelectedQuickBarSlot（数字键切换）/ SelectedBackpackSlot（item_select 切换，兜底）
```

**已弃用**：BattleHUD 的 `_inventoryContainer`（16 格 "PlayerInventory"）——仅被 InventoryWindow 使用，InventoryWindow 停用后为死数据。

## 物品放入优先级（AddItemSmart）

1. **家具**（`IsFurniture`）→ 家具槽（不弹"新获得"介绍）
2. **武器** → 受 `MaxCarriedWeaponCount` 限制，且只放入已解锁槽位（`GetUnlockedWeaponSlots`，Build 升级解锁）
3. 快捷栏：选中槽（未被投掷预留）→ 同类合并 → 最左空槽（跳过预留槽）
4. **溢出 → Backpack**（5 格）

## 当前 UI 功能（BattleHUD.cs）

- 5 槽展示：图标 + 名称标签 + 空槽半透明（`UpdateQuickBarDisplay`/`UpdateQuickBarSlot`）
- 左右手选中高亮（`UpdateHandSlotHighlight` → SelectedFrameTexture 替换空白方块）
- 锁定槽：未解锁槽显示锁遮罩（`UpdateSlotFrames`，物品存在时不遮）
- 投掷武器冷却覆盖层（`ThrowCooldownOverlay`，读槽位 stack 的 `ThrowCooldownRemaining`）
- 数字键切换槽位（SamplePlayer `SwitchToQuickBarSlot`）
- 金币显示（GoldIcon/GoldLabel，与 InventoryWindow 无关）

## 交互现状

**槽位 `mouse_filter = Ignore`——快捷栏无任何鼠标交互**，仅键盘数字键 + 拾取/投掷流程间接改变内容。无法整理槽位顺序、无法丢弃、无法删除物品。

## 已知问题

1. **背包溢出物品无 UI**：`AddItemSmart` 溢出到玩家 Backpack（5 格），但 InventoryWindow 停用后 Backpack **没有任何展示界面**——背包满后多余物品"消失"（不可见、不可取回）
2. **16 格容器死数据**：BattleHUD 的 `_inventoryContainer` 仅 InventoryWindow 使用，停用后应清理（创建/信号/保存序列化引用）
3. **换位逻辑缺失**：快捷栏内物品无法重新排序（只能通过拾取/投掷间接移动）
4. InventoryWindow 遗留问题（若未来复用其逻辑需修复）：`ThrowCooldownRemaining` 换位丢失、PauseManager 计数泄漏、丢弃走完整投掷逻辑（含 `SpawnEffectOnThrow` 副作用）

## 实施步骤（分阶段，从底层开始）

### 阶段 0：解除 InventoryWindow 关联 + 底层清理

| # | 步骤 | 内容 | 涉及文件 |
|---|---|---|---|
| 0.0 | **解除 M 键入口** | 删除 BattleHUD 的 `LoadInventoryWindow` 调用与 `open_inventory` 分支（M 键不再打开物品栏窗口） | BattleHUD.cs |
| 0.1 | 清理互斥与加载路径 | SkillDetailWindow 的 `open_inventory` 互斥判断；UIManager 的 `LoadInventoryWindow` 调用点；16 格 `_inventoryContainer` 死容器（创建/信号/传参）一并删除 | SkillDetailWindow.cs、UIManager.cs、BattleHUD.cs |
| 0.2 | 背包溢出策略 | `AddItemSmart` 满时**拒绝 + 提示**（当前溢出到无 UI 的 Backpack，物品不可见） | PlayerInventoryComponent.cs |
| 0.3 | 换位原语修复 | 修复 `InventoryItemStack` 复制丢失 `ThrowCooldownRemaining`（为拖拽换位做准备） | InventoryItemStack.cs |

> InventoryWindow.cs/tscn 代码保留不删（"暂时放弃"），仅移除 M 键入口。

### 阶段 1：QuickBarPanel 独立化（架构拆分，行为不变）

> 先独立组件再叠交互：拖拽/丢弃逻辑与 UI 控件（槽位鼠标事件、拖拽预览）紧密耦合，先拆分场景与职责，交互在组件内部实现。

| # | 步骤 | 内容 | 涉及文件 |
|---|---|---|---|
| 1.1 | 独立场景 | 新建 `QuickBarPanel.tscn`：**快捷栏槽位区**（QuickBarContainer 5 槽 + 垃圾桶区域 + 确认对话框）+ **金币显示区**（GoldSection：图标 + Label）+ `QuickBarPanel.cs` | 新场景+脚本 |
| 1.2 | 展示逻辑搬运 | 把 BattleHUD 的槽位展示代码移入 QuickBarPanel.cs：图标/标签/空槽、左右手高亮（`UpdateHandSlotHighlight`）、锁定框（`UpdateSlotFrames`）、投掷 CD 覆盖层（`ThrowCooldownOverlay`）；**金币逻辑同步搬入**：`GoldChanged` 订阅/退订（原在 AttachActor/DetachActor）、`OnPlayerGoldChanged` 更新 Label | 新脚本 + BattleHUD.cs |
| 1.3 | BattleHUD 委托 | BattleHUD 删除对 `_quickSlotFrames/_quickSlotIcons/_quickSlotLabels` 与金币节点的直接操作 → 调用 QuickBarPanel 公开方法（`UpdateQuickBarSlot`/`UpdateHandSlotHighlight`/`SetGold`）；`_quickBarContainer` 创建与 `SetQuickBar` 注入不变；BattleHUD.tscn 内嵌 QuickBarPanel 实例并移除原 GoldIcon/GoldContainer | BattleHUD.cs、BattleHUD.tscn |

### 阶段 2：交互功能（在 QuickBarPanel.cs 内实现）

| # | 步骤 | 内容 | 涉及文件 |
|---|---|---|---|
| 2.1 | 拖拽交换 | 槽位开启 `mouse_filter`；拖拽状态机（开始/更新/结束、目标槽解析、同容器交换）——复用 InventoryWindow 逻辑但用 0.3 的**复制构造器**（CD/耐久/运行时属性不丢） | QuickBarPanel.cs |
| 2.2 | 拖出丢弃 | 拖到面板外 → 纯掉落：设置 `LastDroppedBy`、**不激活伤害判定**、排除 `SpawnEffectOnThrow`（丢弃回旋镖不应触发投掷效果）；配合 0.2 溢出拒绝策略 | QuickBarPanel.cs |
| 2.3 | 垃圾桶删除 | 垃圾桶热区 + 确认对话框（移植 `PerformDelete`/确认流程） | QuickBarPanel.cs |

### 阶段 3：验证

- `dotnet build` 0 error
- 游戏内：M 键无反应（旧窗口不再打开）→ 数字键/高亮/锁定/CD 覆盖层无回归（阶段 1 验收）→ 拖拽换位（CD 保留）→ 拖出丢弃（纯掉落、无伤害、回旋镖不触发效果）→ 垃圾桶删除（确认对话框）→ 背包满拒绝提示

**依赖链**：0.x 先行（解除关联/溢出策略/CD 修复）→ 1.x（先拆分组件，行为不变可回退）→ 2.x（组件内加交互）→ 3。每个阶段可独立编译验证。

## 文档关系

- `SYSTEM_OVERVIEW.md` §2 —— 背包架构权威（槽位体系/AddItemSmart/投掷预留），已与本会话代码同步
- 本文档 —— UI 展示层与交互现状、后续方向
- `MAINCHARACTER_SETUP.md` —— 玩家场景节点结构（ItemAttachment 等附件）

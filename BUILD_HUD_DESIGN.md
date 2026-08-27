# BuildSelectionWindow 伪 3D 卡片 HUD

## 实施步骤

### Step 1：创建 `BuildCard.tscn`（卡片场景模板）

新建：`scenes/ui/components/BuildCard.tscn` + `scripts/ui/BuildCard.cs`

#### .tscn 结构（编辑器预设）

```
BuildCard (Control)
  └── CardInner (Control, 全填充, mouse_filter=Pass)
		├── CardBg (TextureRect)               ← 卡片背景图，无 shader
		├── RarityGlow (TextureRect)           ← 稀有度光效
		├── Icon (TextureRect)
		├── KeyLabel (Label, "[1]")
		├── NameLabel (Label)
		├── BuildClassLabel (Label)
		├── DescLabel (Label)
		└── ProgressLabel (Label)
```

#### 运行时结构（_Ready 动态重组）

```
BuildCard (Control)
  ├── SubViewport (透明背景, size=卡牌尺寸)
  │     └── CardInner (全填充, 移入 SubViewport)
  │           ├── CardBg / RarityGlow / Icon (原始渲染)
  │           └── KeyLabel / NameLabel / ... (文字清晰)
  │
  └── DisplayRect (TextureRect + ShaderMaterial, 全填充)
		texture = SubViewport 的渲染画面
```

**原理**：CardInner 所有内容先渲染到 SubViewport 生成一张纹理，DisplayRect 用 `pseudo_3d_card.gdshader` 对整张纹理做透视投影。文字作为像素参与 shader 变换，不存在独立 Label 偏移/旋转/切碎的问题。

鼠标交互在 DisplayRect 上检测，shader 参数由 C# 根据鼠标相对于 DisplayRect 的位置计算。

#### BuildCard.cs 职责

- 暴露 `CardIndex`/`IsSelected`/`Enabled` 属性
- `_Ready`：重组节点树（SubViewport + DisplayRect），复制 ShaderMaterial 确保实例隔离
- `_Process`：鼠标位置 → `rot_y_deg`/`rot_x_deg` 角度（lerp 平滑），更新 shader 参数 + HoverScale pop
- `ApplyCardScale()`：按 `cardWidth / ReferenceSize.X` 等比缩放各 Label 的 `font_size`（`ratio` 上限 1、下限 `MinFontScaleRatio`；Desc 基准字号 12→9——缩小卡牌上长文本更易完整显示）
- `SyncViewportSize()`：卡牌 resize 后同步 SubViewport 尺寸
- `PlayEnterAnimation(delay)`：进入动画（屏幕下方飞入 Back-out），动画期间 `DisplayRect.MouseFilter=Ignore` 防误触

### Step 2：重构 `BuildSelectionWindow.cs` + `.tscn`

- 移除 Card0/Card1/Card2 硬编码 export
- 新增 `[Export] public PackedScene? CardTemplate`
- 新增 `[Export] public Control? CardContainer`（填卡片实例的容器）
- `PopulateOptions()`：`CallDeferred` 延迟执行 → 按 `_options.Count` 动态实例化 → 手动计算 Position/Size
- **固定卡宽布局**：`CardWidth` 导出（默认 400，≤ 容器宽 80% 防超屏）；`cardHeight = cardWidth × (340/260)`，高度受限时同步收窄卡宽（保持比例）
- **动态重叠**：`MaxOverlapRatio` 导出（默认 0.85）——数量少并排（step=卡宽），数量多自动重叠（`step = clamp(fitStep, 卡宽×0.15, 卡宽)`），组宽 ≤ 容器居中——不随数量拉伸/超屏
- `_cards` 从固定数组改为 `List<BuildCard>`

### Step 3：鼠标交互

- DisplayRect `MouseEntered`/`MouseExited` → `_isHovered` + `ZIndex`
- `_Process` → `pseudo_3d_card.gdshader`（透视 shader，rot_y_deg/rot_x_deg 驱动）
- `_GuiInput` → 左键单击 → `Selected` 信号（只切换选中高亮；卡片不直接确认——确认走操作栏按钮/键盘，见 Step 5）

### Step 4：键盘 + 选择高亮 + 动态卡牌数

- `ui_left/ui_right` 移动 `_selectedIndex`，`ui_accept` 确认当前选中（无选中则无事发生）；不再响应 `move_*`（战斗移动键残留）
- **焦点域导航**：`ui_down` 卡片区 → 按钮区（GrabFocus 按钮，原生 focus 样式即选中高亮，后续可换素材主题）；`ui_up` 回卡片区；`ui_left/ui_right` 在域内移动（卡片选中 / 按钮焦点循环）；按钮区 `ui_accept` 触发焦点按钮；鼠标点击任意位置焦点域重置回卡片区
- 动态快捷键：`Key.Key1 + N` → **只选中**第 N 张（不再直接确认——1-5 是战斗快捷栏键，残留输入只会高亮不会误选卡；数字键选中后焦点域回卡片区）
- `IsSelected` → `RarityGlow.Visible`
- `CardsPerSelection` export（范围 2~10）

## 涉及文件

| 文件 | 改动 |
|---|---|
| `scenes/ui/components/BuildCard.tscn` | 新建，CardInner + 子节点 |
| `scripts/ui/BuildCard.cs` | 新建，SubViewport 重组 + shader 驱动 + 字体缩放 |
| `scenes/ui/windows/BuildSelectionWindow.tscn` | 卡片容器改为 Control + CardTemplate export |
| `scripts/ui/BuildSelectionWindow.cs` | 动态 N 卡实例化 + CallDeferred + 等比尺寸 |
| `shaders/materials/pseudo_3d_card.gdshader` | 透视 shader（rot_y_deg/rot_x_deg/fov） |

## 实施状态

- Step 1-4 ✅ 已完成（BuildCard.tscn/cs + BuildSelectionWindow 重构 + 鼠标交互 + 键盘选择 + 进入动画 + 固定卡宽/重叠布局 + 字体缩小）
- 稀有度差异化 ✅ 已实现（蓝/紫/金/红卡贴图 + Glow 颜色按 Common/Rare/Epic/Core 配置在 BuildCard.tscn）
- **Step 5-5A** ✅ 确认制选择（单击/数字键只选中，再按一次取消选中；操作栏 [确认] 按钮 / `ui_accept` 确认；选中卡牌同样有悬停放大效果；键盘不受进入动画锁限制——选中操作无害，鼠标防误触由动画期 MouseFilter=Ignore 承担；**键盘焦点域导航**：上下切换卡片区/按钮区，左右域内移动，按钮区 `ui_accept` 触发焦点按钮，鼠标点击重置回卡片区）
- **Step 5-5B** ✅ 弃选获金币（操作栏 [跳过 +N] 按钮 / `Esc`；`SkipGoldReward` 导出默认 15；核心选择窗口同样可跳过，`CoreSkipGoldReward` 独立数值默认 30；金币局内保存）
- **Step 5-5C** ✅ 卡牌刷新（操作栏 [刷新] 按钮，位于确认左侧；`FreeRerollCount` 免费次数默认 1（可 `AddFreeRerollCount` 增加，供局外养成）+ `RerollBaseCost=10` × `RerollCostGrowth=1.5`^付费次数，窗口内递增每窗重置；免费时按钮无金币 icon，付费时显示；刷新排除当前显示的卡，候选不足时放回；金币不足按钮禁用；刷新不增加 `_triggerCount`）
- 待完成：
  - Step 5-5D：金币收入源（敌人/破坏掉落）
  - card 的 debug 日志清理

---

## Step 5（规划）：确认制选择 ✅ + 卡牌刷新 + 卡牌弃选（金币联动）

> 状态：**5A 确认制已实施**（单击选中、操作栏 [确认] 按钮 / Enter 确认、数字键只选中）。**5B 弃选已实施**（操作栏 [跳过 +N] 按钮 / Esc，`SkipGoldReward` 导出默认 15）。5C 刷新待实施。金币**局内保存**——退出主菜单或死亡时与 build 效果/武器一同清除（GameSaveData 不加 Gold 字段）。

### 设计变更

**交互流程（当前 → 目标）**

```
当前：弹窗 → 进入动画 → 点击卡牌 = 立即选取（误触风险：连续攻击残留输入直接选卡）
目标：弹窗 → 进入动画（不可交互）→ 选择阶段（点击=选中高亮）→ 操作栏 [刷新] [确认] [跳过]
```

- **点击卡牌不再立即确认**——只切换选中态（`IsSelected` 高亮，已实现 5A）
- **确认**：操作栏"确认"按钮 / 键盘 `ui_accept`/Enter——确认当前选中卡（5A 已实现；卡片本身不直接确认，避免绕过操作栏流程）
- **刷新（Reroll）**：操作栏按钮——重新从 EffectPool 抽一批卡（**不增加 `_triggerCount`**，本次升级已计数）
- **弃选（跳过）**：操作栏按钮 / `Esc`——放弃本次升级选择，获得 `SkipGoldReward` 金币（5B 已实现）

### 交互状态机

```
Entering（进入动画，锁输入——已实现 _cardsEntering）
  → Selecting（可选卡 + 操作栏可用）
	   ├── 点击卡 / 键盘左右 → 更新选中（IsSelected）
	   ├── 确认（ui_accept / 按钮）→ 确认选中卡 → onConfirmed(effect)
	   ├── 刷新（按钮 / 快捷键）→ 重新抽卡（次数-1，0 则按钮禁用）→ 重新 Populate（含进入动画）
	   └── 跳过（按钮 / 快捷键）→ onSkipped()
```

### 改动点

| 文件 | 改动 |
|---|---|
| `BuildCard.cs` | `_GuiInput` 单击**只发 `Selected`**（已实现 5A）；卡片不直接确认——确认由窗口操作栏按钮/键盘完成 |
| `BuildSelectionWindow.cs` | 新增操作栏（刷新/确认/跳过按钮 + 标签）；`RerollLimit` 导出（刷新次数，默认 1~2）；刷新逻辑：重新 `PickRandomEffects` → 重填（复用 Populate，但不触发计数/不重复进入动画时的输入锁） |
| `BuildSelectionWindow.tscn` | 新增操作栏节点（确认/跳过已加，5C 加刷新 + 剩余次数 Label） |
| 回调 | `ShowWindow` 增加 `onSkipped` 回调（已实现 5B；`onSkipped == null` 时跳过按钮隐藏——核心选择窗口） |

### 关键细节

- **刷新不增加 `_triggerCount`**：升级计数在 `CheckAndTriggerSelection` 已 +1——刷新只是换一批候选
- **刷新次数**：`RerollLimit` 导出（默认 1）——用完禁用刷新按钮（防无限刷）
- **刷新后的进入动画**：新一批卡也应飞入动画（输入锁复用 `_cardsEntering`）
- **弃选语义**：跳过 = 本次升级不获得效果——后续升级正常（不消耗额外次数）
- **防误触**：进入动画期间鼠标过滤（MouseFilter=Ignore，已实现）覆盖刷新后的重选；键盘只选中（5A 后选中无害，无需锁键盘）；操作栏按钮有 hover 反馈（同其他 Button）
- **键盘快捷键**（可选）：`R` 刷新、`Space` 确认、`Esc` 跳过

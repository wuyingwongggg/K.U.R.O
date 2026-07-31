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
- `ApplyCardScale()`：按 `cardWidth / ReferenceSize.X` 等比缩放各 Label 的 `font_size`
- `SyncViewportSize()`：卡牌 resize 后同步 SubViewport 尺寸

### Step 2：重构 `BuildSelectionWindow.cs` + `.tscn`

- 移除 Card0/Card1/Card2 硬编码 export
- 新增 `[Export] public PackedScene? CardTemplate`
- 新增 `[Export] public Control? CardContainer`（填卡片实例的容器）
- `PopulateOptions()`：`CallDeferred` 延迟执行 → 按 `_options.Count` 动态实例化 → 手动计算 Position/Size
- 卡牌尺寸等比缩放：`cardHeight = cardWidth × (340/260)`
- `_cards` 从固定数组改为 `List<BuildCard>`

### Step 3：鼠标交互

- DisplayRect `MouseEntered`/`MouseExited` → `_isHovered` + `ZIndex`
- `_Process` → `pseudo_3d_card.gdshader`（透视 shader，rot_y_deg/rot_x_deg 驱动）
- `_GuiInput` → 左键点击 → `Confirmed` 信号

### Step 4：键盘 + 选择高亮 + 动态卡牌数

- `ui_left/ui_right` 移动 `_selectedIndex`，`ui_accept/attack` 确认
- 动态快捷键：`Key.Key1 + N` → `ConfirmSelection(N)`，自动支持 N 张卡
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

- Step 1-4 ✅ 已完成（BuildCard.tscn/cs + BuildSelectionWindow 重构 + 鼠标交互 + 键盘选择）
- 待完成：
  - 稀有度差异化：RarityGlow 按 Common/Rare/Epic 换贴图/颜色
  - card 的 debug 日志清理

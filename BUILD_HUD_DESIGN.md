# BuildSelectionWindow 伪 3D 卡片 HUD

## 当前问题

[BuildSelectionWindow.cs](scripts/ui/BuildSelectionWindow.cs) 有 3 个硬伤：

1. **卡片数量写死 3** → Card0/Card1/Card2 各一套 export，未来 1 张/2 张/5 张场景无法扩展
2. **卡片无独立节点** → 伪 3D shader 需要每张卡独立的 ShaderMaterial，当前 VBoxContainer 挂不了
3. **稀有度无差异化** → StyleBoxFlat 换色无法表达 Common/Rare/Epic 的不同边框/光效/动画

## 实施步骤

### Step 1：创建 `BuildCard.tscn`（卡片场景模板）— 最底层

新文件：`scenes/ui/components/BuildCard.tscn` + `scripts/ui/BuildCard.cs`

```
BuildCard (Control, 锚点展开)
  ├── CardBg (TextureRect + ShaderMaterial)  ← pseudo_3d_card.gdshader
  │     shader_param/hovering = 0.0
  │     shader_param/mouse_screen_pos = Vector2(0, 0)
  │     shader_param/screen_scale = 1.0
  ├── RarityGlow (TextureRect)               ← 稀有度边框/光效，按 Rarity 换贴图
  ├── Icon (TextureRect)
  ├── KeyLabel (Label, "[1]")
  ├── NameLabel (Label)
  ├── BuildClassLabel (Label)
  ├── DescLabel (Label)
  └── ProgressLabel (Label)
```

`BuildCard.cs` 职责：
- 暴露 `Rarity`/`EffectId`/`IsHovered`/`IsSelected` 属性
- `_Process` 中检测鼠标是否悬停，更新 `hovering` + `mouse_screen_pos` shader 参数
- 稀有度切换时自动换 RarityGlow 贴图

### Step 2：重构 `BuildSelectionWindow.cs` + `.tscn`

- 移除 Card0/Card1/Card2 硬编码 export
- 新增：`[Export] public PackedScene? CardTemplate`
- 新增：`[Export] public Control? CardContainer`（填卡片实例的容器）
- `PopulateOptions()`：清空 CardContainer → 按 `_options.Count` 动态实例化 N 个 `BuildCard`
- `_cards` 从 `VBoxContainer[3]` 改为 `List<BuildCard>`

### Step 3：鼠标交互

`BuildCard.cs` 内部处理 `_Input` 或 `gui_input` 信号 → 更新 shader → 点击时触发 `Confirmed` 信号。

`BuildSelectionWindow` 订阅每个 `BuildCard.Confirmed` → `ConfirmSelection(index)`。

### Step 4：键盘保留 + 选择高亮

- `ui_left/ui_right` 移动 `_selectedIndex`，`ui_accept/attack` 确认 → `ConfirmSelection`
- `BuildCard.IsSelected = true` 时显示选中高亮效果（shader 额外参数或边框发光）

### 涉及文件

| 文件 | 改动 | 步骤 |
|---|---|---|
| `scenes/ui/components/BuildCard.tscn` | 新建 | Step 1 |
| `scripts/ui/BuildCard.cs` | 新建 | Step 1 |
| `scenes/ui/windows/BuildSelectionWindow.tscn` | 卡片容器改为动态加载 | Step 2 |
| `scripts/ui/BuildSelectionWindow.cs` | 动态实例化 N 张 BuildCard，移除 Card0-2 | Step 2 |
| `shaders/materials/pseudo_3d_card.gdshader` | 已有，不修改 | — |

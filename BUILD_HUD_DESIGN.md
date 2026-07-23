# BuildSelectionWindow 伪 3D 卡片 HUD

## 当前状态

[BuildSelectionWindow.cs](scripts/ui/BuildSelectionWindow.cs) + [BuildSelectionWindow.tscn](scenes/ui/windows/BuildSelectionWindow.tscn)：3 张 `VBoxContainer` 卡片，Icon + 名称 + 标签 + 描述。键盘 1/2/3 或 ←→ + Enter 选择。

## 目标效果

类似 Balatro 的伪 3D 卡片：鼠标悬停时卡片倾斜并产生光照反射，移开恢复平直。点击或键盘确认选择。

## 方案

### 1. 卡片节点改造

每张卡片从 `VBoxContainer` 替换为 `TextureRect`（或 `Control`）+ `pseudo_3d_card.gdshader`（项目中已有）：

```
Card0 (TextureRect)
  shader_material = ShaderMaterial(pseudo_3d_card.gdshader)
  shader_param/mouse_uv = Vector2(0.5, 0.5)    ← 每帧更新
  shader_param/card_texture = card_icon_texture
  └── VBoxContainer
        ├── Icon (TextureRect)
        ├── NameLabel
        ├── BuildClassLabel
        └── DescLabel
```

ShaderMaterial 放在卡片容器上作为背景，内容覆盖其上。

### 2. 鼠标驱动 3D 倾斜

每张卡片在 `_Process` 中把鼠标屏幕坐标转换为相对于卡片的 UV（0,0=左上，1,1=右下），写入 shader：

```csharp
private void UpdateCardHover(int cardIndex)
{
    var card = _cards[cardIndex];
    var mousePos = card.GetLocalMousePosition();
    var uv = new Vector2(
        mousePos.X / card.Size.X,
        mousePos.Y / card.Size.Y
    );
    var mat = card.Material as ShaderMaterial;
    mat?.SetShaderParameter("mouse_uv", uv);
}
```

Shader 内部根据 `mouse_uv` 计算旋转矩阵，产生倾斜 + 光照高光效果。

### 3. 点击选择

`_Input` 中增加 `InputEventMouseButton` 处理，检测点击位置落在哪张卡片上 → `ConfirmSelection(index)`。

### 4. 键盘保留

现有键盘逻辑不变：`ui_left/ui_right` 移动高亮，`ui_accept/attack` 确认。

### Shader 参数接口

| 参数 | 类型 | 说明 |
|---|---|---|
| `mouse_uv` | vec2 | 鼠标在卡片上的归一化位置 (0-1) |
| `card_texture` | sampler2D | 卡片正面纹理（Icon） |
| `tilt_strength` | float | 倾斜强度（默认 0.3） |
| `hover_scale` | float | 悬停时缩放（默认 1.05） |

### 涉及文件

| 文件 | 改动 |
|---|---|
| `scenes/ui/windows/BuildSelectionWindow.tscn` | 卡片节点替换为 TextureRect + ShaderMaterial |
| `scripts/ui/BuildSelectionWindow.cs` | 新增鼠标位置追踪、shader 参数更新、鼠标点击选择 |
| `shaders/materials/pseudo_3d_card.gdshader` | 已有，按参数接口检查是否需要小幅调整 |

# BuildCard 导出后文字不换行问题排查记录

> 结论先行：**spine-godot 自定义导出模板中，RichTextLabel 的 Word 逐词断行对无空格中文失效**，整段文字被当作一个不可分割的"词"排成一行，超出卡片边缘被裁切。解决方案：中文富文本改用 `AutowrapMode.Arbitrary`（按字符任意断行）。

## 问题现象

- **编辑器直接运行**：卡片描述（DescLabel）正常按宽度换行（2-3 行），显示完整
- **导出后运行**：DescLabel 只显示**第一行**，且第一行最后一个字**只显示一半**——超出卡牌边框的那半边被裁掉
- 注意区分：不是文字不显示（字形存在），而是**布局换行失败**导致的裁切

### 运行时数据对比（临时调试探针输出）

```
								  编辑器运行            导出运行
desc.Size（标签尺寸）             330.75 × 87.84        330.75 × 87.84   ← 一致
desc.FontSize（字号）             16                    16               ← 一致
ttfExists / ttfLoaded（字体）     True / True           True / True      ← 字体正常打包加载
textW（文本实际测量宽度）         896px                 896px            ← 字体度量一致
aw（自动换行模式）                Word                  Word             ← 配置一致
desc.Lines（实际行数）            **3**                 **1**            ← 唯一差异
```

所有布局条件完全相同，唯独导出版把 896px 宽的文本在 330px 宽的标签里排成了一行。

## 排查过程（被排除的假设）

按顺序排查并逐一用数据排除：

| 假设 | 结论 |
|------|------|
| 字体没打进导出包 | ❌ pck 含字体、`ttfLoaded=True` |
| 编辑器/导出用了不同系统回退字体 | ❌ 内置 Noto Sans SC 后测量宽度两边完全一致（896px） |
| 文本在入树前设置导致测量宽度无限 | ❌ 改为两阶段填充（先布局一帧再填文本）后无效 |
| `fit_content=true` 的测量行为差异 | ❌ 移除后无效 |
| 窗口/DPI/缩放差异 | ❌ window、rootSize、scaleFactor 全部一致 |
| SubViewport 尺寸失配 | ❌ viewport、inner、desc 尺寸全部一致 |
| 导出版没有 TextServerAdvanced | ❌ 两边都是 `ICU / HarfBuzz / Graphite (Built-in)` |

## 根因

spine-godot 的自定义**导出模板**与**编辑器构建**虽然同为 Godot 4.5.1、同 TextServer，但模板构建中 **Word 断行的词边界数据对 CJK 文本异常**：

- Word 模式依赖引擎的词边界算法决定换行点
- 中文没有空格，词边界完全依赖 Unicode 断词数据
- 导出模板中该数据缺失/异常 → 整段 56 个汉字被判定为一个"不可分割的词" → 无论如何都不换行 → 一行文字超出 330px 标签、超出 SubViewport 纹理边界，被视口边缘裁切（半字现象）

编辑器构建的断词数据正常，所以编辑器里表现正确——这是"编辑器正常、导出异常"类问题的典型形态。

## 解决方案

### 核心修复（BuildCard.cs）

```csharp
// _Ready() 中，ResolveExports() 之后：
// 导出版（spine 模板）的文本服务器对无空格中文断词异常：整段文字被当作
// 一个不可分割的"词"排成一行。中文描述改用按字符任意断行，视觉上与逐词断行一致。
if (DescLabel != null)
	DescLabel.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
```

- **Arbitrary 断行**：在任意字符处断行，不依赖词边界数据
- 对中文来说与 Word 断行的视觉效果基本一致（中文本无空格）
- **后续新增的中文 RichTextLabel 组件照此处理**（若含英文长词，可考虑 WordSmart + 自定义断词，但当前项目场景 Arbitrary 足够）

### 配套修复（同链路问题，一并保留）

| 修复 | 原因 |
|------|------|
| 内置 [NotoSansSC-VF.ttf](assets/fonts/)，设为 `gui/theme/default_font`（project.godot） | 消除编辑器/导出进程"系统字体回退解析不同字体"的隐患，两边渲染完全一致（OFL 许可可分发） |
| `_Ready` 预热 `GD.Load<FontFile>(Noto)` | 导出版字形异步加载，避免文本测量发生在字形就绪前 |
| SubViewport `RenderTargetUpdateMode = Always` | 导出版字形就绪前视口纹理可能只渲染一次就"冻结"，持续更新保证字形加载后补全 |
| `Resized` 时同步视口尺寸 + 字号 | 导出最大化窗口/DPI 差异导致视口与显示区域失配 |
| `PopulateOptions` 两阶段填充（先入树定尺寸等一帧，再填文本） | 保证 RTL 在最终宽度约束下测量，不依赖布局时序 |

## 排查方法论（供后续类似问题参考）

1. **先加运行时探针、两侧对比**：`GD.Print` 打印窗口/缩放/尺寸/行数/内容高度/文本服务器等关键值，编辑器与导出各跑一次，逐字段比对——分叉的字段就是根因方向
2. **一次只排除一个变量**：字体 → 时序 → fit_content → 尺寸 → 引擎行为，每一轮用数据说话
3. **"编辑器正常、导出异常"优先怀疑**：导出模板构建差异（本次根因）、字体回退解析差异、资源打包缺失、帧时序竞态
4. 导出控制台调试用 `Kuro.console.exe`（日志写入 exe 同级 `kuro_*.log`）

## 相关文件

- [scripts/ui/BuildCard.cs](scripts/ui/BuildCard.cs) — 断行修复 + 视口管线
- [scripts/ui/BuildSelectionWindow.cs](scripts/ui/BuildSelectionWindow.cs) — 两阶段填充
- [project.godot](project.godot) — 默认字体设置
- [assets/fonts/NotoSansSC-VF.ttf](assets/fonts/) — 内置中文字体

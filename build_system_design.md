# Machine 构筑卡牌系统

## 实施顺序（6 个 Phase，增量可测试）

---

### Phase 1：数据层 — BuildEffectDefinition / BuildCoreDefinition 新字段

**只加字段，不改逻辑**

1. `BuildEffectDefinition.cs` 新增：
   - `BuildBranch` (string)
   - `BuildRarity` 枚举 (Common, Rare, Epic)
   - `Rarity` (BuildRarity)
   - `Weight` (int)
   - `StackBonusValues` (string, 逗号分隔如 "5,10,15")
   - `MaxStacks` (int, 0=自动推导)
   - `EffectScene` → `EffectEntries` (Array\<AttackEffectEntry\>)

2. `BuildCoreDefinition.cs` 新增：
   - `AllowedEffectClasses` (Array\<string\>，如 ["Machine", "Generic"])

3. 18 个 `resources/builds/BuildMachine_*.tres` 补上新字段（从 CSV 数据填充）

4. 3 个 `resources/builds/core/*.tres` 补上 `AllowedEffectClasses`

**验证**：dotnet build + 打开 .tres 检查 Inspector 字段可见，编译时 `AttackEffectEntry` 引用正确

**涉及文件**：
- `scripts/systems/BuildEffectDefinition.cs`
- `scripts/systems/BuildCoreDefinition.cs`
- `resources/builds/BuildMachine_*.tres` (18 个)
- `resources/builds/core/*.tres` (3 个)

---

### Phase 2：卡牌选择逻辑 — BuildSelectionManager

**依赖 Phase 1 数据到位**

1. `PickRandomEffects()` 改为加权随机：
   - 按 activeCore 的 `AllowedEffectClasses` 过滤
   - 按 `effectiveWeight = Weight * RarityMultiplier[rarity]` 加权抽样
   - 排除 `_pickedEffectIds[effectId] >= effect.MaxStacks` 的卡牌
   - Shuffle 取 count 个

2. 新增 export `RarityMultiplier` Dictionary：
   ```csharp
   [Export] public Godot.Collections.Dictionary<string, float> RarityMultiplier { get; set; }
   ```
   默认：`{"Common": 3.0, "Rare": 1.0, "Epic": 0.3}`

3. `ApplyEffectBonuses()` 中 EffectScene 逻辑改为遍历 EffectEntries

**验证**：加权随机分布合理、MaxStacks 到期卡牌不再出现、MachineCore 下不出现 Waiter/Throw 卡牌

**涉及文件**：
- `scripts/managers/BuildSelectionManager.cs`

---

### Phase 3：效果应用 — BuildStatBonusEffect 堆叠

**依赖 Phase 1 数据到位，Phase 2 堆叠追踪完成**

1. `BuildStatBonusEffect.OnApply()` 从调用方接收当前 stackCount
2. 解析 `StackBonusValues`，按 `stackCount - 1` 索引取值
3. 计算 `effectiveValue = baseValue * (1 + tierValue / 100)`
4. `BuildSelectionManager.ApplyEffectBonuses()` 传入 stackCount

**验证**：同一卡牌选第 2、3 次时 Stat 增幅递增（5% → 10% → 15%）

**涉及文件**：
- `scripts/core/effects/BuildStatBonusEffect.cs`
- `scripts/managers/BuildSelectionManager.cs`（小改，传入 stackCount）

---

### Phase 4：CSV 导出 — ExportCsv.gd

**依赖 Phase 1 数据到位**

1. 新增 `_export_builds()` 方法
2. 读取 `resources/builds/*.tres`
3. 序列化 `EffectEntries` 的 Scene 路径 + PropertyOverrides 文本
4. 输出 `data/builds.csv`，列头动态扩展 EffectScene_N / EffectOverrides_N
5. `_run()` 中补上 `_export_builds()` 调用

**验证**：运行导出 → `data/builds.csv` 生成，字段完整

**涉及文件**：
- `scripts/tools/ExportCsv.gd`

---

### Phase 5：CSV 导入 — ImportCsv.gd

**依赖 Phase 4 CSV 格式稳定**

1. 新增 `import_builds_from_csv()` 方法
2. 读取 `data/builds.csv`
3. 解析 EffectScene_N / EffectOverrides_N 列，重建 `AttackEffectEntry` 数组
4. 回写对应 `.tres` 文件
5. `import_all()` 中补上 `import_builds_from_csv()` 调用

**验证**：修改 CSV 数值 → 运行导入 → `.tres` 文件字段更新

**涉及文件**：
- `scripts/tools/ImportCsv.gd`

---

### Phase 6：18 张 Machine 卡牌的 .tres 批量生成

**依赖 Phase 1-3 完成**

1. 从 `新构筑machine卡牌效果.csv` 批量生成 18 个 `BuildMachine_*.tres`
2. 每个 `.tres` 填充：EffectId, DisplayName, BuildBranch, Rarity, Weight, StackBonusValues
3. EffectEntries 暂时留空（需要 Phase 3 完成后逐个挂载效果场景）

**验证**：18 个 .tres 在 Inspector 中字段正确，BuildSelectionManager 能加载并随机选卡

**涉及文件**：
- `resources/builds/BuildMachine_*.tres` (18 个，新建或覆盖)

---

### 依赖关系图

```
Phase 1 (数据层)
  ├── Phase 2 (选择逻辑)
  │     └── Phase 3 (效果堆叠)
  ├── Phase 4 (CSV 导出)
  │     └── Phase 5 (CSV 导入)
  └── Phase 6 (.tres 批量生成)
```

Phase 2 和 Phase 4 可以并行。Phase 3 依赖 Phase 2。Phase 5 依赖 Phase 4。Phase 6 可最早开始（与 Phase 2 并行），涉及文件最多但逻辑最简单。

### 总计

| Phase | 改动文件数 | 复杂度 |
|---|---|---|
| 1 | ~25 个（2 C# + 18 tres + 3 core tres） | 低（纯加字段） |
| 2 | 1 个 C# | 中（核心算法） |
| 3 | 2 个 C# | 低（数学计算） |
| 4 | 1 个 GDScript | 中（序列化逻辑） |
| 5 | 1 个 GDScript | 中（反序列化逻辑） |
| 6 | 18 个 .tres | 低（机械化生成） |

# Machine 构筑卡牌系统

## 实施顺序（6 个 Phase，增量可测试）

---

### Phase 1：数据层 — 字段定义 + 适配现有逻辑

**✅ 已完成**

1. `BuildEffectDefinition.cs`：
   - 新增 `BuildRarity` 枚举 (Common, Rare, Epic)
   - 新增 `Rarity` (BuildRarity)
   - 新增 `Weight` (int)
   - 新增 `MaxStacks` (int)
   - `EffectScene` → `EffectEntries` (Array\<AttackEffectEntry\>)
   - 移除 `StatBonuses`（统一进 EffectEntries 的 PropertyOverrides）
   - 移除 `BuildBranch`（冗余，EffectId 前缀已编码流派）
   - ExportGroup 改为英文

2. `BuildCoreDefinition.cs`：
   - 新增 `AllowedEffectClasses` (Array\<string\>，如 ["Machine", "Generic"])

3. `BuildSelectionManager.cs`（适配新字段，已随 Phase 1 完成）：
   - 所有 `effect.EffectScene` → 遍历 `effect.EffectEntries` + `entry.InstantiateEffect()`
   - 移除 `effect.StatBonuses.Count > 0` 代码路径（两处：ApplyEffectBonuses + RestoreBuildState）
   - 新增 `using Kuros.Actors.Enemies.Attacks` 引用

4. 待后续完成：18 个 `resources/builds/BuildMachine_*.tres` + 3 个 `resources/builds/core/*.tres` 补上新字段（Phase 6）

**验证**：dotnet build 0 error，Inspector 新字段可见

**涉及文件**：
- `scripts/systems/BuildEffectDefinition.cs` ✅
- `scripts/systems/BuildCoreDefinition.cs` ✅
- `scripts/managers/BuildSelectionManager.cs` ✅

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

**验证**：加权随机分布合理、MaxStacks 到期卡牌不再出现、MachineCore 下不出现 Waiter/Throw 卡牌

**涉及文件**：
- `scripts/managers/BuildSelectionManager.cs`

---

### Phase 3：效果应用 — BuildStatBonusEffect 堆叠

**依赖 Phase 1 数据到位，Phase 2 堆叠追踪完成**

1. 第 1 次选卡：实例化 `EffectEntries`，套用每个 entry 的 PropertyOverrides
2. 第 N 次选卡：调用已有 effect 的 `Refresh(stackCount)`，effect 内部按 stackCount 自行缩放
3. `MaxStacks` 到期 → Phase 2 已在 PickRandomEffects 中排除

**验证**：同一卡牌选到第 N 次时效果数值递增

**涉及文件**：
- `scripts/core/effects/BuildStatBonusEffect.cs`（OnStackRefreshed 内部处理缩放）
- `scripts/managers/BuildSelectionManager.cs`（ApplyEffectBonuses 传入 stackCount）

---

### Phase 4：CSV 导出 — ExportCsv.gd

**依赖 Phase 1 数据到位**

1. 新增 `_export_builds()` 方法，读取 `resources/builds/*.tres`
2. 先扫一遍所有卡牌取最大 `EffectEntries` 数量 N，动态生成列头
3. CSV 列：
   ```
   file, EffectId, DisplayName, Description, BuildClass, Rarity, Weight, MaxStacks,
   EffectScene_1, EffectOverrides_1, ..., EffectScene_N, EffectOverrides_N
   ```
4. PropertyOverrides 序列化格式：`"key1=val1;key2=val2"`（`;` 分条目 → `=` 拆 key:value）
5. 每个 entry 独立一列，不存在的 entry 留空
6. `_run()` 中补上 `_export_builds()` 调用

**验证**：运行导出 → `data/builds.csv` 生成，列头按最大 N 动态扩展，PropertyOverrides 可读

**涉及文件**：
- `scripts/tools/ExportCsv.gd`

---

### Phase 5：CSV 导入 — ImportCsv.gd

**依赖 Phase 4 CSV 格式稳定**

1. 新增 `import_builds_from_csv()` 方法，读取 `data/builds.csv`
2. 按列名 `EffectScene_N` / `EffectOverrides_N` 匹配，重建 `AttackEffectEntry` 数组
3. EffectOverrides 反序列化：`"key1=val1;key2=val2"` → split `;` → split `=` → `effect.Set(key, value)`
4. 回写对应 `.tres` 文件
5. `import_all()` 中补上 `import_builds_from_csv()` 调用

**验证**：修改 CSV 的 EffectOverrides 值 → 运行导入 → `.tres` 中 EffectEntries 的 PropertyOverrides 同步更新

**涉及文件**：
- `scripts/tools/ImportCsv.gd`

---

### Phase 6：18 张 Machine 卡牌的 .tres 批量生成

**依赖 Phase 1-3 完成**

1. 从 `新构筑machine卡牌效果.csv` 批量生成 18 个 `BuildMachine_*.tres`
2. 每个 `.tres` 填充：EffectId, DisplayName, BuildClass, Rarity, Weight, MaxStacks
3. EffectEntries 按卡牌逐个挂载效果场景（Scene + PropertyOverrides）

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

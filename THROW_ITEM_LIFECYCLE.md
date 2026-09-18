# 投掷道具生命周期

> 覆盖：一次性投掷家具 / 投掷武器从"槽位 → 世界实体 → 飞行 → 落地"的完整状态流，
> 以及跨周期（拾取→放置→再投掷）需要携带的运行时状态。
> 主要代码：`PlayerItemInteractionComponent.TryHandleDrop`、`RigidBodyWorldItemEntity`、
> `PlayerInventoryComponent`（家具槽）、`ThrowCoreEffect`（件生成）、构筑卡 `ThrowRecallEffect` 等。

---

## 1. 载体与槽位：同一个道具在"栈"与"实体"之间来回换壳

| 阶段 | 承载者 | 说明 |
|---|---|---|
| 在背包里 | `InventoryItemStack` | 家具→**家具槽** `FurnitureSlotStack`（只能 1 件）；投掷武器→快捷栏/背包 |
| 出手 | **新世界实体** | `WorldItemSpawner.SpawnFromStack(extracted)`：从栈**现场生成**一件实体并 `ApplyThrowImpulse` |
| 落地销毁/归还 | 实体 `QueueFree` | 一次性道具销毁；武器回到原槽位并销毁实体 |
| 捡回 | 写入**新栈** | 实体销毁前把状态写回家具槽栈 |

**⚠️ 最重要的坑：每次拾取/提取都会新建栈。**
- `PlayerInventoryComponent.AddFurnitureItem`：`FurnitureSlotStack = new InventoryItemStack(item, 1)`（拾取时新建）
- `PlayerInventoryComponent.TryExtractFromFurnitureSlot`：`extracted = new InventoryItemStack(...)`（出手时新建副本）

因此任何"跨投掷周期需要保留的状态"都必须**显式搬运**。目前共三项（都在 `InventoryItemStack`）：

| 标记 | 含义 | 出手搬运点 | 拾取写回点 |
|---|---|---|---|
| `RuntimeSourceTag` | 投掷核心"件"身份 | `TryExtractFromFurnitureSlot` | `RigidBodyWorldItemEntity.TryPickupByActor` |
| `RuntimeIsThrowCoreCopy` | A_007 复制件身份（恢复乱码滤镜） | 同上 | 同上 |
| `RuntimeThrowCountUsed` | 投掷耐久已用次数（B_006 对象撤回） | 同上 | 同上 |

> 漏搬一个 → 静默丢状态（例如"投掷耐久永远从 0 开始"）。

**投掷武器不同**：出手时**不从背包提取**（`extractedFromInventory = false`，原 stack 留在快捷栏），
飞行的是临时副本 `IsDisposableCopy = true`；CD 记在原栈上并**挪到实体**（`ThrowCooldownRemaining`）。

---

## 2. 世界实体状态机（`RigidBodyWorldItemEntity`）

```
spawn → ApplyThrowImpulse(方向 × ThrowImpulse)
  │   置位：_isThrown、_inFlight、_impactArmed；ZIndex = ItemDefinition.ThrowZIndex（默认 3）
  │        判定盒/影子投影到地面行；root 位置**停在投掷起点不跟随**
  ▼
飞行（脚本抛物线，非物理；水平速度 = 距离 / 时长，时长取档位 ThrowDuration）
  ├─ 命中敌人（StopOnHit 且未携带 B_008 穿透）→ 结算伤害段 → 停止
  ├─ 撞 AirWall（CheckWallHit，按名 "AirWall"）→ 停止
  └─ 飞尽（phase ≥ 1）→ 落点 AoE 伤害（一次性道具）→ 停止
  ▼
停止收尾（三个等价入口：落地 / 命中停止 / 回弹自然停止）
  ├─ BounceAfterStop → _bouncing（脚本回弹：重力积分 + 地面行反弹衰减）
  │    · 地面行 = _bounceGroundY = _throwJudgmentY + LandingOffsetYDelta（本件自己的落点行，
  │      A_008 分裂件各行不同；写死基准行会把偏移件拽回去）
  │    · 水平方向分两种：撞击停止 → **反方向**弹开（撞墙/撞人的原语义）；
  │      完整抛物线干净落点 → **顺势向前**滚（StartBounce(forwardHorizontal: true)）
  │    · B_006 保留件在"完整抛物线干净落点"也走这一段（否则干净落点是直接定格，没有物理感）
  └─ 直接进入计时段
  ▼
_landingHideTimer = LandingHideDelay
  ├─ 一次性道具 → DestroyItemAtLanding → 销毁动画 → Destroy()
  │      （OnThrowDestroy 特效链 + Destroyed 广播 → A_004 冲击投放爆炸）
  └─ 投掷武器  → HideItemAtLanding → _inventoryReturnTimer = ThrowWeaponCooldown
                 → ReturnToInventory（回投掷前预占槽位）
```

**判定层 vs 视觉**：视觉由内部 `RigidBody2D` 承载（位置/抛物线真实位置），
判定盒与影子被投影到"投掷者地面行"`_throwJudgmentY`（A_008 分裂件的判定行随 phase 漂移到各自终点行）。
→ 所以**飞行期间 root 节点位置是过期的**（还是在投掷起点）。

**图层**：出手时 `ZIndex` 被覆写为 `ThrowZIndex`（默认 3，"飞行途中在最上层"）；
家具场景根节点是 2。**落地后仍活着的实体必须还原**（见 §5）。

---

## 3. 组语义（入组时机决定卡的联动）

| 组 | 何时入组 | 谁在消费 |
|---|---|---|
| `throwcore_piece_identity`（件身份） | 直接生成 / 放置 / 投掷 **都入** | A_004 摧毁爆炸的过滤条件；B_006 保留判定 |
| `throwcore_generated_furniture`（脉冲组） | **仅放置时**入组 | 长按核心键清场、A_002 脉冲扩散、A_003 节点串联（都按此组枚举） |
| `throwcore_copy_furniture` | A_007 复制件 | 拾取→放置恢复乱码滤镜 |

投掷件**不恢复脉冲**是原设计（投掷件落地即销毁，不需要在场身份）；
B_006 让投掷件落地保留后，收尾时才补入脉冲组（见 §5）。

---

## 4. 拾取门控（三层，缺一就会"按了没反应"）

1. `IsInThrowLifecycle`（`_isThrown || _inFlight || _bouncing || _landingHideTimer > 0 || _inventoryReturnTimer > 0`）
   —— "投掷全周期"的统一定义，也是"何时还不能捡"的唯一真源。
2. `IsPickupAvailable = !_isPicked && !_isDestroying && !IsInThrowLifecycle`
   —— **候选筛选**用：`PlayerItemInteractionComponent` 的两条选取路径（交互区重叠 / 距离兜底）都按它过滤。
   不过滤的后果：飞行中的道具会抢占"最近可拾取"位置，随后被第 3 层拒绝 → 拾取**静默失败**。
3. `TryPickupByActor`
   —— **硬门控**：同样拒绝投掷全周期；通过后走 `TryTransferToActor`（家具→`AddItemSmart`→家具槽）并写回 §1 的运行时标记。

---

## 5. B_006「对象撤回」的接入点（投出不再销毁）

| 环节 | 位置 | 行为 |
|---|---|---|
| 出手 | `ThrowRecallEffect.OnPieceThrown` | 仅对**本次投掷原件**置 `KeepAfterLanding = true`、`KeepThrowLimit = TierValues[当前层]`（层1/层2 = 2/3 次；分裂件不经 `PieceThrown`，故不标记） |
| 落地判定 | `RigidBodyWorldItemEntity.ShouldKeepAfterLanding` | `KeepAfterLanding && !IsThrowWeapon && ThrowCountUsed < KeepThrowLimit` |
| 收尾 | `FinalizeKeptAfterLanding()` | 还原飞行投影/碰撞/阴影 → **`SyncRootToBody()`** → **`ZIndex = _initialZIndex`** → 清投掷标记 → 补入脉冲组 → 变成普通可拾取世界物 |
| 耐久 | `ThrowCountUsed`（实体）/ `RuntimeThrowCountUsed`（栈） | 出手 +1、拾取写回；第 N 次投掷照常结算，落地走正常销毁 |

**为什么收尾要做这三件事**（都是踩过的坑）：
- `SyncRootToBody()`：root 位置飞行期不跟随，而高亮/拾取距离/遮挡判定都用 root 位置 → 不同步会"站在道具旁没高亮、站在投掷起点却提示可拾取"。
- 还原 `ZIndex`：不还原则保留件永远盖在所有家具之上（图层与放置件不一致）。
- 补入脉冲组：保留件从此是"在场核心件"，长按清场/A_002/A_003 都该认它（仅补有件身份的件，天然家具扔出后不补）。

---

## 6. 快速自检清单（改投掷相关逻辑时逐条过）

- [ ] 新增的跨周期状态，是否在 `TryExtractFromFurnitureSlot` **和** `TryPickupByActor` 两处都搬运了？
- [ ] 新增的物体是否会被 `IsPickupAvailable` 误判（飞行中被选成"最近可拾取"）？
- [ ] 落地后仍存活的实体：root 位置、`ZIndex`、碰撞、影子/判定盒投影 是否都还原了？
- [ ] 组：该实例此刻应该在"件身份"组，还是"脉冲"组（在场件）？
- [ ] 销毁路径（`DestroyItemAtLanding` / `RequestDestroy` / 攻击破坏）是否与 A_004、B_007 的过滤条件一致？

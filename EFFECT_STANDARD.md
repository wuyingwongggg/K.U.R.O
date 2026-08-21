# 效果统一规范

## 核心原则

1. **效果不关心目标身份。** 变量名用 `target`/`actor`，禁止用 `enemy`/`player`。目标是谁由调用方或配置决定，不写死在效果内部。
2. **所有外部效果通过统一入口作用于 GameActor。GameActor 自身决定接受还是拒绝。**

```
效果（伤害/击退/眩晕/...）
  → 统一入口（GameActor 公开方法）
	→ GameActor 内部守门（CanBeAffected / ActiveImmunities）
	  → 执行
```

---

## 一、三道守门：按优先级

### 1. CanBeAffected(effect) — 角色级条件免疫

```csharp
// GameActor.cs
public virtual bool CanBeAffected(ActorEffect? effect) => true;
```

- `effect != null`：效果系统调用（`ApplyEffect`）
- `effect == null`：直接伤害调用（`TakeDamage`）
- 子类覆写实现"非眩晕无敌"等逻辑

### 2. ActiveImmunities — 类型级永久免疫

```csharp
public ImmunityFlags ActiveImmunities { get; set; }
```

| Flag | 阻塞内容 |
|------|---------|
| `Stun` | FreezeEffect 施加 |
| `ForcedMovement` | 所有击退/位移 |
| `SpeedSlow` | 减速效果 |
| `ThrowableDamage` | 投掷物直接伤害 |
| `NonThrowableDamage` | 非投掷物伤害 |

在效果入口统一检查，不依赖效果内部自行判断。

### 3. IsDead / IsDeathSequenceActive — 生死门

`TakeDamage` 和 `ApplyEffect` 入口已内置，效果无需重复检查。

---

## 二、两种效果模式

### 模式 A：命中触发型（DamageEventBus）

效果监听 `DamageEventBus.OnDamageResolved(attacker, target, ...)`，天然目标无关。

```
宿主打谁 → 效果就挂谁身上，不需要知道对方阵营
```

**规则：**
- 检查 `attacker == Actor`（确保是宿主打的）
- 对 `target` 执行效果
- **变量名用 `target`，禁止用 `enemy` / `player`**

**示例：** `DotBleedEffect`、`SlowOnHitEffect`、`KnockbackOnAttackEffect`

### 模式 B：区域扫描型（IntersectShape / Area2D 信号）

效果通过物理引擎发现目标。**禁止 `GetNodesInGroup`，统一用 `IntersectShape` 或 `Area2D` 信号。**

```csharp
// 同步形状查询
var query = new PhysicsShapeQueryParameters2D
{
	Shape = shape,
	Transform = shape.GlobalTransform,
	CollisionMask = TargetCollisionMask,     // 导出配置，不写死
	CollideWithAreas = true,
	CollideWithBodies = false
};
spaceState.IntersectShape(query);

// 或信号驱动
area.BodyEntered += OnBodyEntered;
```

碰撞掩码通过 `[Export(PropertyHint.Layers2DPhysics)] uint TargetCollisionMask` 配置：

| 影响对象 | 值 |
|---------|-----|
| 只影响敌人 | `2`（Layer 2） |
| 只影响玩家 | `4`（Layer 3） |
| 同时影响两者 | `6`（2\|4） |

### 迁移清单（仍用 GetNodesInGroup 的效果）

| 效果 | 当前方式 | 应改为 |
|------|---------|--------|
| BoomDmgEffect | `GetNodesInGroup` + `DistanceTo` | `IntersectShape` + `CircleShape2D(Radius)` |
| DiscoFlashStunEffect | `GetNodesInGroup` + `DistanceSquaredTo` | `IntersectShape` |
| BlackHoleEffect 吸引 | `GetNodesInGroup` + `DistanceTo` | `IntersectShape` |
| SoundWaveEffect | `GetNodesInGroup` + `DistanceTo` | `IntersectShape` |
| TeleportStrikeEffect | `GetNodesInGroup` + `DistanceToSegment` | `IntersectShape` |

### 当前实施难点：WorldItem 的 IntersectShape 检测

以上效果迁移至 `IntersectShape` 时，WorldItem 阵营无法被正确检测。根因是 WorldItem 的碰撞结构不统一：

| WorldItem 类型 | 基类 | 碰撞方式 | IntersectShape 问题 |
|---|---|---|---|
| WorldItemEntity | `CharacterBody2D` | `BodyCollisionLayer = 0`（身体碰撞层关闭），通过 `TriggerArea`（Area2D, layer 2）交互 | Body 查询找不到（层 0），Area 查询需沿树找 `world_items` 组 |
| RigidBodyWorldItemEntity | `Node2D` | Area2D 子节点 | 非物理体，Body 查询完全找不到 |
| DestructibleObject | `StaticBody2D` 子节点 | `StaticBody2D` + CollisionShape2D | 找到的是子节点 `StaticBody2D`，需沿树向上找 `world_items` 组成员 |

**待解决**：是否接受"用 `GetNodesInGroup("world_items")` 处理 WorldItem，其余阵营走 `IntersectShape`"的混合方案，还是为 WorldItem 统一碰撞层后全部迁移。

### WorldItem 阵营的现状（2026-08 定稿）

`TargetableFactions.WorldItem` 的实际语义 = **只对 DestructibleObject（可破坏物）生效**：
- `DestructibleObject`：有 `TakeDamage(float)` 接口（HP/摧毁），StaticBody2D 在层 1 → 可被伤害
- `WorldItemEntity` / `RigidBodyWorldItemEntity`（掉落物）：无 `TakeDamage` → `ResolveDamageReceiver` 解析不出伤害接收者，**免疫伤害**（掉落物不该被打碎）

因此配置 `WorldItem` flag 时实际命中目标只有可破坏物；掉落物即使物理层被检测到也会被伤害结算拒绝。

---

### 场景根节点类型规范

| 效果类型 | 根节点 | 继承 | 应用路径 |
|---------|--------|------|---------|
| 逻辑效果（眩晕/DOT/Buff/击退） | `Node` | `ActorEffect` | `ApplyEffect` → EffectController |
| 世界生成效果（爆炸/投射物/区域） | `Node2D` | `Node2D` 或子类 | `SpawnSingleEffect` → world |

- `Node` 根的效果不需要世界坐标，挂载在 GameActor 下由 `EffectController` 管理生命周期
- `Node2D` 根的效果需要 `GlobalPosition` 定位，走 `SpawnSingleEffect` 的世界生成路径
- **禁止混用**：`ActorEffect` 子类用 `Node2D` 根会导致坐标失效，`Node2D` 脚本用 `Node` 根会导致 Godot 类型错误

### 已知设计债务：WorldActorEffect

`ActorEffect` 继承 `Node`，导致需要世界坐标的效果（如 `SlowHitAreaEffect`）无法同时拥有 `ActorEffect` 的生命周期管理和 `Node2D` 的位置能力。当前通过 `IWorldSpawnable` 接口手动管理位置——这是绕过 C# 单继承限制的临时方案。

**待实施**：新增 `WorldActorEffect : Node2D` 基类，继承 `ActorEffect` 的核心逻辑（`EffectController` 交互、`Duration`、`OnTick`），但根类型为 `Node2D`。实施后：

| 基类 | 根节点 | 用途 |
|---|---|---|
| `ActorEffect` | `Node` | 纯逻辑：眩晕/减速/DOT/Buff |
| `WorldActorEffect` | `Node2D` | 世界效果：区域伤害/投射物/爆炸 |

`SlowHitAreaEffect` 从 `ActorEffect + IWorldSpawnable` 改为继承 `WorldActorEffect`，删除接口 hack。

---

## 三、伤害：只走 DamageDispatcher

### 禁止

```csharp
actor.TakeDamage(damage);  // 绕过阵营过滤和自伤保护
```

### 统一使用

```csharp
// 有 Area2D
DamageDispatcher.DealDamageFromArea(area, damage, attacker, factions, allowSelfDamage);

// 有具体目标
bool dealt = DamageDispatcher.DealDamage(target, damage, origin, attacker, source, factions, allowSelfDamage);
if (!dealt) return;  // 阵营不匹配时不继续
```

详见 `DAMAGE_DISPATCHER_SYSTEM.md`。

---

## 四、击退：只走 GameActor.ApplyKnockback

### 新增：统一击退入口

```csharp
// GameActor.cs
public void ApplyKnockback(Vector2 direction, float speed)
{
	if (ActiveImmunities.HasFlag(ImmunityFlags.ForcedMovement)) return;
	Velocity = direction * speed;
}
```

### 迁移：所有击退点改为调用此方法

| 当前 | 改为 |
|------|------|
| `enemy.Velocity = beamDir * knockSpeed` | `enemy.ApplyKnockback(beamDir, knockSpeed)` |
| `actor.Velocity = knockbackVelocity` | `actor.ApplyKnockback(direction, speed)` |
| `mainCharacter.Velocity = knockbackVelocity` | `mainCharacter.ApplyKnockback(dir, speed)`（玩家走覆写） |

`KnockbackDriver`（`KnockbackOnAttackEffect` 内部）保留，因其需要 `MoveAndCollide` 做碰撞响应，但 `Attach` 入口也加 `ForcedMovement` 检查。

---

## 五、效果应用：只走 GameActor.ApplyEffect

```csharp
// GameActor.cs
public void ApplyEffect(ActorEffect effect)
{
	if (!CanBeAffected(effect)) return;
	EffectController?.AddEffect(effect);
}
```

已内置 `CanBeAffected` 守门。施加效果的代码直接调 `ApplyEffect`，不需要自己判断目标状态。

---

## 六、命名规范

| 禁止 | 正确 |
|------|------|
| `enemy`、`_enemy` | `target`、`actor` |
| `capturedEnemy` | `capturedTarget` |
| `EnemiesLayerMask` | `TargetCollisionMask` |
| 注释中的"敌人" | "目标" |

---

## 七、GameActor 公开 API 总览

| 方法 | 用途 | 内部守门 |
|------|------|---------|
| `TakeDamage(damage, ...)` | 造成伤害 | IsDead, ActiveImmunities |
| `ApplyEffect(effect)` | 施加效果（眩晕/减速/Buff...） | CanBeAffected |
| `ApplyKnockback(dir, speed)` | 击退位移 | ForcedMovement |
| `CanBeAffected(effect?)` | 子类覆写，条件免疫 | — |

---

## 八、阵营系统（暂不实施）

当前项目没有 PvP / 友伤 / 队友 NPC，不需要阵营系统。未来如有需要，加 `Faction` 枚举到 `GameActor`：

```csharp
public enum FactionType { Neutral, Player, Enemy }
[Export] public FactionType Faction { get; set; }
```

过滤一行：`if (target.Faction == Actor.Faction) return;`

**不要提前添加。**

---

## 九、新效果开发检查清单

- [ ] 目标发现：`IntersectShape` 或 `Area2D` 信号，不用 `GetNodesInGroup`
- [ ] 伤害：`DamageDispatcher.DealDamage`，不用 `actor.TakeDamage`
- [ ] 击退：`actor.ApplyKnockback`，不用 `actor.Velocity =`
- [ ] 效果施加：`actor.ApplyEffect`，不用手动判断免疫
- [ ] `TargetCollisionMask` 导出配置，不写死组名/层号
- [ ] 变量名用 `target`/`actor`，不硬编码 `enemy`/`player`
- [ ] 效果内部不判断目标阵营/身份

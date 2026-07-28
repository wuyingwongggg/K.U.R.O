# 效果统一规范

## 核心原则

**所有外部效果通过统一入口作用于 GameActor。GameActor 自身决定接受还是拒绝。**

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
- **不阻塞 `TakeDamage`**，只阻塞 `ApplyEffect`

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

## 二、目标发现：只走物理

### 禁用

```csharp
GetTree().GetNodesInGroup("enemies")    // 绕过物理引擎
GetTree().GetFirstNodeInGroup("player") // 硬编码组名
actor.GlobalPosition.DistanceTo() < R   // 忽略碰撞形状
```

### 统一使用

```csharp
// 同步形状查询（IntersectShape）
var query = new PhysicsShapeQueryParameters2D
{
	Shape = shape,
	Transform = shape.GlobalTransform,
	CollisionMask = TargetCollisionMask,     // 导出配置，不写死
	CollideWithAreas = true,
	CollideWithBodies = false
};
spaceState.IntersectShape(query);

// 或信号驱动（Area2D.BodyEntered / AreaEntered）
area.BodyEntered += OnBodyEntered;
```

目标：所有效果通过物理引擎发现目标，netAdmin 的 CollisionShape2D 状态自动被尊重。

### 迁移清单

| 效果 | 当前方式 | 应改为 |
|------|---------|--------|
| BoomDmgEffect | `GetNodesInGroup` + `DistanceTo` | `IntersectShape` + `CircleShape2D(Radius)` |
| DiscoFlashStunEffect | `GetNodesInGroup` + `DistanceSquaredTo` | `IntersectShape` |
| BlackHoleEffect 吸引 | `GetNodesInGroup` + `DistanceTo` | `IntersectShape` |
| SoundWaveEffect | `GetNodesInGroup` + `DistanceTo` | `IntersectShape` |
| TeleportStrikeEffect | `GetNodesInGroup` + `DistanceToSegment` | `IntersectShape` |

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
| `mainCharacter.Velocity = knockbackVelocity` | `mainCharacter.ApplyKnockback(dir, speed)`（玩家走覆写，含 ConsumePendingHitKnockback） |

`KnockbackDriver`（`KnockbackOnAttackEffect` 内部）保留，因为它需要 `MoveAndCollide` 做碰撞响应。但在 `Attach` 入口也加 `ForcedMovement` 检查。

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

已内置 `CanBeAffected` 守门。所有施加效果的代码直接调 `ApplyEffect`，不需要自己判断目标状态。

---

## 六、GameActor 公开 API 总览

| 方法 | 用途 | 内部守门 |
|------|------|---------|
| `TakeDamage(damage, ...)` | 造成伤害 | IsDead, ActiveImmunities |
| `ApplyEffect(effect)` | 施加效果（眩晕/减速/Buff...） | CanBeAffected |
| `ApplyKnockback(dir, speed)` | 击退位移 | ForcedMovement |
| `CanBeAffected(effect?)` | 子类覆写，条件免疫 | — |

---

## 七、新效果开发检查清单

- [ ] 目标发现：`IntersectShape` 或 `Area2D` 信号，不用 `GetNodesInGroup`
- [ ] 伤害：`DamageDispatcher.DealDamage`，不用 `actor.TakeDamage`
- [ ] 击退：`actor.ApplyKnockback`，不用 `actor.Velocity =`
- [ ] 效果施加：`actor.ApplyEffect`，不用手动判断免疫
- [ ] `TargetCollisionMask` 导出配置，不写死组名/层号
- [ ] 不硬编码 `enemy`/`player` 变量名

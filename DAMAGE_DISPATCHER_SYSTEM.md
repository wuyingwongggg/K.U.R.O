# DamageDispatcher 伤害分发系统

## 概述

`DamageDispatcher` 是所有伤害的唯一入口。应通过 `DealDamage` 或 `DealDamageFromArea`，绕过它直接调用 `actor.TakeDamage()` 会丢失阵营过滤和自伤保护。

**文件**: `scripts/core/DamageDispatcher.cs`

---

## 核心架构

```
攻击发起（敌人/玩家/特效）
	│
	├── 有 Area2D 引用
	│   └── DealDamageFromArea(area, damage, attacker, factions, allowSelfDamage)
	│       ├── 玩家路径：GetFirstNodeInGroup("player") → IsHitByArea → DealToGameActor
	│       └── 其他路径：IntersectShape → ResolveDamageReceiver → DealDamage
	│
	└── 有具体目标节点（信号触发）
		└── DealDamage(target, damage, origin, attacker, source, factions, allowSelfDamage)
			└── 遍历节点树 → GetFaction 过滤 → 自伤保护 → TakeDamage
			└── 返回 bool：true=命中有效目标，false=未命中（阵营不匹配）
```

---

## 两个核心方法

### DealDamage (单目标，返回 bool)

```csharp
public static bool DealDamage(
	Node target,
	float damage,
	Vector2? origin = null,
	GameActor? attacker = null,
	DamageSource source = DamageSource.DirectAttack,
	TargetableFactions allowedFactions = TargetableFactions.All,
	bool allowSelfDamage = false)
```

**流程**：
1. 从 `target` 向上遍历节点树
2. 每级检查 `GetFaction(current)` — 不匹配 `allowedFactions` 则跳过
3. 遇到 `GameActor` → 自伤检查 → `DealToGameActor`
4. 遇到 `HasMethod("TakeDamage")` → `DealViaCall`
5. 返回 `true` 表示命中有效目标，`false` 表示未命中

### DealDamageFromArea (区域扫描，返回 void)

```csharp
public static void DealDamageFromArea(
	Area2D area,
	float damage,
	GameActor? attacker,
	TargetableFactions allowedFactions = TargetableFactions.All,
	bool allowSelfDamage = false)
```

**两条路径**：

**玩家路径（前置）**：
```
GetFirstNodeInGroup("player") → GameActor.IsHitByArea(area) → DealToGameActor
```

**其他阵营路径（IntersectShape）**：
```
IntersectShape(area.Shape) → 扫描所有重叠碰撞体 → ResolveDamageReceiver → DealDamage
```

`damaged` HashSet 防止同一目标被两条路径重复伤害。

---

## 阵营系统

```csharp
[Flags]
public enum TargetableFactions
{
	None      = 0,
	Player    = 1 << 0,  // group "player"
	Enemy     = 1 << 1,  // group "enemies"
	WorldItem = 1 << 2,  // group "world_items"
	All       = Player | Enemy | WorldItem
}
```

| Group | Faction |
|-------|---------|
| `"player"` | Player |
| `"enemies"` | Enemy |
| `"world_items"` | WorldItem |

### WorldItem 阵营的实际可伤害对象

`world_items` 组包含两类，但**只有 DestructibleObject 可被伤害**：

| 对象 | 结构 | 伤害接口 | 可被伤害 |
|------|------|---------|---------|
| `DestructibleObject`（可破坏物/屏障） | Node2D + StaticBody2D 子节点（层 1）+ `world_items` 组 | `TakeDamage(float)`（HP/摧毁） | ✓ |
| `WorldItemEntity`（武器/物品掉落） | CharacterBody2D（body 层 0）+ TriggerArea（层 2）+ `world_items` 组 | 无 | ✗ 免疫 |
| `RigidBodyWorldItemEntity`（投掷武器掉落） | Node2D + GrabArea（层 2）+ `world_items` 组 | 无 | ✗ 免疫 |

判定差异：
- DestructibleObject 走 **Body** 路径（StaticBody2D 层 1，`GetOverlappingBodies`/`BodyEntered`）
- 掉落物走 **Area** 路径（层 2）——即使被物理检测到，`ResolveDamageReceiver` 因无 `TakeDamage` 返回 null

---

## 自伤保护

### BelongsToActor

```csharp
public static bool BelongsToActor(Node node, GameActor? actor)
```

从 `node` 沿节点树向上检查是否属于 `actor`。

### AllowSelfDamage 开关（独立于阵营筛选）

```csharp
// 方法签名中新增参数（默认 false = 不自伤）
bool allowSelfDamage = false
```

- **false（默认）**：`BelongsToActor` 激活，绝对不对 attacker 自身造成伤害
- **true**：跳过 `BelongsToActor` 检查，攻击可命中自身

各脚本通过 `[Export] public bool AllowSelfDamage` 在编辑器中独立控制。

**涉及位置**：
- `DealDamage` 中：`if (!allowSelfDamage && BelongsToActor(current, attacker)) continue;`
- `DealDamageFromArea` 中：玩家路径和 IntersectShape 路径各有一处
- 特效前置守卫：`if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(target, _attacker)) return;`

---

## 攻击模组入口

### EnemyAttackTemplate 基类 wrapper（唯一调用点）

```csharp
// EnemyAttackTemplate.cs
[Export] public bool AllowSelfDamage { get; set; } = false;

protected void DealDamage(Area2D area)
{
	DamageDispatcher.DealDamageFromArea(area, GetDamage(), Enemy, TargetableFactions, AllowSelfDamage);
}

protected void DealDamage(Area2D area, int damageOverride)
{
	DamageDispatcher.DealDamageFromArea(area, damageOverride, Enemy, TargetableFactions, AllowSelfDamage);
}
```

所有攻击子类调用 `DealDamage(area)` 而不是直接调 `DamageDispatcher.DealDamageFromArea`。加参数只需改基类一处。

---

## 特效/投射物的最佳实践

### 信号驱动（推荐模式）

```csharp
// _Ready()
_attackArea.BodyEntered += OnBodyEntered;
_attackArea.AreaEntered += OnAreaEntered;

// 导出
[Export] public bool AllowSelfDamage { get; set; } = false;

void OnBodyEntered(Node body)
{
	if (_hit) return;
	// 自伤守卫（受 AllowSelfDamage 控制）
	if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, _attacker)) return;

	bool alreadyInvincible = body is MainCharacter mc && mc.IsHitInvincible;

	// 关键：检查返回值，阵营不匹配则不自毁
	bool dealt = DamageDispatcher.DealDamage(body, Damage, GlobalPosition, _attacker,
		DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage);
	if (!dealt) return;

	if (!alreadyInvincible && body is GameActor hitActor)
		ApplyKnockback(hitActor);

	_hit = true;
	QueueFree();
}
```

### Attacker 解析

特效由 `SpawnEffectAtEnemy()` 生成时，Effect 和 Enemy 是同级节点：

```csharp
private void ResolveAttacker()
{
	var parent = GetParent();
	if (parent == null) return;
	foreach (var child in parent.GetChildren())
	{
		if (child.IsInGroup("enemies") && child is GameActor ga)
		{
			_attacker = ga;
			break;
		}
	}
}
```

### 击退时序

击退必须在 `DealDamage` **之前**检查无敌状态（`IsHitInvincible`），避免伤害触发无敌后短路击退分支。

---

## 常见反模式

| 反模式 | 问题 | 正确做法 |
|--------|------|----------|
| 直接 `actor.TakeDamage()` | 绕过阵营过滤和自伤保护 | 用 `DamageDispatcher.DealDamage` |
| `DealDamage` 不检查返回值 | 非目标阵营碰撞时误自毁 | `if (!dealt) return;` |
| `DealDamage` 传 `attacker: null` | `GameActor` 被跳过 | 用 `ResolveAttacker()` 获取 `_attacker` |
| 只检查 `_player` group | 其他阵营的 `TargetableFactions` 无效 | 用信号替代轮询 |
| `DealDamageFromArea` + `DealDamage` 叠加 | 双重伤害 | 只用一条路径 |
| 攻击子类直接调 `DamageDispatcher.DealDamageFromArea` | 维护困难 | 用基类 `DealDamage(area)` wrapper |

---

## 文件索引

| 文件 | 角色 |
|------|------|
| `scripts/core/DamageDispatcher.cs` | 伤害分发核心 |
| `scripts/core/GameActor.cs` | `IsHitByArea`, `ResolvePreferredHitArea`, `TakeDamage` |
| `scripts/actors/heroes/SamplePlayer.cs` | `HitArea` 导出 |
| `scripts/actors/enemies/attacks/EnemyAttackTemplate.cs` | 基类 wrapper + `AllowSelfDamage` export |
| `scripts/actors/enemies/attacks/EnemyWaiterA02ProjectileInstance.cs` | 信号驱动参考实现 |
| `scripts/fx/EnemyBullet.cs` | 信号驱动参考实现 |
| `scripts/fx/LaserBeamUltimate.cs` | 轮询 + DealDamageFromArea 参考实现 |

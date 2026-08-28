# 接口文档

## 概述

项目使用接口解耦跨模块的行为调用，避免硬编码类型检查。

| 接口 | 命名空间 | 文件 | 用途 |
|---|---|---|---|
| `IFacingDirectional` | `Kuros.Fx` | [scripts/fx/IFacingDirectional.cs](../scripts/fx/IFacingDirectional.cs) | 传递朝向信息 |
| `IDamageable` | `Kuros.Core` | [scripts/core/IDamageable.cs](../scripts/core/IDamageable.cs) | 接收伤害 |
| `IAttackerProvider` | `Kuros.Fx` | [scripts/fx/IAttackerProvider.cs](../scripts/fx/IAttackerProvider.cs) | 传递攻击者 |

---

## 一、IFacingDirectional

### 定义

```csharp
namespace Kuros.Fx
{
	public interface IFacingDirectional
	{
		bool FacingRight { get; set; }
	}
}
```

### 用途

当敌人攻击生成特效（激光、飞弹等）时，特效需要知道朝左还是朝右。通过接口传递朝向，新增特效类型时攻击模板无需修改。

### 调用方：EnemyAttackTemplate.SpawnEffectAtEnemy()

```csharp
// scripts/actors/enemies/attacks/EnemyAttackTemplate.cs 第546-548行
if (node2D is Kuros.Fx.IFacingDirectional facing)
{
	facing.FacingRight = Enemy.FacingRight;
}
```

### 实现方

| 类 | 文件 |
|---|---|
| `EnemyBullet` | [scripts/fx/EnemyBullet.cs](../scripts/fx/EnemyBullet.cs) |
| `EnemyPaperBullet` | [scripts/fx/EnemyPaperBullet.cs](../scripts/fx/EnemyPaperBullet.cs) |
| `LaserBeam` | [scripts/fx/LaserBeam.cs](../scripts/fx/LaserBeam.cs) |

### 新增实现步骤

1. 类声明加 `, IFacingDirectional`
2. 将 `FacingRight` 从字段改为属性（`{ get; set; }`）

```csharp
public partial class MyNewFx : Node2D, IFacingDirectional
{
	[Export] public bool FacingRight { get; set; } = true;
}
```

> **注意**：C# 字段不能实现接口属性。必须用 `{ get; set; }` 自动属性，否则编译报错 CS0535。

---

## 二、IDamageable

### 定义

```csharp
namespace Kuros.Core
{
	public interface IDamageable
	{
		void TakeDamage(float damage);
	}
}
```

### 用途

玩家攻击命中非 GameActor 目标（家具、可破坏物等）时，通过接口调用伤害，避免玩家代码直接依赖具体类。

### 调用方：SamplePlayer.TryDamageDestructibleItem()

```csharp
// scripts/actors/heroes/SamplePlayer.cs 第1608-1620行
private static void TryDamageDestructibleItem(Area2D hitArea, float damageAmount)
{
	Node? current = hitArea;
	while (current != null)
	{
		if (current is IDamageable damageable)
		{
			damageable.TakeDamage(damageAmount);
			return;
		}
		current = current.GetParentOrNull<Node>();
	}
}
```

### 调用链路

```
玩家攻击
  → SamplePlayer.PerformAttackCheck()
	→ ApplyDamageWithArea()
	  → DealDamageFromHitAreas()          // 先查 GameActor
		→ TryResolveActorFromHitArea()    // 失败（非敌人）
		  → TryDamageDestructibleItem()   // 沿父链查 IDamageable
			→ damageable.TakeDamage(damage)
```

### 实现方

| 类 | 文件 |
|---|---|
| `DestructibleWorldItem` | [scripts/items/world/DestructibleWorldItem.cs](../scripts/items/world/DestructibleWorldItem.cs) |

### 新增实现步骤

1. 类声明加 `, IDamageable`
2. 实现 `TakeDamage(float damage)` 方法

```csharp
public partial class MyBreakableObject : Node, IDamageable
{
	[Export] public float MaxHP = 100f;

	public void TakeDamage(float damage)
	{
		// 扣血、死亡逻辑
	}
}
```

3. 确保该节点是碰撞目标 Area2D 的祖先节点（`TryDamageDestructibleItem` 从碰撞点沿父链查找）

---

## 三、IAttackerProvider

### 定义

```csharp
namespace Kuros.Fx
{
	public interface IAttackerProvider
	{
		GameActor? Attacker { get; set; }
	}
}
```

### 用途

攻击特效/投射物造成伤害时需要知道攻击者（`AllowSelfDamage` 自伤保护、阵营过滤、击退来源）。通过接口显式传递，避免从父节点猜测——父节点下第一个敌人不一定是发射者，解析错误会导致自伤保护失效（打自己）。

### 调用方

**EnemyAttackTemplate.SpawnEffectAtEnemy()**（敌人攻击特效）：

```csharp
// scripts/actors/enemies/attacks/EnemyAttackTemplate.cs
if (node2D is Kuros.Fx.IAttackerProvider attackerProvider)
{
	attackerProvider.Attacker = Enemy;
}
```

**RigidBodyWorldItemEntity 投掷系统**（投掷武器特效，如回旋镖）：

```csharp
// scripts/items/world/RigidBodyWorldItemEntity.cs（SpawnThrowDestroyEffects / Destroy）
if (node2D is Kuros.Fx.IAttackerProvider attackerProvider)
{
	attackerProvider.Attacker = LastDroppedBy;
}
```

### 实现方

| 类 | 文件 |
|---|---|
| `LaserBeamA` | [scripts/fx/LaserBeamA.cs](../scripts/fx/LaserBeamA.cs) |
| `LaserBeamUltimate` | [scripts/fx/LaserBeamUltimate.cs](../scripts/fx/LaserBeamUltimate.cs) |
| `EnemyPaperBullet` | [scripts/fx/EnemyPaperBullet.cs](../scripts/fx/EnemyPaperBullet.cs) |
| `RotatingCube` | [scripts/fx/RotatingCube.cs](../scripts/fx/RotatingCube.cs) |
| `EnemyWaiterAThrowProjectile` | [scripts/actors/enemies/attacks/EnemyWaiterAThrowProjectile.cs](../scripts/actors/enemies/attacks/EnemyWaiterAThrowProjectile.cs) |
| `BoomerangAttackEffect` | [scripts/effects/BoomerangAttackEffect.cs](../scripts/effects/BoomerangAttackEffect.cs) |
| `ECoreAttackEffect` | [scripts/effects/ECoreAttackEffect.cs](../scripts/effects/ECoreAttackEffect.cs) |

### 新增实现步骤

1. 类声明加 `, IAttackerProvider`
2. 提供 `GameActor? Attacker { get; set; }` 属性（可以是显式属性，或委托给已有 `_attacker` 字段的访问器）

```csharp
public partial class MyNewFx : Node2D, IAttackerProvider
{
	public GameActor? Attacker
	{
		get => _attacker;
		set => _attacker = value;
	}
}
```

> **注意**：C# 字段不能实现接口属性。必须用 `{ get; set; }` 属性，否则编译报错 CS0535（同 IFacingDirectional）。

### 与 ResolveAttacker 的关系

`ResolveAttacker()`（父节点猜测）保留为**兜底**：生成方已显式设置 `Attacker` 时优先使用显式值，未设置时退回父节点解析。

---

## 四、设计原则

```
调用方                          接口                           实现方
──────                         ──────                         ──────
EnemyAttackTemplate  ──→  IFacingDirectional  ←──  EnemyBullet
												   EnemyPaperBullet
												   LaserBeam

SamplePlayer         ──→  IDamageable          ←──  DestructibleWorldItem
												   (未来：BreakableWall, ExplosiveBarrel...)

EnemyAttackTemplate  ──→  IAttackerProvider    ←──  LaserBeamA
RigidBodyWorldItem   ──→                       ←──  LaserBeamUltimate
(投掷系统)                                       ←──  EnemyPaperBullet
													   RotatingCube
													   EnemyWaiterAThrowProjectile
													   BoomerangAttackEffect
													   ECoreAttackEffect
```

- **调用方只依赖接口**，不 import 实现类的命名空间
- **新增实现类无需修改调用方**
- **接口放核心层**（`Kuros.Core` / `Kuros.Fx`），实现放各自模块

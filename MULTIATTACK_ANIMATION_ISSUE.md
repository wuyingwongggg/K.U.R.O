# MultiAttack 动画偶数次卡帧问题

## 最终结论

**根因：`_activeElapsed` 计时器在攻击间残留，导致偶数次 Warmup 第一帧就超时，Active 阶段被跳过，动画没机会播放。**

修复一行：`OnWarmupStarted` 中 `_activeElapsed = 0f`。

---

## 现象

偶数次 MultiAttack 动画卡在第一帧，Spine hit 事件不触发，无伤害。奇数正常。

## 根因链

```
MultiAttack #1:
  Warmup → _isActiveHeld=true, _activeElapsed=0
  Active → _PhysicsProcess 逐帧累计 _activeElapsed
  Duration 到 → _isActiveHeld=false, ForceEnterRecoveryPhase
  Recovery → Idle → OnAttackFinished
  ⚠ _activeElapsed 仍为上次的累计值（如 2.0）

MultiAttack #2:
  Warmup → _isActiveHeld=true
  ⚠ _activeElapsed 仍是 2.0（第 1 次的残留）
  _PhysicsProcess 第一帧: _activeElapsed=2.016 >= Duration → _isActiveHeld=false
  → ForceEnterRecoveryPhase（立刻跳过 Active！）
  → AnimationController: IsInMultiAttackPhase=false → 走 Part 分支
  → PartialLoop 从未启动 → 动画始终卡帧 1
```

## 涉及文件

- `scripts/actors/enemies/attacks/EnemyNetAdminMultiMeleeAttack.cs` — 修复处
- `scripts/actors/enemies/attacks/EnemyPinballAttack.cs` — 参考（正确实现）
- `scripts/actors/enemies/attacks/EnemyOnePunchAttack.cs` — 参考（正确实现）

## 修复

```csharp
// EnemyNetAdminMultiMeleeAttack.OnWarmupStarted
protected override void OnWarmupStarted()
{
	_invokeCount++;
	_isActiveHeld = true;
	_activeElapsed = 0f;   // ← 关键：每次攻击开头清零计时器
	base.OnWarmupStarted();
}
```

---

## 通用规律：挂起类攻击的状态重置

任何使用 `ShouldHoldActivePhase` / `ShouldHoldRecoveryPhase` 挂起的攻击，**所有计时字段必须在 `OnAttackStarted` 或 `OnWarmupStarted` 中重置**。不能只在 `OnActivePhase` 中清，因为下一轮 Warmup 阶段不受 Active 保护。

### 正确模板

```csharp
// ✓ PinballAttack: OnAttackStarted 重置所有计时字段
protected override void OnAttackStarted()
{
	base.OnAttackStarted();
	IsStopping = false;
	_isDashing = false;
	_dashTimeElapsed = 0f;    // ← 重置
	// ... 其他字段
}

// ✓ OnePunchAttack: OnAttackStarted 同样处理
protected override void OnAttackStarted()
{
	base.OnAttackStarted();
	_isDashing = false;
	_dashFinalized = false;
	_dashDistanceTraveled = 0f; // ← 重置
	// ... 其他字段
}
```

### 反模式（导致本 bug）

```csharp
// ✗ 只在 OnActivePhase 重置，OnWarmupStarted 不碰
protected override void OnActivePhase()
{
	_activeElapsed = 0f;  // 太晚：Warmup 期间的 _PhysicsProcess 看不到
}
```

### 三步检查法

给任何"挂起阶段 + 计时到期退出"的攻击做 code review：

1. 计时字段在哪重置？→ 必须是 `OnAttackStarted` 或 `OnWarmupStarted`
2. `_PhysicsProcess` 中的超时判断依赖哪些字段？→ 每个字段是否在第一步中重置
3. `OnAttackFinished` 是否清理了挂起标志？→ `_isActiveHeld = false` 等

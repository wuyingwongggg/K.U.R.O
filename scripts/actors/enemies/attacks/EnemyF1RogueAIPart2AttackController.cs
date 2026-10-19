using System.Collections.Generic;
using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// rogueAI 二阶段本体的攻击控制器（Enemy_F1_rogueAI_part2 专属）：把父类的大招武装判据从
	/// 关卡进度换回**血量阈值**——
	///   · 判据 = <c>CurrentHealth / MaxHealth ≤ 阈值</c>（血量从高往低跌落，与其它 boss 的
	///     UltimateHealthThresholds 语义一致）；父类两个"进度"导出在本子类重解释为血量比值：
	///     UltimateProgressThresholds = 血量阈值列表（升序配置、内部降序消费）、
	///     UltimateUpgradeProgress = Pro 升级的血量比值。
	///   · 背景：二阶段本体的"血量"由部件驱动（EnemyF1RogueAIPart2），是真实进度量；而关卡进度在
	///     phase-2 恒 ≥1，父类的进度判据会让全部大招出场即连放。part1 不受影响（仍用父类进度判据）。
	///   · 指针/消费/打断退还/重触发间隔/清陈旧队列/Pro 切换等机械**全部继承父类**，只换判据源。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAIPart2AttackController : EnemyF1RogueAIAttackController
	{
		/// <summary>血量比值（0~1）；本体缺失 / MaxHealth ≤ 0 时视为 1（不武装，安全侧）。</summary>
		private float HealthRatio
			=> Enemy != null && GodotObject.IsInstanceValid(Enemy) && Enemy.MaxHealth > 0
				? Mathf.Clamp((float)Enemy.CurrentHealth / Enemy.MaxHealth, 0f, 1f)
				: 1f;

		/// <summary>阈值缓存改**降序**：血量从高往低跌落，先跨过最大的比值（如 0.75），指针沿降序列表前进
		/// （父类进度语义是升序——进度只升、血量只降，同形异向）。</summary>
		protected override void RefreshThresholdCache()
		{
			var list = new List<float>();
			if (UltimateProgressThresholds != null)
			{
				foreach (float t in UltimateProgressThresholds)
				{
					float clamped = Mathf.Clamp(t, 0f, 1f);
					if (!list.Contains(clamped)) list.Add(clamped);
				}
			}
			list.Sort((a, b) => b.CompareTo(a));
			_sortedThresholds = list.ToArray();
		}

		/// <summary>"血量已跌破阈值但还没放"：直接轮询当前血量比值，天然幂等（本体一次跌多件也不会漏）。</summary>
		protected override bool ShouldTriggerUltimate()
		{
			if (_sortedThresholds.Length == 0) return false;
			if (ConsumedUltimates >= _sortedThresholds.Length) return false;
			return HealthRatio <= _sortedThresholds[ConsumedUltimates];
		}

		/// <summary>血量比值 ≤ UltimateUpgradeProgress（重解释为血量）→ 原版大招换成 Pro。</summary>
		protected override bool ShouldUpgradeUltimate()
			=> HealthRatio <= UltimateUpgradeProgress;
	}
}

using Godot;
using System;
using System.Collections.Generic;

namespace Kuros.Actors.Enemies.Attacks
{
	public partial class EnemyD1NetAdminAttackController : EnemyAttackController
	{
		[Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";
		[Export] public string MultiMeleeAttackName { get; set; } = "MultiSimpleMeleeAttack";
		[Export] public string UltimateAttackName { get; set; } = "UltimateAttack";

		[ExportCategory("Ultimate Attack")]
		/// <summary>
		/// 触发终极技的血量百分比阈值列表（0~1）。
		/// 每个阈值只触发一次，按从高到低顺序依次检测。
		/// </summary>
		[Export] public float[] UltimateHealthThresholds = new float[] { 0.5f };

		/// <summary>连续使用相同攻击时每次额外降低的权重百分比。</summary>
		[Export(PropertyHint.Range, "0,50,1")]
		public int FatiguePercentPerUse = 10;

		/// <summary>疲劳惩罚后的最小权重百分比（不低于原始权重的此比例）。</summary>
		[Export(PropertyHint.Range, "1,100,1")]
		public int MinWeightPercent = 10;

		public string CurrentAttackName { get; private set; } = string.Empty;

		private readonly Dictionary<string, float> _originalWeights = new();
		private string _lastAttackName = string.Empty;
		private int _consecutiveSameCount;

		private float[] _sortedUltimateThresholds = Array.Empty<float>();
		private int _triggeredUltimateIndex;

		public override void Initialize(SampleEnemy enemy)
		{
			base.Initialize(enemy);
			CacheOriginalWeights();
			RefreshThresholdCache();
			ConfigureNextAttack();
		}

		// 在 base.Initialize 填充 _entries 后，记录各攻击的原始权重
		private void CacheOriginalWeights()
		{
			_originalWeights.Clear();
			foreach (var child in GetChildren())
			{
				if (child is EnemyAttackTemplate template && child.HasMeta("attack_weight"))
				{
					float w = (float)child.GetMeta("attack_weight");
					_originalWeights[template.Name] = w;
				}
			}
		}

		protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
		{
			base.OnChildAttackStarted(attack);
			CurrentAttackName = attack.Name;

			if (IsAttack(attack.Name, UltimateAttackName))
			{
				ConsumeUltimateTrigger();
				if (!string.IsNullOrEmpty(_lastAttackName))
					RestoreAttackWeight(_lastAttackName);
				_consecutiveSameCount = 0;
				_lastAttackName = string.Empty;
				ConfigureNextAttack();
				return;
			}

			if (attack.Name == _lastAttackName)
			{
				// 连续使用相同攻击：疲劳累计
				_consecutiveSameCount++;
				int reduction = _consecutiveSameCount * FatiguePercentPerUse;
				float originalWeight = GetOriginalWeight(attack.Name);
				float floor = originalWeight * MinWeightPercent / 100f;
				float newWeight = Mathf.Max(originalWeight * (100 - reduction) / 100f, floor);
				TrySetAttackWeight(attack.Name, newWeight);
			}
			else
			{
				// 切换攻击：重置上一次攻击的权重为原始值，重置疲劳计数
				if (!string.IsNullOrEmpty(_lastAttackName))
					RestoreAttackWeight(_lastAttackName);

				_consecutiveSameCount = 1;
				_lastAttackName = attack.Name;
			}
		}

		protected override void OnAttackFinished()
		{
			ConfigureNextAttack();
			base.OnAttackFinished();
			CurrentAttackName = string.Empty;
		}

		/// <summary>
		/// 外部（如 Frozen 结束时）强制重新评估终极技触发条件。
		/// </summary>
		public void ForceEvaluateUltimate()
		{
			GD.Print($"[NetAdminUltimate] ForceEvaluate: HP={Enemy?.CurrentHealth}/{Enemy?.MaxHealth}");
			ConfigureNextAttack();
		}

		private void ConfigureNextAttack()
		{
			// 血量阈值优先级最高：满足条件时强制排队终极技
			if (ShouldTriggerUltimate())
			{
				GD.Print($"[NetAdminUltimate] 触发！阈值={_sortedUltimateThresholds[_triggeredUltimateIndex]}, HP比={(float)Enemy!.CurrentHealth / Enemy.MaxHealth:F2}");
				TrySetAttackWeight(UltimateAttackName, 1f);
				TrySetAttackWeight(MeleeAttackName, 0f);
				TrySetAttackWeight(MultiMeleeAttackName, 0f);
				return;
			}

			// 未触发终极技时恢复普通权重
			TrySetAttackWeight(UltimateAttackName, 0f);
			RestoreAttackWeight(MeleeAttackName);
			RestoreAttackWeight(MultiMeleeAttackName);
		}

		private bool ShouldTriggerUltimate()
		{
			if (_sortedUltimateThresholds.Length == 0) return false;
			if (_triggeredUltimateIndex >= _sortedUltimateThresholds.Length) return false;
			if (Enemy == null || Enemy.MaxHealth <= 0) return false;

			float healthRatio = (float)Enemy.CurrentHealth / Enemy.MaxHealth;
			return healthRatio <= _sortedUltimateThresholds[_triggeredUltimateIndex];
		}

		private void ConsumeUltimateTrigger()
		{
			_triggeredUltimateIndex++;
			// 跳过 HP 已经低于的后续阈值，避免连发多次 Ultimate
			while (_triggeredUltimateIndex < _sortedUltimateThresholds.Length
				&& Enemy != null
				&& (float)Enemy.CurrentHealth / Enemy.MaxHealth <= _sortedUltimateThresholds[_triggeredUltimateIndex])
			{
				_triggeredUltimateIndex++;
			}
		}

		private void RefreshThresholdCache()
		{
			var list = new List<float>();
			if (UltimateHealthThresholds != null)
			{
				foreach (float t in UltimateHealthThresholds)
				{
					float clamped = Mathf.Clamp(t, 0.01f, 1.0f);
					if (!list.Contains(clamped))
						list.Add(clamped);
				}
			}
			list.Sort((a, b) => b.CompareTo(a));
			_sortedUltimateThresholds = list.ToArray();
		}

		private static bool IsAttack(string attackName, string expectedName)
		{
			return attackName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
		}

		private float GetOriginalWeight(string attackName)
		{
			return _originalWeights.TryGetValue(attackName, out float w) ? w : 0f;
		}

		private void RestoreAttackWeight(string attackName)
		{
			if (_originalWeights.TryGetValue(attackName, out float original))
				TrySetAttackWeight(attackName, original);
		}
	}
}

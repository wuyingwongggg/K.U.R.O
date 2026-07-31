using Godot;
using System;
using System.Collections.Generic;

namespace Kuros.Actors.Enemies.Attacks
{
	public partial class EnemyD1DroneAttackController : EnemyAttackController
	{
		[Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";

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

		public override void Initialize(SampleEnemy enemy)
		{
			base.Initialize(enemy);
			CacheOriginalWeights();
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
			base.OnAttackFinished();
			CurrentAttackName = string.Empty;
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

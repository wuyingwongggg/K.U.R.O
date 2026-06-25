using Godot;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// 敌人战斗行为配置（Resource），挂载到 SampleEnemy 的 BehaviorConfig 字段上，
	/// 控制敌人在战斗中的站位策略和距离参数。
	/// </summary>
	[GlobalClass]
	public partial class EnemyBehaviorConfig : Resource
	{
		/// <summary>
		/// 战斗站位策略
		/// </summary>
		public enum PositioningStrategy
		{
			CloseIn,      // 贴脸近战，玩家远离时加速逼近
			KeepDistance, // 保持距离，玩家靠近时后退
			Adaptive      // 动态调整：有远程攻击可用时保持距离，否则贴脸
		}

		[ExportCategory("Positioning")]
		[Export]
		public PositioningStrategy Positioning = PositioningStrategy.CloseIn;

		/// <summary>
		/// 与玩家之间的最小舒适距离。KeepDistance 模式突破此距离触发后撤；
		/// CloseIn 模式超出此距离触发突进。
		/// </summary>
		[Export(PropertyHint.Range, "0,5000,10")]
		public float MinComfortDistance = 120f;

		[ExportCategory("Burst")]
		/// <summary>
		/// 突进/后撤时的速度倍率。
		/// </summary>
		[Export(PropertyHint.Range, "0.5,10,0.1")]
		public float BurstSpeedMultiplier = 2f;

		/// <summary>
		/// 单次突进/后撤持续时间（秒）。
		/// </summary>
		[Export(PropertyHint.Range, "0.1,10,0.1")]
		public float BurstDuration = 1f;

		/// <summary>
		/// 突进/后撤结束后的冷却时间（秒）。
		/// </summary>
		[Export(PropertyHint.Range, "0,30,0.1")]
		public float BurstCooldown = 3f;

		/// <summary>
		/// 突进/后撤期间的无敌持续时间（秒）。0 表示不免疫。
		/// </summary>
		[Export]
		public float BurstDamageImmuneDuration = 0f;
	}
}

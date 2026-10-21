using Godot;
using Kuros.Core;
using Kuros.Fx;

namespace Kuros.Effects
{
	/// <summary>
	/// 干扰区散布器：由攻击特效生成一次，在自身位置周围的圆盘内随机撒若干个
	/// <see cref="InterferenceZone"/>（世界层兄弟节点，自管理生命周期），随后自毁。
	/// 攻击来源通过 <see cref="IAttackerProvider"/> 注入给每个干扰区。
	/// </summary>
	[GlobalClass]
	public partial class InterferenceHazardField : Node2D, IAttackerProvider
	{
		[ExportCategory("Spawn 生成")]
		[Export(PropertyHint.Range, "1,12,1")] public int ZoneCount { get; set; } = 4;
		/// <summary>散布半径（像素）：以本节点位置为圆心的均匀圆盘。</summary>
		[Export(PropertyHint.Range, "100,3000,50")] public float SpawnRadius { get; set; } = 900f;
		[Export(PropertyHint.Range, "40,600,5")] public float MinZoneRadius { get; set; } = 120f;
		[Export(PropertyHint.Range, "40,600,5")] public float MaxZoneRadius { get; set; } = 200f;
		[Export] public PackedScene? ZoneScene { get; set; }

		[ExportCategory("Damage 伤害")]
		[Export(PropertyHint.Range, "0,200,1")] public int Damage { get; set; } = 20;

		public GameActor? Attacker { get; set; }

		private bool _spawned;

		// 首物理帧再生成：AttackEffectEntry 是 AddChild → 再摆 GlobalPosition，
		// _Ready 里读到的是默认坐标（同 ShockwaveController 的生成时序）。
		public override void _PhysicsProcess(double delta)
		{
			if (_spawned) return;
			_spawned = true;
			SpawnZones();
			QueueFree();
		}

		private void SpawnZones()
		{
			if (ZoneScene == null)
			{
				GD.PushWarning($"[{nameof(InterferenceHazardField)}] 未配置 ZoneScene，无法散布干扰区");
				return;
			}

			Node? parent = GetParent();
			if (parent == null)
			{
				GD.PushWarning($"[{nameof(InterferenceHazardField)}] 没有父节点，无法散布干扰区");
				return;
			}

			float minRadius = Mathf.Min(MinZoneRadius, MaxZoneRadius);
			float maxRadius = Mathf.Max(MinZoneRadius, MaxZoneRadius);
			int count = Mathf.Max(ZoneCount, 1);

			for (int i = 0; i < count; i++)
			{
				var zone = ZoneScene.Instantiate<InterferenceZone>();
				if (zone == null) continue;

				zone.Attacker = Attacker;
				zone.Damage = Damage;
				zone.Radius = (float)GD.RandRange(minRadius, maxRadius);

				float angle = GD.Randf() * Mathf.Tau;
				float distance = Mathf.Sqrt(GD.Randf()) * Mathf.Max(SpawnRadius, 0f);
				Vector2 offset = new(Mathf.Cos(angle), Mathf.Sin(angle));

				parent.AddChild(zone);
				zone.GlobalPosition = GlobalPosition + offset * distance;
			}
		}
	}
}

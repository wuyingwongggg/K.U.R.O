using Godot;
using Kuros.Actors.Enemies.Animation;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// rogueAI 二阶段本体（Enemy_F1_rogueAI_part2 专属脚本）：在 <see cref="EnemyF1RogueAI"/> 之上把
	/// 生命周期从"进度满收工"改为**部件驱动的血量**——
	///   · 订阅 <see cref="GameActor.DeathFinalized"/>，按组过滤本次战斗的部件（罐子 / 两条机械臂），
	///     每碎一件本体血量 -= <see cref="DamagePerPart"/>；归零走正常战斗死亡流程（Dying/Dead 状态、
	///     BossHUD 解绑、FinalizeDeath）。形态门：up（血量 &gt; <see cref="DownHealthThreshold"/>）免伤、
	///     down（≤ 阈值）放行**直接伤害**——部件联动链两种形态下都有效（直扣血量、不走免伤门）。
	///   · <see cref="EnemyF1RogueAI"/> 的 progress 收工链被强制关闭：F_begin 的 stage_top 段删除后
	///     进度恒 ≥1（stage_up 已满不清零），保留收工链会出场即退场；且部件清完那一帧会与 HP 归零
	///     双结束链同时开跑。生命周期唯一 owner = 血量。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAIPart2 : EnemyF1RogueAI
	{
		[ExportCategory("Part Link 部件联动")]
		/// <summary>部件标记组：本次战斗的罐子/机械臂由生成方打入此组——滑槽机械用
		/// <c>SlideRailMount.CarriageGroupToAdd</c>、过场直生用 <c>EffectConfig.GroupToAdd</c>、
		/// 手工摆放用 tscn 的 groups。部件死亡即扣本体血量。</summary>
		[Export] public string PartGroup { get; set; } = "rogueai_part2_part";
		/// <summary>每碎一件部件扣的血量（血量口径 = 场景 MaxHealth，建议配成部件总数）。</summary>
		[Export(PropertyHint.Range, "0,100,1")] public int DamagePerPart { get; set; } = 1;

		[ExportCategory("Form up/down 形态")]
		/// <summary>down 形态血量阈值：血量 ≤ 本值时进入 down（常态循环切 walk_down、放行直接伤害）；
		/// **0（默认）= 恒 up**（恒免伤 = 旧行为）。up（血量 > 阈值）免疫直接伤害，但部件联动链照常生效。</summary>
		[Export(PropertyHint.Range, "0,10000,1")] public int DownHealthThreshold { get; set; }

		private EnemyF1RogueAISpineAnimationController? _animController;
		private bool _downForm;

		public override void _Ready()
		{
			base._Ready();
			// 生命周期由部件血量驱动：关掉进度收工链（stage_top 已删、进度恒满，保留它 = 出场即退场）
			RetreatOnProgressFull = false;
			_animController = GetNodeOrNull<EnemyF1RogueAISpineAnimationController>("AnimationController");
			GameActor.DeathFinalized += OnPartDeathFinalized;
		}

		/// <summary>血量豁免楼层难度缩放：本体的"血量"= 部件计数（每碎一件 -1），必须与部件数严格相等——
		/// 被放大（如 stage_6 Floor 4 → ×1.48 → 场景值 4 变 6）后部件全碎也清不了血条，本体不死。
		/// 对外伤害照常按难度缩放（它仍是真刀真枪与玩家对打的敌人）。
		/// 在 <c>base._Ready()</c> 抄写 BaseMaxHealth/CurrentHealth 之前回滚，见 SampleEnemy.ApplyStageScaling 时序。</summary>
		protected override void ApplyStageScaling()
		{
			int rawMaxHealth = MaxHealth;
			base.ApplyStageScaling();   // 照常写入对外伤害系数（顺带把血量乘了系数）
			MaxHealth = rawMaxHealth;   // 血量回滚为场景原始值（部件计数口径）
		}

		public override void _ExitTree()
		{
			GameActor.DeathFinalized -= OnPartDeathFinalized;
			base._ExitTree();
		}

		public override void _PhysicsProcess(double delta)
		{
			base._PhysicsProcess(delta);

			// down 形态轮询（幂等）：血量跌到阈值及以下 → down（动画切 walk_down）。判据即时算（不读缓存），
			// 轮询只负责同步动画；回升会自动回 up（当前没有治疗源，实际单向）。
			bool down = IsDownForm;
			if (down == _downForm) return;
			_downForm = down;
			if (_animController != null && GodotObject.IsInstanceValid(_animController))
				_animController.DownForm = down;
		}

		/// <summary>up 形态（血量 > 阈值）常态免伤；down 形态放行**直接伤害**（仍不吃 ActorEffect）。
		/// 部件联动链（<see cref="OnPartDeathFinalized"/> 直扣血量）不走这道门——两种形态下都有效。</summary>
		public override bool CanBeAffected(ActorEffect? effect) => effect == null && IsDownForm;

		private bool IsDownForm => DownHealthThreshold > 0 && CurrentHealth <= DownHealthThreshold;

		/// <summary>部件阵亡 → 本体掉血（免伤核心的专用通道：直扣血量、不走 TakeDamage）。</summary>
		private void OnPartDeathFinalized(GameActor actor)
		{
			if (IsDeadOrDying || actor == null || !IsInstanceValid(actor)) return;
			if (string.IsNullOrEmpty(PartGroup) || !actor.IsInGroup(PartGroup)) return;
			if (DamagePerPart <= 0) return;

			CurrentHealth = Mathf.Max(0, CurrentHealth - DamagePerPart);
			NotifyHealthChanged();
			if (CurrentHealth <= 0)
				Die();
		}
	}
}

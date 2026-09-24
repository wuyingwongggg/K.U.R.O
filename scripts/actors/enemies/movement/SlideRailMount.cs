using Godot;

/// <summary>
/// 通用单轴滑座（滑槽）：一个轴、Marker 限位、恒速滑动、每帧硬钳。
/// 由被挂载的子节点（机械）通过 <see cref="SetTarget"/> 驱动；自身不含相位/玩家逻辑，供多台滑轨机械复用
/// （Enemy_F1_rogueAI_Magnet / _MachineGun / _Cannon）。
/// 滑动轴由 Axis 指定（默认 Y）；限位 Marker 取"该轴的世界坐标"。不配 Slot 限位 Marker 时滑槽静止
/// （退化成"只有机械在轨道上移动"的单轴形态）。
/// 限位解析放在首个物理帧（而不是 _Ready）：生成器是 AddChild 之后才写 GlobalPosition，_Ready 时节点还没摆到位。
/// </summary>
public partial class SlideRailMount : Node2D
{
	public enum RailAxis { X, Y }

	/// <summary>机械出生锚点：Far/Near 是**角色语义**（勾了 FlipCarriageEnds 会自动互换）。</summary>
	public enum SpawnAnchor { Far, Near, Origin }

	[ExportCategory("Rail 滑槽")]
	/// <summary>滑槽自身滑动的轴（默认 Y = 纵向升降；设 X 则为横向滑槽）。</summary>
	[Export] public RailAxis Axis { get; set; } = RailAxis.Y;
	/// <summary>滑槽滑动轴的限位标记（不配 = 滑槽静止）。</summary>
	[Export] public NodePath SlotStartMarkerPath { get; set; } = new("SlotStartMarker");
	[Export] public NodePath SlotEndMarkerPath { get; set; } = new("SlotEndMarker");
	/// <summary>机械的行程限位标记（缺省时机械被锁死在轨道原点）。
	/// 约定：Near = 贴玩家侧（场内/内侧）端，Far = 场外端。</summary>
	[Export] public NodePath CarriageNearMarkerPath { get; set; } = new("CarriageNearMarker");
	[Export] public NodePath CarriageFarMarkerPath { get; set; } = new("CarriageFarMarker");
	/// <summary>
	/// 镜像这条轨：把两个限位 Marker 的**角色**互换（Near ⇄ Far），Marker 位置与 NodePath 都不用动；
	/// 同时把 <see cref="VisualPath"/> 的 scale.x 取负，**连整块外观一起镜像**。
	/// 左右两条镜像轨只需在其中一条上勾选本开关，机械侧就能共用同一份配置
	/// （机械的 RetreatEnd 一律指"场外端"，翻转后语义自动跟着走）。
	/// </summary>
	[Export] public bool FlipCarriageEnds { get; set; }
	/// <summary>外观子节点：只放 Sprite 之类，**里面不能有 Marker / Mount / 物理体**（负缩放不保证碰撞结果）。
	/// 勾了 <see cref="FlipCarriageEnds"/> 时它的 scale.x 被写成负值 = 整块外观镜像；逻辑侧（root/Marker/Mount）
	/// 始终保持单位变换，坐标判定不受影响。</summary>
	[Export] public NodePath VisualPath { get; set; } = new("Visual");
	/// <summary>被挂载的机械场景：配了就自动实例化进 Mount（"机械固定在滑槽内"的父子结构）。</summary>
	[Export] public PackedScene? CarriagePrefab { get; set; }
	/// <summary>机械出生锚点：Far = 场外/待命端（默认）、Near = 贴玩家端、Origin = 滑槽原点（关卡摆放点/轨道中点）。
	/// Far/Near 与限位解释同源，勾了 <see cref="FlipCarriageEnds"/> 时自动跟着互换。
	/// 过场生成的 PropertyOverrides 会在入树前写好这个值，正好赶得上 <see cref="_Ready"/> 里的摆位。</summary>
	[Export] public SpawnAnchor CarriageSpawn { get; set; } = SpawnAnchor.Far;
	/// <summary>滑槽自身滑动速度（px/s）。</summary>
	[Export(PropertyHint.Range, "10,2000,1")] public float Speed { get; set; } = 200f;
	[Export(PropertyHint.Range, "0,64,1")] public float ArriveDeadzone { get; set; } = 4f;

	private bool _resolved;
	private float _slotStart;
	private float _slotEnd;
	private float _carriageNear;
	private float _carriageFar;
	private float _target;
	private bool _hasTarget;
	/// <summary>本次目标的速度覆盖（&gt; 0 时用它，否则用自身 <see cref="Speed"/>）：见 <see cref="SetTargetInTime"/>。
	/// 每次 <see cref="SetTarget"/>（常规跟随）会清零——时长只对"设目标那一次"负责。</summary>
	private float _targetSpeed;

	/// <summary>限位已解析（首个物理帧之后）。</summary>
	public bool IsResolved => _resolved;
	/// <summary>滑槽是否可动（配了 Slot 限位 Marker）。</summary>
	public bool HasSlotLimits { get; private set; }
	public float SlotStart => _slotStart;
	public float SlotEnd => _slotEnd;
	/// <summary>
	/// 两个行程限位 Marker 的坐标（世界坐标，沿 <see cref="CarriageAxis"/>），**已按角色解释**：
	/// Near = 贴玩家侧端、Far = 场外端；勾了 <see cref="FlipCarriageEnds"/> 则两者互换。
	/// 机械直接用这两个选端点（退场去 Far），用下面的区间做夹取/钳位。
	/// </summary>
	public float CarriageNear => _carriageNear;
	public float CarriageFar => _carriageFar;
	/// <summary>行程区间（已排序，与角色无关）：夹取/钳位用这两个，别拿 Near/Far 当 Clamp 的上下界。</summary>
	public float CarriageLo => Mathf.Min(_carriageNear, _carriageFar);
	public float CarriageHi => Mathf.Max(_carriageNear, _carriageFar);
	/// <summary>机械沿其移动的轴 = 滑动轴的正交轴。</summary>
	public RailAxis CarriageAxis => Axis == RailAxis.Y ? RailAxis.X : RailAxis.Y;
	/// <summary>滑槽当前在滑动轴上的世界坐标。</summary>
	public float CurrentRailCoordinate => GetCoordinate(GlobalPosition);

	/// <summary>滑槽是否已滑到目标（无 Slot 限位/无目标 = 视为到位）。</summary>
	public bool Arrived => !HasSlotLimits || !_hasTarget
		|| Mathf.Abs(_target - CurrentRailCoordinate) <= ArriveDeadzone;

	/// <summary>机械的出生点（**局部坐标**，Mount 坐标系）：机械可以据此"回生成点"
	/// （例如满进度后收工归位）。与出生摆放同源，FlipCarriageEnds/CarriageSpawn 都算在内。</summary>
	public Vector2 SpawnLocalPosition { get; private set; }

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;

		ApplyVisualMirror();
		SpawnLocalPosition = ResolveSpawnPosition();

		// 挂载点：CarriagePrefab 自动入驻 Mount，构成"机械是滑槽子节点"的结构约束
		var mount = EnsureMount();
		if (CarriagePrefab != null && mount.GetChildCount() == 0)
		{
			var carriage = CarriagePrefab.Instantiate<Node2D>();
			mount.AddChild(carriage);
			carriage.Position = SpawnLocalPosition;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Engine.IsEditorHint()) return;

		ResolveNow();
		if (!HasSlotLimits || !_hasTarget) return;

		float step = _target - CurrentRailCoordinate;
		if (Mathf.Abs(step) <= ArriveDeadzone)
		{
			SetCoordinate(_target);
			return;
		}

		float speed = _targetSpeed > 0f ? _targetSpeed : Speed;
		SetCoordinate(CurrentRailCoordinate + Mathf.Sign(step) * speed * (float)delta);
		// 硬钳兜底：外部系统（爆炸/黑洞直接写位置）也推不出滑槽
		SetCoordinate(Mathf.Clamp(CurrentRailCoordinate, _slotStart, _slotEnd));
	}

	/// <summary>把"角色翻转"同步到外观：<see cref="FlipCarriageEnds"/> 时 <see cref="VisualPath"/> 的 scale.x 写负
	/// ——整块美术（含 Visual 下每个精灵的局部位置）一起镜像。
	/// 用负缩放而不是 flip_h：flip_h 只是"这一个绘制节点的贴图怎么画"，不沿节点树传递（Node2D 上根本没有这个属性，
	/// Sprite2D 上也只有它自己受影响）；scale 属于变换链，才会带着子树一起翻。
	/// 符号由本开关**唯一决定**（写 ±|x|，不累乘）：在编辑器里手摆的镜像会在运行时被规范化掉。
	/// 只在 _Ready 应用一次——过场的 PropertyOverrides 是在入树前写好的，正好赶得上。</summary>
	private void ApplyVisualMirror()
	{
		var visual = VisualPath.IsEmpty ? null : GetNodeOrNull<Node2D>(VisualPath);
		if (visual == null)
		{
			if (FlipCarriageEnds)
				GD.PushWarning($"{Name}: FlipCarriageEnds=true 但未找到外观节点（{VisualPath}），外观不会被镜像");
			return;
		}

		var scale = visual.Scale;
		float absX = Mathf.Abs(scale.X);
		visual.Scale = new Vector2(FlipCarriageEnds ? -absX : absX, scale.Y);
	}

	/// <summary>机械出生位置（**局部坐标**——与滑槽在关卡里的摆放位置无关，_Ready 阶段就能算准）。
	/// 默认 Far（待命位/场外端），避免从轨道中间冒出来再滑走。</summary>
	private Vector2 ResolveSpawnPosition()
	{
		if (CarriageSpawn == SpawnAnchor.Origin) return Vector2.Zero;

		var marker = ResolveEndMarker(wantFar: CarriageSpawn == SpawnAnchor.Far);
		return marker?.Position ?? Vector2.Zero;
	}

	/// <summary>角色端点 Marker：wantFar = 要"场外/待命端"、false = 要"贴玩家端"。
	/// 与 <see cref="ResolveNow"/> 的判定同源——FlipCarriageEnds 对"出生点"和"限位解释"同时生效。</summary>
	private Marker2D? ResolveEndMarker(bool wantFar)
		=> GetNodeOrNull<Marker2D>(wantFar != FlipCarriageEnds ? CarriageFarMarkerPath : CarriageNearMarkerPath);

	/// <summary>设置滑槽滑动目标（世界坐标，滑动轴）。无 Slot 限位时忽略（滑槽静止）。
	/// 常规跟随用它（速度 = 自身 <see cref="Speed"/>）。</summary>
	public void SetTarget(float coordinate)
	{
		SetTargetInternal(coordinate);
		_targetSpeed = 0f;
	}

	/// <summary>设置目标并**按固定时长到达**：速度 = 本次距离 / <paramref name="duration"/>。
	/// 语义：调用方（满进度退场等）要"总耗时固定"，不关心距离有多远。
	/// 只对**这一次**目标负责——常规的每帧 <see cref="SetTarget"/> 跟随会把速度交还给自己 <see cref="Speed"/>，
	/// 所以用它的那段时间里不要再每帧重设目标（否则等于没设过）。
	/// 无 Slot 限位（滑槽静止）/ duration ≤ 0 / 已在目标上 → 退回自身 Speed。</summary>
	public void SetTargetInTime(float coordinate, float duration)
	{
		float before = CurrentRailCoordinate;
		SetTargetInternal(coordinate);
		_targetSpeed = duration > 0f && _hasTarget
			? Mathf.Abs(_target - before) / duration
			: 0f;
	}

	private void SetTargetInternal(float coordinate)
	{
		if (!HasSlotLimits) return;
		_target = Mathf.Clamp(coordinate, _slotStart, _slotEnd);
		_hasTarget = true;
	}

	/// <summary>解析限位（幂等；机械侧可主动调用以避开节点处理顺序假设）。</summary>
	public void ResolveNow()
	{
		if (_resolved) return;
		_resolved = true;

		var slotStart = GetNodeOrNull<Marker2D>(SlotStartMarkerPath);
		var slotEnd = GetNodeOrNull<Marker2D>(SlotEndMarkerPath);
		if (slotStart != null && slotEnd != null)
		{
			HasSlotLimits = true;
			var a = GetCoordinate(slotStart.GlobalPosition);
			var b = GetCoordinate(slotEnd.GlobalPosition);
			_slotStart = Mathf.Min(a, b);
			_slotEnd = Mathf.Max(a, b);
			SetCoordinate(Mathf.Clamp(CurrentRailCoordinate, _slotStart, _slotEnd));
		}

		// 角色约定（Near = 玩家侧、Far = 场外）在读取后按 FlipCarriageEnds 解释，不排序
		var near = GetNodeOrNull<Marker2D>(CarriageNearMarkerPath);
		var far = GetNodeOrNull<Marker2D>(CarriageFarMarkerPath);
		if (near != null && far != null)
		{
			_carriageNear = GetCarriageCoordinate(near.GlobalPosition);
			_carriageFar = GetCarriageCoordinate(far.GlobalPosition);
			if (FlipCarriageEnds)
				(_carriageNear, _carriageFar) = (_carriageFar, _carriageNear);
		}
		else
		{
			float x = GetCarriageCoordinate(GlobalPosition);
			_carriageNear = x;
			_carriageFar = x;
			GD.PushWarning($"{Name}: 未配置 {CarriageNearMarkerPath}/{CarriageFarMarkerPath}，机械将被锁死在轨道原点。");
		}
	}

	/// <summary>把世界坐标投影到滑动轴（= 该轴分量）。</summary>
	public float GetCoordinate(Vector2 worldPosition)
		=> Axis == RailAxis.X ? worldPosition.X : worldPosition.Y;

	/// <summary>把世界坐标投影到机械的移动轴。</summary>
	public float GetCarriageCoordinate(Vector2 worldPosition)
		=> CarriageAxis == RailAxis.X ? worldPosition.X : worldPosition.Y;

	/// <summary>沿父链查找最近的滑槽（机械用：直接父节点可能是 Mount 挂载点）。</summary>
	public static SlideRailMount? FindFor(Node? node)
	{
		var current = node?.GetParent();
		for (int i = 0; i < 4 && current != null; i++)
		{
			if (current is SlideRailMount rail) return rail;
			current = current.GetParent();
		}
		return null;
	}

	private Node2D EnsureMount()
	{
		var mount = GetNodeOrNull<Node2D>("Mount");
		if (mount == null)
		{
			mount = new Node2D { Name = "Mount" };
			AddChild(mount);
		}
		return mount;
	}

	private void SetCoordinate(float coordinate)
	{
		var pos = GlobalPosition;
		if (Axis == RailAxis.X) pos.X = coordinate;
		else pos.Y = coordinate;
		GlobalPosition = pos;
	}
}

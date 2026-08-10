using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Events;
using System.Collections.Generic;

namespace Kuros.Companions
{
    /// <summary>
    /// P2（2P 伴随角色）控制器：负责跟随/自由移动、朝向、渲染层级、Spine 动画播放、
    /// 受击处理与 Dialogic 气泡。移动采用双模式（自由游走 / 跟随接近），
    /// 状态机（P2.tscn 的 StateMachine）只做行为/动画层，位移唯一由本类驱动。
    /// </summary>
    public partial class P2CompanionController : CharacterBody2D, ICompanionStateSource
    {
        [ExportCategory("Companion State")]
        /// <summary>伴随角色定位名（供外部系统识别用途）。</summary>
        [Export] public string CompanionRoleName { get; set; } = "support";
        /// <summary>报告的最大生命（模拟值，非 GameActor 管线）。</summary>
        [Export(PropertyHint.Range, "1,9999,1")] public int ReportedMaxHp { get; set; } = 100;
        /// <summary>报告的当前生命（受击扣减）。</summary>
        [Export(PropertyHint.Range, "0,9999,1")] public int ReportedCurrentHp { get; set; } = 100;

        /// <summary>角色名（ICompanionStateSource 接口）。</summary>
        public string CompanionName => Name;
        /// <summary>钳制后的当前生命（0~Max）。</summary>
        public int CurrentHp => Mathf.Clamp(ReportedCurrentHp, 0, Mathf.Max(1, ReportedMaxHp));
        /// <summary>最大生命（至少 1）。</summary>
        public int MaxHp => Mathf.Max(1, ReportedMaxHp);
        /// <summary>是否可用（在场景树内且可见）。</summary>
        public bool IsCompanionAvailable => IsInsideTree() && Visible;
        /// <summary>角色定位（ICompanionStateSource 接口）。</summary>
        public string CompanionRole => CompanionRoleName;

        [ExportCategory("Follow")]
        /// <summary>玩家节点路径（默认兄弟节点 MainCharacter）。</summary>
        [Export] public NodePath PlayerPath { get; set; } = new("../MainCharacter");
        /// <summary>玩家身上的跟随锚点节点（MainCharacter 无此节点时回退玩家位置）。</summary>
        [Export] public NodePath CompanionAnchorPath { get; set; } = new("CompanionAnchor");
        /// <summary>跟随偏移（相对玩家，X 按朝向反侧取符号，Y 叠加浮动）。</summary>
        [Export] public Vector2 FollowOffset { get; set; } = new(320f, -80f);
        /// <summary>跟随平滑度：越大收敛越快。</summary>
        [Export(PropertyHint.Range, "0.1,30,0.1")] public float FollowSmoothing { get; set; } = 8.5f;
        /// <summary>每帧最大位移（速度上限 px/秒）。</summary>
        [Export(PropertyHint.Range, "10,5000,1")] public float MaxCatchUpSpeed { get; set; } = 1400f;
        /// <summary>始终跟随玩家背后（偏移取玩家朝向反侧）。</summary>
        [Export] public bool AlwaysFollowBehindPlayer { get; set; } = true;
        /// <summary>保持固定在玩家朝向侧（转身不穿越玩家）。</summary>
        [Export] public bool KeepCompanionOnFacingSide { get; set; } = false;

        [ExportCategory("Floating")]
        /// <summary>跟随点的正弦浮动振幅（px）。</summary>
        [Export(PropertyHint.Range, "0,200,0.1")] public float FloatAmplitude { get; set; } = 22f;
        /// <summary>浮动频率（Hz）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float FloatFrequency { get; set; } = 1.8f;

        [ExportCategory("Render Layer")]
        /// <summary>动态层级开关：按 Y 差/朝向切换 ZIndex（前后遮挡）。</summary>
        [Export] public bool EnableDynamicLayering { get; set; } = true;
        /// <summary>在前（低于玩家）时的 ZIndex 增量。</summary>
        [Export(PropertyHint.Range, "0,200,1")] public int FrontLayerDelta { get; set; } = 0;
        /// <summary>在后（高于玩家）时的 ZIndex 增量。</summary>
        [Export(PropertyHint.Range, "-200,0,1")] public int BackLayerDelta { get; set; } = -1;
        /// <summary>层级切换的死区：超过此差值才切换前后层（防抖动）。</summary>
        [Export(PropertyHint.Range, "0,100,0.1")] public float LayerSwitchDeadZone { get; set; } = 8f;
        /// <summary>按朝向而非 Y 差判断前后层。</summary>
        [Export] public bool LayerByFacingDirection { get; set; } = false;

        [ExportCategory("Free Roam")]
        /// <summary>自由移动开关：在玩家周围环形区域内游走（决策/脚本目标点）。关闭则纯跟随。</summary>
        [Export] public bool EnableFreeRoam { get; set; } = true;
        /// <summary>自由模式环形区域内半径：随机游走点的最小半径。</summary>
        [Export(PropertyHint.Range, "0,1000,10")] public float MoveRangeMin { get; set; } = 500f;
        /// <summary>自由模式环形区域外半径：超出此距离切换到跟随模式。</summary>
        [Export(PropertyHint.Range, "0,2000,10")] public float MoveRangeMax { get; set; } = 2000f;
        /// <summary>跟随模式最小距离：回到此区间内即恢复自由模式。</summary>
        [Export(PropertyHint.Range, "0,1000,10")] public float FollowRangeMin { get; set; } = 300f;
        /// <summary>跟随模式最大距离：跟随模式的目标是进入此区间（接近玩家到 FollowRangeMin~Max 内）。</summary>
        [Export(PropertyHint.Range, "0,2000,10")] public float FollowRangeMax { get; set; } = 500f;
        /// <summary>空闲游走间隔（秒）：每隔一段时间随机生成新目标点。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float WanderInterval { get; set; } = 2.0f;
        /// <summary>到达目标点的判定距离（px）。</summary>
        [Export(PropertyHint.Range, "5,100,5")] public float ArriveDistance { get; set; } = 20f;

        [ExportCategory("Combat")]
        /// <summary>受击免疫窗口（秒）：受击后此期间忽略后续伤害。</summary>
        [Export(PropertyHint.Range, "0,5,0.1")] public float HitInvincibilityDuration { get; set; } = 0.5f;

        [ExportCategory("Visual")]
        /// <summary>要镜像/翻转的精灵路径（场景节点名 SpineSprite）。</summary>
        [Export] public NodePath SpritePath { get; set; } = new("SpineSprite");

        [ExportCategory("Dialogic Hint")]
        /// <summary>
        /// P2 的 Dialogic 角色资源路径（.dch）。气泡会跟随 BubbleAnchorPath 节点位置显示。
        /// 需要在 Dialogic Variables 设置中定义 'p2_hint_text' 变量（默认值为空字符串）。
        /// </summary>
        [Export(PropertyHint.File, "*.dch")] public string P2CharacterPath { get; set; } = "res://dialogic/character/P2.dch";
        /// <summary>气泡锚点节点（相对于 P2CompanionController 自身）。留空则以自身位置为锚点。</summary>
        [Export] public NodePath BubbleAnchorPath { get; set; } = new(".");
        /// <summary>气泡自动关闭时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.5,10,0.1")] public float HintDisplaySeconds { get; set; } = 2.2f;
        /// <summary>气泡队列最大长度（超出丢弃新 hint）。</summary>
        [Export(PropertyHint.Range, "1,20,1")] public int MaxHintQueueSize { get; set; } = 6;

        [ExportCategory("Debug")]
        /// <summary>调试热键开关（按下推送 "combat" 气泡）。</summary>
        [Export] public bool EnableDebugHintHotkey { get; set; } = true;
        /// <summary>调试热键。</summary>
        [Export] public Key DebugHintKey { get; set; } = Key.F7;

        private MainCharacter? _player;
        private Node2D? _companionAnchor;
        private Node2D? _sprite;
        private float _hoverClock;      // 跟随浮动时钟
        private int _layerSign = 1;     // 当前前后层符号
        /// <summary>移动模式：自由游走（环形范围）或跟随（接近玩家到跟随范围）。</summary>
        private enum RoamMode { FreeRoam, Follow }

        private RoamMode _mode = RoamMode.FreeRoam;

        /// <summary>当前是否处于跟随模式（Walk 状态据此切换 move/walk 动画）。</summary>
        public bool IsFollowingMode => _mode == RoamMode.Follow;
        private Vector2? _moveTarget;              // 决策/游走指定的移动目标（世界坐标），null = 跟随
        private float _wanderTimer;                // 空闲游走计时
        private float _hitInvincibilityRemaining;  // 受击免疫剩余时间
        private bool _pendingAction;               // 等待接近玩家后触发的 action（决策动作两阶段）

        // Dialogic 气泡队列
        private readonly Queue<string> _hintQueue = new();
        private bool _dialogicBusy;                // 气泡正在播放
        private bool _waitingForHintEnd;           // 等待当前气泡结束
        private GodotObject? _dialogic;            // /root/Dialogic 单例引用
        private Callable _timelineEndedCallable;

        public override void _Ready()
        {
            AddToGroup("companions"); // 供 GameStateProvider 组回退识别

            // 订阅 Dialogic 的 timeline 结束信号（气泡队列推进）
            _dialogic = GetNodeOrNull("/root/Dialogic");
            if (_dialogic != null)
            {
                _timelineEndedCallable = Callable.From(OnDialogicTimelineEnded);
                _dialogic.Connect("timeline_ended", _timelineEndedCallable);
            }

            ResolveReferences();

            if (_player != null)
            {
                // 初始放到跟随点、同步朝向与层级，并播 "ready" 气泡
                GlobalPosition = ComputeFollowPosition(_player.GlobalPosition);
                UpdateVisualFacing(GlobalPosition); // 初始无移动目标 → 保持默认朝向
                UpdateDynamicLayering();
                PushHint("ready");
            }

            // 初始化状态机（P2.tscn 的 StateMachine 节点，状态见 scripts/companions/states/）
            GetNodeOrNull<Kuros.Systems.FSM.StateMachine>("StateMachine")?.Initialize(this);
        }

        /// <summary>播放 Spine 动画：主 SpineSprite + OutlineLayer 下全部描边精灵同步播放（仿 MainCharacter）。
        /// 主精灵未挂 SpineController 时跳过（由 P2.tscn 配置），outline 始终生效。</summary>
        public void PlaySpineAnimation(string animationName, bool loop = true)
        {
            if (string.IsNullOrWhiteSpace(animationName)) return;

            var spine = GetNodeOrNull<Node>("SpineSprite");
            if (spine != null && spine.HasMethod("play"))
                spine.Call("play", animationName, loop);

            var outlineLayer = GetNodeOrNull<Node>("OutlineLayer");
            if (outlineLayer == null) return;
            foreach (Node child in outlineLayer.GetChildren())
            {
                if (child.HasMethod("play"))
                    child.Call("play", animationName, loop);
            }
        }

        public override void _ExitTree()
        {
            // 退订 Dialogic 信号，防止悬挂回调
            if (_dialogic != null && IsInstanceValid(_dialogic)
                && _dialogic.IsConnected("timeline_ended", _timelineEndedCallable))
            {
                _dialogic.Disconnect("timeline_ended", _timelineEndedCallable);
            }
        }

        public override void _Notification(int what)
        {
            // P2 被隐藏时（过场 HideNodePaths 触发），立即取消正在显示的 hint 气泡
            if (what == NotificationVisibilityChanged && !Visible)
                CancelActiveHint();
        }

        /// <summary>取消当前气泡并清空队列（隐藏/过场时调用）。</summary>
        private void CancelActiveHint()
        {
            _hintQueue.Clear();
            if (!_waitingForHintEnd) return;
            _waitingForHintEnd = false;
            _dialogicBusy = false;
            _dialogic ??= GetNodeOrNull("/root/Dialogic");
            if (_dialogic != null && IsInstanceValid(_dialogic) && _dialogic.HasMethod("end_timeline"))
                _dialogic.Call("end_timeline");
        }

        public override void _PhysicsProcess(double delta)
        {
            ResolveReferences();
            if (_player == null)
            {
                return;
            }

            _hoverClock += (float)delta;

            // 受击免疫计时
            if (_hitInvincibilityRemaining > 0f)
                _hitInvincibilityRemaining -= (float)delta;

            // 计算移动目标（双模式：自由游走/跟随），Lerp 指数平滑 + 限速位移
            Vector2 target = ComputeMovementTarget((float)delta);
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, FollowSmoothing) * (float)delta);
            Vector2 next = GlobalPosition.Lerp(target, blend);

            float maxStep = Mathf.Max(10f, MaxCatchUpSpeed) * (float)delta;
            Vector2 step = next - GlobalPosition;
            if (step.Length() > maxStep)
            {
                next = GlobalPosition + step.Normalized() * maxStep;
            }

            GlobalPosition = next;

            UpdateMotionState(); // 状态机：有目标 → Walk，否则 Idle（位移唯一由本方法驱动）
            UpdateVisualFacing(target);
            UpdateDynamicLayering();

            if (EnableDebugHintHotkey && Input.IsKeyPressed(DebugHintKey))
            {
                PushHint("combat");
            }
        }

        /// <summary>
        /// 移动目标解析（P2 位移的唯一驱动者，状态机不参与位置计算）。
        /// 双模式：
        /// - 自由模式（FreeRoam）：在 MoveRangeMin~Max 环形区域内游走；超出 MoveRangeMax 切换到跟随模式；
        /// - 跟随模式（Follow）：接近玩家直到进入 FollowRangeMin~Max 区间，然后恢复自由模式。
        /// 自由模式下：显式目标（决策）→ 移向目标；无目标 → 定时生成随机点；等待期间停在原地。
        /// </summary>
        private Vector2 ComputeMovementTarget(float delta)
        {
            Vector2 anchor = _companionAnchor?.GlobalPosition ?? _player!.GlobalPosition;
            float distToPlayer = GlobalPosition.DistanceTo(anchor);

            // ── 模式切换 ──────────────────────────────────────────
            if (_mode == RoamMode.FreeRoam && distToPlayer > MoveRangeMax)
            {
                // 超出自由范围 → 切跟随模式（清空目标，接近玩家）
                _mode = RoamMode.Follow;
                _moveTarget = null;
            }
            else if (_mode == RoamMode.Follow && !_pendingAction
                     && distToPlayer <= FollowRangeMax
                     && distToPlayer >= FollowRangeMin)
            {
                // 回到跟随范围区间 → 恢复自由模式（挂起 action 时保持跟随直到触发）
                _mode = RoamMode.FreeRoam;
            }

            // ── 跟随模式：目标 = 玩家位置（接近到 FollowRange 区间后由模式切换恢复自由）──
            if (_mode == RoamMode.Follow)
            {
                TryFirePendingAction(distToPlayer); // 两阶段动作：接近到 FollowRangeMin 内触发
                return anchor;
            }

            // ── 自由模式 ──────────────────────────────────────────
            // 1. 显式移动目标优先
            if (_moveTarget.HasValue)
            {
                if (GlobalPosition.DistanceTo(_moveTarget.Value) < ArriveDistance)
                {
                    // 到达：清空目标并重置计时，等 WanderInterval 后再生成下一个，
                    // 避免目标过近时连续快速切换导致来回频率过高
                    _moveTarget = null;
                    _wanderTimer = WanderInterval;
                }
                else
                {
                    return _moveTarget.Value;
                }
            }

            // 2. 常态游走：无目标时定时生成环形内随机目标点
            if (EnableFreeRoam && !_moveTarget.HasValue)
            {
                _wanderTimer -= delta;
                if (_wanderTimer <= 0f)
                {
                    _wanderTimer = WanderInterval;
                    _moveTarget = GenerateWanderTarget(anchor);
                }
                else
                {
                    // 等待下一个随机点期间：停在原地（不回跟随点，避免往返玩家身边）
                    return GlobalPosition;
                }
            }

            // 3. 默认跟随玩家偏移点（纯跟随模式 EnableFreeRoam=false 时）
            return ComputeFollowPosition(anchor);
        }

        /// <summary>原跟随逻辑：玩家偏移点 + 朝向反侧 + 正弦浮动。</summary>
        private Vector2 ComputeFollowPosition(Vector2 anchor)
        {
            float sideSign;
            if (AlwaysFollowBehindPlayer)
            {
                // 跟随玩家背后：偏移取朝向反侧
                sideSign = _player!.FacingRight ? -1f : 1f;
            }
            else
            {
                sideSign = _player.FacingRight ? 1f : -1f;
                if (!KeepCompanionOnFacingSide)
                {
                    // 保持世界空间固定侧（转身不穿越玩家）
                    sideSign = GlobalPosition.X >= anchor.X ? 1f : -1f;
                }
            }

            // 正弦浮动
            float hover = Mathf.Sin(_hoverClock * Mathf.Tau * FloatFrequency) * FloatAmplitude;
            return anchor + new Vector2(FollowOffset.X * sideSign, FollowOffset.Y + hover);
        }

        /// <summary>生成环形区域（Min~Max）内的随机游走目标点。</summary>
        private Vector2 GenerateWanderTarget(Vector2 anchor)
        {
            float angle = GD.Randf() * Mathf.Tau;
            float radius = Mathf.Lerp(MoveRangeMin, MoveRangeMax, GD.Randf());
            return anchor + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        /// <summary>设置移动目标（世界坐标）。决策/游走调用；null 恢复跟随。</summary>
        public void SetMoveTarget(Vector2? target)
        {
            _moveTarget = target;
        }

        /// <summary>停止当前移动目标（受击硬直时由 Hit 状态调用）。</summary>
        public void StopMoving()
        {
            _moveTarget = null;
        }

        /// <summary>
        /// 执行决策动作（治疗/护盾等）：两阶段——先进入跟随模式接近玩家，
        /// 到达 FollowRangeMin 内（TryFirePendingAction）才切 Action 状态播 action 动画。
        /// </summary>
        public void TriggerAction()
        {
            _mode = RoamMode.Follow;
            _moveTarget = null;
            _pendingAction = true;
        }

        /// <summary>跟随模式接近玩家到 FollowRangeMin 内时，触发挂起的 action 动画。</summary>
        private void TryFirePendingAction(float distToPlayer)
        {
            if (!_pendingAction) return;
            if (distToPlayer > FollowRangeMin) return; // 还没接近到动作范围
            _pendingAction = false;
            GetNodeOrNull<Kuros.Systems.FSM.StateMachine>("StateMachine")?.ChangeState("Action");
        }

        /// <summary>状态机协作：自由模式有移动目标 → Walk；跟随模式（移向玩家，目标已被清空）→ Walk；否则 Idle。
        /// 跟随模式必须算作移动，否则状态停在 Idle 播 walk 循环，move 动画永远无法播放。
        /// 只在 Walk/Idle 之间切换：当前处于 Action/Hit/Stun 等动作状态时不打断，
        /// 否则每帧的 ChangeState 会在下一帧把 action 动画顶掉。</summary>
        private void UpdateMotionState()
        {
            var sm = GetNodeOrNull<Kuros.Systems.FSM.StateMachine>("StateMachine");
            if (sm == null) return;

            // 动作/受击/眩晕等状态不打断（播完自行回 Idle 后再接管）
            string current = sm.CurrentState?.Name ?? string.Empty;
            if (current != "Walk" && current != "Idle") return;

            string targetState = _moveTarget.HasValue || _mode == RoamMode.Follow ? "Walk" : "Idle";
            if (current != targetState)
                sm.ChangeState(targetState);
        }

        /// <summary>
        /// 受击入口（DamageDispatcher 通过 HasMethod("TakeDamage") 命中）。
        /// 扣 HP + 免疫窗口 + 切 Hit 状态（播 hit 动画）。
        /// </summary>
        public void TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null,
            DamageSource damageSource = DamageSource.DirectAttack)
        {
            if (damage <= 0) return;
            if (_hitInvincibilityRemaining > 0f) return; // 免疫窗口内忽略
            if (ReportedCurrentHp <= 0) return;

            ReportedCurrentHp = Mathf.Max(0, ReportedCurrentHp - damage);
            _hitInvincibilityRemaining = HitInvincibilityDuration;

            GetNodeOrNull<Kuros.Systems.FSM.StateMachine>("StateMachine")?.ChangeState("Hit");
        }

        /// <summary>解析玩家/精灵/锚点引用（三级回退：相对路径 → ../归一化 → 组回退）。</summary>
        private void ResolveReferences()
        {
            if (_player == null || !IsInstanceValid(_player) || !_player.IsInsideTree())
            {
                _player = GetNodeOrNull<MainCharacter>(PlayerPath)
                    ?? GetNodeOrNull<MainCharacter>(NormalizeRelativePath(PlayerPath))
                    ?? GetTree().GetFirstNodeInGroup("player") as MainCharacter;
            }

            if (_player == null)
            {
                _companionAnchor = null;
                return;
            }

            _sprite ??= GetNodeOrNull<Node2D>(SpritePath);

            if (_companionAnchor == null || !IsInstanceValid(_companionAnchor) || !_companionAnchor.IsInsideTree())
            {
                _companionAnchor = _player.GetNodeOrNull<Node2D>(CompanionAnchorPath)
                    ?? _player.FindChild(CompanionAnchorPath.ToString(), recursive: true, owned: false) as Node2D;
            }
        }

        /// <summary>动态渲染层级：按 Y 差（或朝向）切换 ZIndex，实现前后遮挡。</summary>
        private void UpdateDynamicLayering()
        {
            if (!EnableDynamicLayering || _player == null)
            {
                return;
            }

            if (AlwaysFollowBehindPlayer)
            {
                // 跟随背后：固定在后层
                ZIndex = _player.ZIndex + BackLayerDelta;
                return;
            }

            if (LayerByFacingDirection)
            {
                // 按朝向判断前后：与玩家朝向同侧在前
                float xDiff = GlobalPosition.X - _player.GlobalPosition.X;
                if (Mathf.Abs(xDiff) > Mathf.Max(0f, LayerSwitchDeadZone))
                {
                    bool sameSideAsFacing = _player.FacingRight ? xDiff >= 0f : xDiff <= 0f;
                    _layerSign = sameSideAsFacing ? 1 : -1;
                }
            }
            else
            {
                // 按 Y 差判断前后：Y 更大（下方）在前
                float yDiff = GlobalPosition.Y - _player.GlobalPosition.Y;
                if (Mathf.Abs(yDiff) > Mathf.Max(0f, LayerSwitchDeadZone))
                {
                    _layerSign = yDiff >= 0f ? 1 : -1;
                }
            }

            int delta = _layerSign >= 0 ? FrontLayerDelta : BackLayerDelta;
            ZIndex = _player.ZIndex + delta;
        }

        /// <summary>
        /// 朝向同步：统一不跟随玩家朝向，只跟随移动 X 轴方向（垂直移动/站定时保持当前朝向）。
        /// Spine 角色翻转用 Scale.X（同 GameActor 的 spine 翻转方式），保留原缩放绝对值（P2 根 Scale 0.33）。
        /// </summary>
        private void UpdateVisualFacing(Vector2 target)
        {
            if (_player == null)
            {
                return;
            }

            // 按移动 X 轴方向朝向（垂直移动/站定保持当前朝向，不受玩家朝向影响）
            float dx = target.X - GlobalPosition.X;
            if (Mathf.Abs(dx) < 0.1f) return;
            float sign = dx > 0f ? 1f : -1f;

            // 主精灵
            if (_sprite != null)
                _sprite.Scale = new Vector2(Mathf.Abs(_sprite.Scale.X) * sign, _sprite.Scale.Y);

            // OutlineLayer 下的描边精灵同步翻转
            var outlineLayer = GetNodeOrNull<Node2D>("OutlineLayer");
            if (outlineLayer == null) return;
            foreach (Node child in outlineLayer.GetChildren())
            {
                if (child is Node2D outline)
                    outline.Scale = new Vector2(Mathf.Abs(outline.Scale.X) * sign, outline.Scale.Y);
            }
        }

        /// <summary>相对路径归一化：无 ../ 前缀时补上（相对本节点的路径统一为 ../ 形式）。</summary>
        private static NodePath NormalizeRelativePath(NodePath path)
        {
            string text = path.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("../", System.StringComparison.Ordinal))
            {
                return path;
            }

            return new NodePath($"../{text}");
        }

        /// <summary>
        /// 播放 p2_hint timeline 中对应 label 的对话气泡。
        /// hintKey 对应 p2_hint.dtl 中的 label 名称（如 "ready"、"combat"）。
        /// 在 Dialogic 编辑器中编辑 dialogic/timeline/p2_hint.dtl 来维护文本。
        /// </summary>
        public void PushHint(string hintKey)
        {
            if (string.IsNullOrWhiteSpace(hintKey))
                return;

            // 过场播放期间禁止触发 hint
            var cutsceneManager = GetTree().GetFirstNodeInGroup("cutscene_manager");
            if (cutsceneManager is Kuros.Systems.Cutscene.CutsceneManager cm && cm.IsPlaying)
                return;

            _dialogic ??= GetNodeOrNull("/root/Dialogic");
            if (_dialogic == null || !IsInstanceValid(_dialogic))
                return;

            // 如果 Dialogic 正在播放非本 hint 的 Timeline（例如剧情对话），则放弃
            var currentTimeline = _dialogic.Get("current_timeline");
            if (currentTimeline.VariantType != Variant.Type.Nil && !_waitingForHintEnd)
                return;

            // 气泡播放中：入队等待（超队列上限丢弃）
            if (_dialogicBusy)
            {
                if (_hintQueue.Count < Mathf.Max(1, MaxHintQueueSize))
                    _hintQueue.Enqueue(hintKey);
                return;
            }

            StartDialogicHint(hintKey);
        }

        /// <summary>
        /// 显示运行时动态生成的文本（如 AI 个性台词），文本不在 DTL 中预定义。
        /// 通过 Dialogic 变量 "p2_hint_text" 注入后播放 p2_hint.dtl 的 label:direct。
        /// 需在 Dialogic 编辑器 Variables 中预先定义 "p2_hint_text" 变量（默认值留空即可）。
        /// </summary>
        public void PushHintDirect(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return;

            _dialogic ??= GetNodeOrNull("/root/Dialogic");
            if (_dialogic == null || !IsInstanceValid(_dialogic))
                return;

            // 在启动 timeline 前注入变量，label:direct 中的 {p2_hint_text} 会读取该值
            _dialogic.Get("VAR").AsGodotObject()?.Call("set_variable", "p2_hint_text", rawText);
            PushHint("direct");
        }

        /// <summary>启动一条 Dialogic 气泡（标记忙碌 → 定位角色 → 到时自动结束）。</summary>
        private void StartDialogicHint(string hintKey)
        {
            if (_dialogic == null || !IsInstanceValid(_dialogic))
                return;

            _dialogicBusy = true;
            _waitingForHintEnd = true;

            // 若还没有激活的 Layout，先加载 textbubble_A 样式
            var styles = _dialogic.Get("Styles").AsGodotObject();
            if (styles != null && !(bool)styles.Call("has_active_layout_node"))
                styles.Call("load_style", "textbubble_A");

            // 以 label 为入口启动 p2_hint timeline（文本全部定义在 dtl 文件中）
            var layoutNode = _dialogic.Call("start", "p2_hint", hintKey).AsGodotObject() as Node;

            // 将气泡定位到 BubbleAnchorPath 指定节点
            if (!string.IsNullOrEmpty(P2CharacterPath) && !BubbleAnchorPath.IsEmpty && layoutNode != null)
            {
                var anchor = GetNodeOrNull<Node2D>(BubbleAnchorPath);
                if (anchor != null)
                    layoutNode.CallDeferred("register_character", P2CharacterPath, anchor);
            }

            // 到时后自动结束（若玩家未手动推进）
            float delay = Mathf.Max(0.5f, HintDisplaySeconds);
            GetTree().CreateTimer(delay).Timeout += () =>
            {
                if (_waitingForHintEnd && _dialogic != null && IsInstanceValid(_dialogic))
                    _dialogic.Call("end_timeline");
            };
        }

        /// <summary>Dialogic timeline 结束回调：解除忙碌并推进队列中的下一条气泡。</summary>
        private void OnDialogicTimelineEnded()
        {
            if (!_waitingForHintEnd)
                return;

            _waitingForHintEnd = false;
            _dialogicBusy = false;

            if (_hintQueue.Count > 0)
                StartDialogicHint(_hintQueue.Dequeue());
        }
    }
}

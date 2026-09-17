using System.Threading.Tasks;
using Godot;
using Kuros.Managers;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 过场动画统一管理器，挂到 Stage_2（BattleScene 根节点下）。
    /// 负责：统一跳过逻辑 / 禁用玩家输入 / 镜头接管 / 黑幕淡变。
    ///
    /// Inspector 必填项：
    /// PlayerPath       → 相对路径指向玩家节点（如 "World/MainCharacter"）
    /// CameraPath       → 相对路径指向 Camera2D（如 "World/MainCharacter/Camera2D"）
    /// DialoguePanelPath→ 可选，指向 CutsceneDialoguePanel 节点
    /// FadeOverlayPath  → 可选，指向全屏黑幕 CanvasItem
    /// TopBlackBarPath → 可选，指向电影式黑幕上方 ColorRect
    /// BottomBlackBarPath → 可选，指向电影式黑幕下方 ColorRect
    /// HideNodePaths    → 可选，过场期间隐藏并禁用 ProcessMode 的节点路径列表（如 P2、UI 根节点）
    /// BattleSceneManagerPath → 可选，过场开始时自动调用 HideAllUI()，结束时调用 ShowAllUI()，指向 BattleSceneManager 节点
    /// </summary>
    [GlobalClass]
    public partial class CutsceneManager : Node
    {
        // ── 信号 ──────────────────────────────────────────────────────────
        [Signal] public delegate void CutsceneStartedEventHandler(string sequenceId);
        [Signal] public delegate void CutsceneFinishedEventHandler(string sequenceId);

        // ── 导出属性 ──────────────────────────────────────────────────────
        /// <summary>跳过按键动作名（Input Map 中定义；默认 = 专用动作 cutscene_skip，可在设置菜单改键）。
        /// 按住它 <see cref="SkipHoldSeconds"/> 秒才触发跳过。</summary>
        [Export] public string SkipActionName { get; set; } = Kuros.Core.InputActions.CutsceneSkip;

        [Export] public NodePath PlayerPath        { get; set; } = new NodePath();
        [Export] public NodePath CameraPath        { get; set; } = new NodePath();
        [Export] public NodePath DialoguePanelPath { get; set; } = new NodePath();
        [Export] public NodePath FadeOverlayPath   { get; set; } = new NodePath();

        /// <summary>
        /// 电影式黑幕：上方黑色 ColorRect 的节点路径（用于 FadeStep 电影模式）
        /// </summary>
        [Export] public NodePath TopBlackBarPath { get; set; } = new NodePath();

        /// <summary>
        /// 电影式黑幕：下方黑色 ColorRect 的节点路径（用于 FadeStep 电影模式）
        /// </summary>
        [Export] public NodePath BottomBlackBarPath { get; set; } = new NodePath();

        /// <summary>
        /// 过场期间隐藏并禁用 ProcessMode 的节点（如 P2、UI 根节点）。
        /// 结束后自动恢复显示和 ProcessMode。
        /// </summary>
        [Export] public Godot.Collections.Array<NodePath> HideNodePaths { get; set; } = new();

        /// <summary>
        /// 指向 BattleSceneManager 节点的路径。
        /// 设置后，过场开始时自动调用 HideAllUI()，结束时调用 ShowAllUI()。
        /// </summary>
        [Export] public NodePath BattleSceneManagerPath { get; set; } = new NodePath();

        [ExportCategory("Skip 跳过（长按）")]
        /// <summary>长按多久才触发跳过（秒）。0 = 立刻跳过（旧行为）。</summary>
        [Export(PropertyHint.Range, "0,3,0.05")] public float SkipHoldSeconds { get; set; } = 1.0f;

        /// <summary>松开后进度倒退的倍率（1 = 与填充同速）。</summary>
        [Export(PropertyHint.Range, "0.5,5,0.1")] public float SkipRewindMultiplier { get; set; } = 1.0f;

        /// <summary>承载跳过 HUD 的 CanvasLayer 层级——必须高于电影黑条所在层（各 Stage 为 128）。</summary>
        [Export(PropertyHint.Range, "0,200,1")] public int SkipHudLayer { get; set; } = 129;

        /// <summary>跳过 HUD 场景（空 = 默认加载 res://scenes/ui/hud/CutsceneSkipHUD.tscn）。</summary>
        [Export] public PackedScene? SkipHudScene { get; set; }

        // ── 公开状态 ──────────────────────────────────────────────────────
        public bool IsPlaying { get; private set; } = false;

        // ── 内部访问（供 Step 使用）──────────────────────────────────────
        internal bool                  IsSkipRequested { get; private set; } = false;
        internal Node2D?               Player          { get; private set; }
        internal Camera2D?             Camera          { get; private set; }
        internal CutsceneDialoguePanel? DialoguePanel  { get; private set; }
        internal CanvasItem?           FadeOverlay     { get; private set; }
        internal Control?              TopBlackBar     { get; private set; }
        internal Control?              BottomBlackBar  { get; private set; }

        // ── 私有字段 ──────────────────────────────────────────────────────
        private bool    _cameraWasTopLevel = false;
        private bool    _playerWasVisible  = false;
        private readonly System.Collections.Generic.List<CanvasItem> _hiddenNodes = new();
        private Kuros.Scenes.BattleSceneManager? _battleSceneManager;
        private CameraZoneManager? _cameraZoneManager;
        // 长按跳过累加器（秒）与 HUD
        private float _skipHold = 0f;
        private CanvasLayer? _skipHudLayer;
        private Kuros.UI.CutsceneSkipHUD? _skipHud;

        /// <summary>长按跳过进度 0..1（供 HUD 驱动环形进度）。</summary>
        public float SkipHoldProgress => SkipHoldSeconds > 0f
            ? Mathf.Clamp(_skipHold / SkipHoldSeconds, 0f, 1f)
            : 0f;
        // ── 生命周期 ──────────────────────────────────────────────────────
        public override void _Ready()
        {
            AddToGroup("cutscene_manager");

            SetupSkipHud();

            if (!PlayerPath.IsEmpty)
                Player = GetNodeOrNull<Node2D>(PlayerPath);

            if (!CameraPath.IsEmpty)
                Camera = GetNodeOrNull<Camera2D>(CameraPath);

            if (!DialoguePanelPath.IsEmpty)
                DialoguePanel = GetNodeOrNull<CutsceneDialoguePanel>(DialoguePanelPath);

            if (!FadeOverlayPath.IsEmpty)
                FadeOverlay = GetNodeOrNull<CanvasItem>(FadeOverlayPath);

            if (!TopBlackBarPath.IsEmpty)
                TopBlackBar = GetNodeOrNull<Control>(TopBlackBarPath);

            if (!BottomBlackBarPath.IsEmpty)
                BottomBlackBar = GetNodeOrNull<Control>(BottomBlackBarPath);

            if (!BattleSceneManagerPath.IsEmpty)
                _battleSceneManager = GetNodeOrNull<Kuros.Scenes.BattleSceneManager>(BattleSceneManagerPath);

            // 若 NodePath 查找失败（节点顺序问题），延迟一帧再试
            if (Player == null || Camera == null)
                CallDeferred(MethodName.LateInit);
        }

        private void LateInit()
        {
            if (Player == null && !PlayerPath.IsEmpty)
                Player = GetNodeOrNull<Node2D>(PlayerPath);
            if (Camera == null && !CameraPath.IsEmpty)
                Camera = GetNodeOrNull<Camera2D>(CameraPath);
            if (DialoguePanel == null && !DialoguePanelPath.IsEmpty)
                DialoguePanel = GetNodeOrNull<CutsceneDialoguePanel>(DialoguePanelPath);
            if (FadeOverlay == null && !FadeOverlayPath.IsEmpty)
                FadeOverlay = GetNodeOrNull<CanvasItem>(FadeOverlayPath);

            // 若仍为 null，尝试通过 group 查找玩家（兜底）
            if (Player == null)
                Player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            if (Player != null && Camera == null)
                Camera = Player.GetNodeOrNull<Camera2D>("Camera2D");

            if (_battleSceneManager == null && !BattleSceneManagerPath.IsEmpty)
                _battleSceneManager = GetNodeOrNull<Kuros.Scenes.BattleSceneManager>(BattleSceneManagerPath);

            GD.Print($"[Cutscene] LateInit — Player: {(Player != null ? Player.Name : "null")}, Camera: {(Camera != null ? Camera.Name : "null")}, BattleSceneManager: {(_battleSceneManager != null ? _battleSceneManager.Name : "null")}");
        }

        public override void _Process(double delta)
        {
            UpdateSkipHold((float)delta);
        }

        /// <summary>
        /// 长按跳过：按住跳过键（或鼠标按住 HUD 环）开始累加，松手按 <see cref="SkipRewindMultiplier"/> 倒退；
        /// 累计满 <see cref="SkipHoldSeconds"/> 才真正置 IsSkipRequested（"快进到终态"的语义由各 Step 处理）。
        /// 注意：不复用 SamplePlayer 的 InputHoldTracker —— 过场期间玩家节点是 ProcessMode.Disabled，那个 tracker 不推进。
        /// </summary>
        private void UpdateSkipHold(float delta)
        {
            if (!IsPlaying || SkipHoldSeconds <= 0f)
            {
                _skipHold = 0f;
                return;
            }

            bool holding = Input.IsActionPressed(SkipActionName) || (_skipHud?.IsButtonHeld ?? false);
            _skipHold = holding
                ? Mathf.Min(_skipHold + delta, SkipHoldSeconds)
                : Mathf.Max(_skipHold - delta * Mathf.Max(0.01f, SkipRewindMultiplier), 0f);

            if (_skipHold >= SkipHoldSeconds)
            {
                _skipHold = 0f;
                IsSkipRequested = true;
                GD.Print("[Cutscene] 长按跳过触发");
            }
        }

        /// <summary>建立跳过 HUD（自建 CanvasLayer，层级必须高于电影黑条）。</summary>
        private void SetupSkipHud()
        {
            PackedScene? scene = SkipHudScene;
            if (scene == null)
            {
                const string defaultPath = "res://scenes/ui/hud/CutsceneSkipHUD.tscn";
                if (ResourceLoader.Exists(defaultPath))
                    scene = GD.Load<PackedScene>(defaultPath);
            }

            if (scene == null)
            {
                GD.PushWarning("[Cutscene] 跳过 HUD 场景未配置且默认路径不存在，长按跳过按钮不可用");
                return;
            }

            _skipHudLayer = new CanvasLayer { Name = "CutsceneSkipLayer", Layer = SkipHudLayer };
            AddChild(_skipHudLayer);
            _skipHud = scene.Instantiate<Kuros.UI.CutsceneSkipHUD>();
            _skipHudLayer.AddChild(_skipHud);
        }

        // ── 公开 API ──────────────────────────────────────────────────────
        /// <summary>播放一段过场动画序列。已在播放中则忽略。</summary>
        public async Task PlayCutscene(CutsceneSequence sequence)
        {
            if (IsPlaying) return;

            GD.Print($"[Cutscene] === PlayCutscene 开始: {sequence.SequenceId}, Steps数量: {sequence.Steps?.Count ?? 0} ===");
            GD.Print($"[Cutscene] DisablePlayerInput={sequence.DisablePlayerInput}, TakeOverCamera={sequence.TakeOverCamera}");
            GD.Print($"[Cutscene] Player节点: {(Player != null ? Player.Name : "null")}");
            GD.Print($"[Cutscene] Camera节点: {(Camera != null ? Camera.Name : "null")}");
            GD.Print($"[Cutscene] DialoguePanel: {(DialoguePanel != null ? DialoguePanel.Name : "null")}");
            GD.Print($"[Cutscene] FadeOverlay: {(FadeOverlay != null ? FadeOverlay.Name : "null")}");

            IsPlaying       = true;
            IsSkipRequested = false;
            _skipHold       = 0f;   // 每段过场从零开始的长按进度

            EmitSignal(SignalName.CutsceneStarted, sequence.SequenceId);

            // 隐藏 BattleSceneManager 管理的 UI
            _battleSceneManager?.HideAllUI();

            // 禁用玩家输入，并同时隐藏玩家（Shadow 等子节点随父节点一起消失）
            _playerWasVisible = false;
            if (sequence.DisablePlayerInput)
            {
                if (Player != null)
                {
                    Player.ProcessMode = ProcessModeEnum.Disabled;
                    if (Player.Visible)
                    {
                        Player.Hide();
                        _playerWasVisible = true;
                    }
                    GD.Print($"[Cutscene] 禁用+隐藏: {Player.Name}");
                }
                else
                {
                    GD.PrintErr("[Cutscene] DisablePlayerInput=true 但 Player 节点为 null，请检查 PlayerPath");
                }
            }

            // 隐藏节点并同时禁用其 ProcessMode（防止角色仍在移动/运算）
            _hiddenNodes.Clear();
            foreach (var path in HideNodePaths)
            {
                var node = GetNodeOrNull<CanvasItem>(path);
                if (node != null)
                {
                    if (node.Visible)
                    {
                        node.Hide();
                        _hiddenNodes.Add(node);
                    }
                    node.ProcessMode = ProcessModeEnum.Disabled;
                    GD.Print($"[Cutscene] 隐藏+禁用: {node.Name}");
                }
                else
                {
                    GD.PrintErr($"[Cutscene] HideNodePaths 中路径 '{path}' 未找到节点");
                }
            }

            // 接管摄像机
            if (sequence.TakeOverCamera)
                BeginCameraOverride();

            // 依次执行步骤
            var ctx = new CutsceneContext(this, sequence);
            int stepIndex = 0;
            foreach (var step in sequence.Steps ?? new Godot.Collections.Array<CutsceneStep>())
            {
                if (step == null)
                {
                    GD.PrintErr($"[Cutscene] 第 {stepIndex} 步为 null，跳过");
                    stepIndex++;
                    continue;
                }
                // 跳过请求：默认仍会执行该步骤（各步骤在 Execute 里对 ctx.IsSkipping 做"瞬时落终态"），
                // 只有显式覆写 ExecuteOnSkip=false 的步骤才被整步取消。
                if (IsSkipRequested && !step.ExecuteOnSkip)
                {
                    GD.Print($"[Cutscene] 跳过请求，跳过第 {stepIndex} 步: {step.GetType().Name}");
                    stepIndex++;
                    continue;
                }
                GD.Print($"[Cutscene] 执行第 {stepIndex} 步: {step.GetType().Name}");
                await step.Execute(ctx);
                GD.Print($"[Cutscene] 第 {stepIndex} 步完成: {step.GetType().Name}");
                stepIndex++;
            }

            // 还原
            if (sequence.TakeOverCamera)
                EndCameraOverride();

            // 恢复玩家输入与可见性
            if (sequence.DisablePlayerInput && Player != null && GodotObject.IsInstanceValid(Player))
            {
                Player.ProcessMode = ProcessModeEnum.Inherit;
                if (_playerWasVisible)
                    Player.Show();
                GD.Print($"[Cutscene] 恢复输入+显示: {Player.Name}");
            }

            // 恢复隐藏节点的显示和 ProcessMode
            foreach (var node in _hiddenNodes)
            {
                if (GodotObject.IsInstanceValid(node))
                {
                    node.Show();
                    node.ProcessMode = ProcessModeEnum.Inherit;
                    GD.Print($"[Cutscene] 恢复显示+启用: {node.Name}");
                }
            }
            _hiddenNodes.Clear();

            DialoguePanel?.HidePanel();

            // 恢复 BattleSceneManager 管理的 UI
            _battleSceneManager?.ShowAllUI();

            IsPlaying = false;
            GD.Print($"[Cutscene] === PlayCutscene 结束: {sequence.SequenceId} ===");
            EmitSignal(SignalName.CutsceneFinished, sequence.SequenceId);
        }

        /// <summary>手动请求跳过当前过场（例如由 UI 按钮调用）。</summary>
        public void RequestSkip() => IsSkipRequested = true;

        // ── 摄像机接管 ────────────────────────────────────────────────────
        private void BeginCameraOverride()
        {
            if (Camera == null) return;

            // 禁用 CameraFollow 脚本的 _Process / _PhysicsProcess
            Camera.SetProcess(false);
            Camera.SetPhysicsProcess(false);

            // 解除与玩家节点的父级变换绑定，使摄像机可自由移动至任意世界坐标
            _cameraWasTopLevel = Camera.TopLevel;
            var gpos = Camera.GlobalPosition;
            Camera.TopLevel        = true;
            Camera.GlobalPosition  = gpos;

            // 锁定 CameraZoneManager，防止玩家 ProcessMode.Disabled 导致区域误退出
            _cameraZoneManager ??= GetTree().GetFirstNodeInGroup("camera_zone_manager") as CameraZoneManager;
            _cameraZoneManager?.LockZone();
        }

        private void EndCameraOverride()
        {
            if (Camera == null) return;

            Camera.TopLevel = _cameraWasTopLevel;
            Camera.SetProcess(true);
            Camera.SetPhysicsProcess(true);
            // CameraFollow 恢复后会通过 position_smoothing 平滑归位

            // 解锁 CameraZoneManager
            _cameraZoneManager?.UnlockZone();
        }
    }
}

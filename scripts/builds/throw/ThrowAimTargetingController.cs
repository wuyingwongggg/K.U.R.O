using Godot;
using Kuros.Actors.Heroes;
using Kuros.Builds.BuildCore;
using Kuros.Core;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 坐标寻址瞄准模式控制器（BuildThrow_A_010）：
    /// 短按核心技能(未被 A_009 消费) → 进入瞄准模式 —— 全局时间减缓 + 全屏半透明黑幕 +
    /// 瞄准点(设备无关,与生成点同源 AimPointResolver)处悬浮生成物幽灵预览(外观与实体件一致);
    /// 按攻击键 = 在该点确认生成(消耗充能);取消 = 右键 / ESC / 再按核心技能键。
    /// 期间攻击与拾取/放置输入被屏蔽(SuppressAttackInput / SuppressInteractInput)——左键征用为"确认"、右键征用为"取消";
    /// ESC 经 BattleMenu 判定优先取消本模式(IsAnyActive)而非打开菜单。
    /// 常驻组件:由卡效果创建(存在即启用),OnRemoved 时释放;时间/输入/视觉在退出时统一还原。
    /// </summary>
    [GlobalClass]
    public partial class ThrowAimTargetingController : Node
    {
        public const string NodeName = "ThrowAimTargetingController";

        public static ThrowAimTargetingController GetOrCreate(GameActor player)
        {
            var existing = player.GetNodeOrNull<ThrowAimTargetingController>(NodeName);
            if (existing != null) return existing;

            var node = new ThrowAimTargetingController { Name = NodeName };
            player.AddChild(node);
            return node;
        }

        public static ThrowAimTargetingController? Find(GameActor player)
            => player.GetNodeOrNull<ThrowAimTargetingController>(NodeName);

        [ExportCategory("Slow Motion")]
        /// <summary>瞄准期间全局时间流速。</summary>
        [Export(PropertyHint.Range, "0.05,1,0.05")] public float SlowTimeScale { get; set; } = 0.1f;

        [ExportCategory("Overlay")]
        /// <summary>全屏遮罩颜色(半透明黑)。</summary>
        [Export] public Color DimColor { get; set; } = new(0f, 0f, 0f, 0.55f);
        /// <summary>遮罩 CanvasLayer 层级(HUD 之上、CRT 滤镜之下)。</summary>
        [Export] public int OverlayLayer { get; set; } = 500;

        /// <summary>瞄准模式进行中。</summary>
        public bool IsActive { get; private set; }

        private static ThrowAimTargetingController? _activeInstance;
        /// <summary>是否有瞄准模式进行中(BattleMenu 的"ESC 优先取消瞄准"判定用)。</summary>
        public static bool IsAnyActive
            => _activeInstance != null && GodotObject.IsInstanceValid(_activeInstance);

        private SamplePlayer? _player;
        private ThrowCoreEffect? _core;
        private AimPointResolver? _resolver;
        private CanvasLayer? _overlay;
        private Node2D? _ghostHolder;
        private float _savedTimeScale = 1f;
        private bool _skipInputThisFrame;      // 进入帧不读确认/取消(触发短按的键缘可能同帧仍在,防误确认/误取消)
        private bool _releaseAttackToRestore;  // 退出时攻击键(左键)仍未松开 → 保持屏蔽,松开再解除
        private bool _releaseInteractToRestore;// 退出时右键仍未松开 → 保持屏蔽(防取消点击的松开缘触发拾取)

        public override void _Ready()
        {
            _player = GetParent() as SamplePlayer;
            _core = _player?.EffectController?.GetEffect<ThrowCoreEffect>();
            _resolver = _player != null ? AimPointResolver.Find(_player) : null;
        }

        /// <summary>进入瞄准模式(核心短按入口回调)。返回 false = 未进入(无充能/无生成物/组件未就绪)。</summary>
        public bool TryEnter()
        {
            if (IsActive) return false;
            _player ??= GetParent() as SamplePlayer;
            _core ??= _player?.EffectController?.GetEffect<ThrowCoreEffect>();
            _resolver ??= _player != null ? AimPointResolver.Find(_player) : null;
            if (_player == null || !IsInstanceValid(_player)) return false;
            if (_core == null || !IsInstanceValid(_core) || !_core.CanSpawn) return false;

            IsActive = true;
            _skipInputThisFrame = true;
            _activeInstance = this;
            _core.AimModeActive = true;
            _player.SuppressAttackInput = true;
            _player.SuppressInteractInput = true;

            _savedTimeScale = (float)Engine.TimeScale;
            Engine.TimeScale = Mathf.Clamp(SlowTimeScale, 0.05f, 1f);

            BuildOverlay();
            return true;
        }

        /// <summary>事件输入:右键 / ESC 取消(独立于 _Process 轮询)。
        /// ESC 同时被 BattleMenu 监听——BattleMenu 以 IsAnyActive 判定优先让本模式处理,不会开菜单;
        /// 右键的拾取/放置语义由查询级屏蔽(SuppressInteractInput)阻断,不会触发交互。</summary>
        public override void _Input(InputEvent @event)
        {
            if (!IsActive) return;

            bool cancel = @event switch
            {
                InputEventMouseButton mb => mb.Pressed && mb.ButtonIndex == MouseButton.Right,
                InputEventKey key => key.Pressed && !key.Echo && key.Keycode == Key.Escape,
                _ => false,
            };
            if (!cancel) return;

            Exit();
            GetViewport()?.SetInputAsHandled();
        }

        public override void _Process(double delta)
        {
            // 非激活期:确认/取消点击残留的按住松开后解除屏蔽,防按住连击/松开缘拾取在退出后立即触发
            if (!IsActive)
            {
                if (_releaseAttackToRestore && !Input.IsActionPressed("attack"))
                {
                    _releaseAttackToRestore = false;
                    if (_player != null && IsInstanceValid(_player))
                        _player.SuppressAttackInput = false;
                }
                if (_releaseInteractToRestore && !Input.IsMouseButtonPressed(MouseButton.Right))
                {
                    _releaseInteractToRestore = false;
                    if (_player != null && IsInstanceValid(_player))
                        _player.SuppressInteractInput = false;
                }
                return;
            }

            // 玩家失效/死亡(换场景、死亡流程) → 兜底取消
            if (_player == null || !IsInstanceValid(_player) || _player.IsDeadOrDying
                || _core == null || !IsInstanceValid(_core))
            {
                Exit();
                return;
            }

            UpdateGhostPosition();

            // 进入帧:触发短按的键缘可能仍在(同帧未清),跳过确认/取消读取
            if (_skipInputThisFrame)
            {
                _skipInputThisFrame = false;
                return;
            }

            // 确认:攻击键(攻击输入已屏蔽,此处直读)
            if (Input.IsActionJustPressed("attack"))
            {
                Confirm();
                return;
            }

            // 取消:再次短按核心技能键(短按在松开帧触发)
            if (_player.WasActionShortPressed(InputActions.CoreSkill))
            {
                Exit();
            }
        }

        public override void _ExitTree()
        {
            Exit(); // 换场景/死亡清理兜底还原时间与输入
            // 组件释放:悬而未决的"松开再解除屏蔽"随组件终结,强制解除(防攻击/交互输入被永久屏蔽)
            _releaseAttackToRestore = false;
            _releaseInteractToRestore = false;
            _activeInstance = null;
            if (_player != null && IsInstanceValid(_player))
            {
                _player.SuppressAttackInput = false;
                _player.SuppressInteractInput = false;
            }
            base._ExitTree();
        }

        // ── 内部 ─────────────────────────────────────────────────────

        private void Confirm()
        {
            var core = _core;
            Exit();                   // 先还原时间/输入/视觉
            core?.TrySpawnOnce();     // 生成点由 SpawnAtAimPointRange 走 AimPointResolver,与幽灵同源
        }

        /// <summary>退出瞄准模式:还原时间流速、攻击屏蔽、幽灵与遮罩。幂等。</summary>
        private void Exit()
        {
            if (!IsActive) return;
            IsActive = false;
            if (ReferenceEquals(_activeInstance, this))
                _activeInstance = null;

            if (_core != null && IsInstanceValid(_core))
                _core.AimModeActive = false;

            if (_player != null && IsInstanceValid(_player))
            {
                // 确认/取消点击仍按住 → 保持屏蔽到松开(防左键残按连击 / 右键松开缘触发拾取);
                // 玩家死亡/失效时无需等待,直接解除
                if (Input.IsActionPressed("attack") && !_player.IsDeadOrDying)
                    _releaseAttackToRestore = true;
                else
                    _player.SuppressAttackInput = false;

                if (Input.IsMouseButtonPressed(MouseButton.Right) && !_player.IsDeadOrDying)
                    _releaseInteractToRestore = true;
                else
                    _player.SuppressInteractInput = false;
            }

            // 只在自己仍持有减缓时还原(期间若他人(如受伤眩晕减缓)改写时间,交给其自行恢复)
            float mine = Mathf.Clamp(SlowTimeScale, 0.05f, 1f);
            if (Mathf.Abs((float)Engine.TimeScale - mine) < 0.001f)
                Engine.TimeScale = _savedTimeScale;

            _ghostHolder = null;
            if (_overlay != null && IsInstanceValid(_overlay))
                _overlay.QueueFree();
            _overlay = null;
        }

        private void BuildOverlay()
        {
            var tree = GetTree();
            if (tree == null) return;

            _overlay = new CanvasLayer { Name = "ThrowAimOverlay", Layer = OverlayLayer };
            tree.Root.AddChild(_overlay);

            // 半透明黑幕(拦截 GUI 点击,防止误触 HUD;Input 轮询不受 GUI 消费影响)
            var dim = new ColorRect
            {
                Name = "Dim",
                Color = DimColor,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _overlay.AddChild(dim);

            // 生成物幽灵(半透明;黑幕之后添加 → 绘制在其上)
            _ghostHolder = new Node2D { Name = "Ghost" };
            var ghost = BuildGhostVisual();
            if (ghost != null)
                _ghostHolder.AddChild(ghost);
            _overlay.AddChild(_ghostHolder);
        }

        /// <summary>构建幽灵视觉:实例化当前将生成的场景(不加入树 → 脚本/物理不运行),
        /// 把其中 Sprite2D 按相对根节点的变换迁入裸 Node2D 容器——无脚本、无碰撞、不入组,纯视觉。
        /// 外观与实体件一致(不做透明度处理:件的主视觉 shader 直接覆写 COLOR,Modulate 对其无效)。
        /// 场景缺失或无 Sprite2D 时回退物品图标。</summary>
        private Node2D? BuildGhostVisual()
        {
            Node2D? holder = null;

            var scene = _core?.ResolveSpawnScene(out _);
            if (scene != null)
            {
                var inst = scene.Instantiate();
                if (inst is Node2D root)
                {
                    holder = new Node2D { Name = "Visual" };
                    CollectSprites(root, root, holder);
                    if (holder.GetChildCount() == 0)
                    {
                        holder.Free();
                        holder = null;
                    }
                }
                inst.Free();
            }

            holder ??= new Node2D { Name = "Visual" };
            if (holder.GetChildCount() == 0 && _core?.FurnitureIcon != null)
                holder.AddChild(new Sprite2D { Texture = _core.FurnitureIcon });

            return holder;
        }

        private static void CollectSprites(Node2D node, Node2D root, Node2D holder)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is Sprite2D sprite)
                {
                    if (sprite.Visible)
                    {
                        holder.AddChild(new Sprite2D
                        {
                            Name = sprite.Name,
                            Texture = sprite.Texture,
                            Material = sprite.Material,
                            Transform = TransformRelativeToRoot(sprite, root),
                            Centered = sprite.Centered,
                            Offset = sprite.Offset,
                            FlipH = sprite.FlipH,
                            FlipV = sprite.FlipV,
                            RegionEnabled = sprite.RegionEnabled,
                            RegionRect = sprite.RegionRect,
                            Hframes = sprite.Hframes,
                            Vframes = sprite.Vframes,
                            Frame = sprite.Frame,
                            Modulate = sprite.Modulate,
                            SelfModulate = sprite.SelfModulate,
                            ZIndex = sprite.ZIndex,
                        });
                    }
                }

                // Sprite2D 本身也可能挂子节点(如 Outline 挂着方块面/线框精灵)——必须继续下钻
                if (child is Node2D childNode)
                {
                    CollectSprites(childNode, root, holder);
                }
            }
        }

        private static Transform2D TransformRelativeToRoot(Node2D node, Node2D root)
        {
            var t = node.Transform;
            var parent = node.GetParent() as Node2D;
            while (parent != null && parent != root)
            {
                t = parent.Transform * t;
                parent = parent.GetParent() as Node2D;
            }
            return t;
        }

        /// <summary>幽灵定位:瞄准世界点 → 屏幕坐标(与 HUD 跟随同公式),缩放跟随相机。</summary>
        private void UpdateGhostPosition()
        {
            if (_ghostHolder == null || !IsInstanceValid(_ghostHolder)) return;
            if (_resolver == null || !IsInstanceValid(_resolver)) return;

            var viewport = GetViewport();
            var camera = viewport?.GetCamera2D();
            if (viewport == null || camera == null) return;
            if (!_resolver.TryGetAimWorldPoint(out var aimWorld)) return;

            Vector2 screen = viewport.GetVisibleRect().Size * 0.5f
                + (aimWorld - camera.GetScreenCenterPosition()) * camera.Zoom;
            _ghostHolder.Position = screen;
            _ghostHolder.Scale = camera.Zoom;
        }
    }
}

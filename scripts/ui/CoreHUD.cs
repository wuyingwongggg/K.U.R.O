using System.Collections.Generic;
using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Systems;

namespace Kuros.UI
{
    public partial class CoreHUD : Control
    {
        [Export] public Control? MachinePanel { get; set; }
        [Export] public Control? WaiterPanel { get; set; }
        [Export] public Control? ThrowPanel { get; set; }

        [ExportGroup("Throw Charges")]
        /// <summary>充能单元（单图标 + 数字计数 + 扇形遮罩;计数型充能显示）。</summary>
        [Export] public Control? ThrowChargeUnit { get; set; }
        /// <summary>充能图标（显示生成的家具;素材即物品定义 Icon）。</summary>
        [Export] public TextureRect? ThrowIconRect { get; set; }
        /// <summary>充能数字（当前可用发数,如 3）。</summary>
        [Export] public Label? ThrowCountLabel { get; set; }
        /// <summary>底槽素材（可替换;null = 仅图标+遮罩无底图）。</summary>
        [Export] public TextureRect? ThrowSlotBaseRect { get; set; }
        /// <summary>遮罩素材（可替换;null = 程序绘制半透明扇形兜底）。</summary>
        [Export] public Texture2D? ThrowOverlayTexture { get; set; }

        [ExportGroup("Machine Heat Bar")]
        [Export] public TextureProgressBar? HeatBar { get; set; }
        [Export] public TextureProgressBar? HeatFillBar { get; set; }
        [Export] public Label? HeatValueLabel { get; set; }

        [ExportGroup("Overflow Pulse")]
        /// <summary>热量爆表时整条 bar 的脉动+抖动表现开关。</summary>
        [Export] public bool OverflowPulseEnabled { get; set; } = true;
        [Export] public Color OverflowPulseColorA { get; set; } = new(1f, 0.35f, 0.15f);   // 红
        [Export] public Color OverflowPulseColorB { get; set; } = new(1f, 0.8f, 0.25f);    // 亮橙
        [Export(PropertyHint.Range, "0,50,0.5")] public float OverflowJitterStrength { get; set; } = 2f;

        [ExportGroup("Follow Player")]
        /// <summary>核心面板跟随玩家显示（世界坐标 → 屏幕坐标），关闭则按场景锚点定位（默认左下角）。</summary>
        [Export] public bool FollowPlayer { get; set; } = true;
        /// <summary>面板相对玩家位置的屏幕偏移（如 (0, -90) 显示在玩家头顶上方）。</summary>
        [Export] public Vector2 PlayerAnchorOffset { get; set; } = new Vector2(0, -90);
        /// <summary>跟随平滑速度（越大跟得越紧，越小越滞后；指数平滑，值越大越灵敏）。</summary>
        [Export(PropertyHint.Range, "1,30,0.5")] public float SmoothSpeed { get; set; } = 10f;

        private readonly Dictionary<string, Control?> _panelMap = new();
        private MachineCoreEffect? _boundMachineCore;
        private ThrowCoreEffect? _boundThrowCore;
        private Control? _activePanel; // 当前显示的跟随面板（跟随泛化:Machine/Throw/Waiter 同规则）
        private float _baseFillScaleX = 1f;
        private bool _anchorsReset;
        private Tween? _overflowPulseTween;
        private bool _overflowActive;
        private Vector2 _barBasePosition;

        // Throw 充能运行态（计数型:单图标 + 数字 + 单扇）
        private ThrowCooldownOverlay? _chargeOverlay;
        private Texture2D? _chargeIcon;

        public override void _Ready()
        {
            _panelMap[BuildClassConstants.Machine] = MachinePanel;
            _panelMap[BuildClassConstants.Waiter] = WaiterPanel;
            _panelMap[BuildClassConstants.Throw] = ThrowPanel;

            // 当前场景结构：HeatFillBar 是 MachinePanel 的直接子节点（单个 TextureProgressBar 条）
            HeatBar ??= MachinePanel?.GetNodeOrNull<TextureProgressBar>("HeatFillBar");
            HeatFillBar ??= HeatBar;
            HeatValueLabel ??= MachinePanel?.GetNodeOrNull<Label>("HeatValueLabel");

            if (HeatFillBar != null)
                _baseFillScaleX = HeatFillBar.Scale.X;
        }

        public void BindMachineCore(MachineCoreEffect? effect)
        {
            _boundMachineCore = effect;
        }

        public void BindThrowCore(ThrowCoreEffect? effect)
        {
            _boundThrowCore = effect;
            _chargeIcon = null; // 图标/数字在下一帧按新核心刷新
        }

        public override void _Process(double delta)
        {
            // 跟随玩家：将可见核心面板定位到玩家附近（世界坐标 → 屏幕坐标）
            if (FollowPlayer)
                UpdateFollowPlayer((float)delta);

            // Throw 面板:充能计数驱动（独立于 Machine 热量逻辑）
            if (_boundThrowCore != null && IsInstanceValid(_boundThrowCore)
                && ThrowPanel != null && ThrowPanel.Visible)
            {
                UpdateThrowCharge();
            }

            if (HeatBar == null || HeatFillBar == null)
                return;
            if (_boundMachineCore == null || !IsInstanceValid(_boundMachineCore))
                return;
            if (MachinePanel == null || !MachinePanel.Visible)
            {
                // 面板隐藏时熄灭脉动
                if (_overflowActive) UpdateOverflowPulse(false);
                return;
            }

            float maxHeat = _boundMachineCore.MaxHeat;
            float heat = _boundMachineCore.Heat;
            HeatBar.MaxValue = maxHeat;
            HeatBar.Value = heat;   // TextureProgressBar 内部钳制：爆表时条停在满格
            HeatFillBar.MaxValue = maxHeat;
            HeatFillBar.Value = heat;

            // 【已停用】爆表：条 Scale.X 跟随溢出量实时放大（1 → 1 + overflow/MaxHeat，最大 1.5 倍）
            // 暂时注释，后续改为燃烧特效表现（见 HeatBurnEffect 方案）
            // float overflow = Mathf.Max(heat - maxHeat, 0f);
            // float factor = 1f + (maxHeat > 0f ? overflow / maxHeat : 0f);
            // var fillScale = HeatFillBar.Scale;
            // fillScale.X = _baseFillScaleX * factor;
            // HeatFillBar.Scale = fillScale;

            if (HeatValueLabel != null)
                HeatValueLabel.Text = $"{(int)heat}/{(int)maxHeat}";

            // 爆表表现：红↔亮橙脉动 + 随机抖动
            if (OverflowPulseEnabled)
            {
                UpdateOverflowPulse(heat > maxHeat);
                if (_overflowActive)
                {
                    float s = OverflowJitterStrength;
                    HeatFillBar.Position = _barBasePosition
                        + new Vector2((GD.Randf() - 0.5f) * s, (GD.Randf() - 0.5f) * s * 0.75f);
                }
            }
        }

        /// <summary>爆表状态变化：进入时启动循环脉动 Tween，退出时恢复原色与原位置。</summary>
        private void UpdateOverflowPulse(bool overflow)
        {
            if (overflow == _overflowActive) return;

            _overflowActive = overflow;
            if (overflow)
            {
                _barBasePosition = HeatFillBar!.Position;
                _overflowPulseTween?.Kill();
                _overflowPulseTween = CreateTween();
                _overflowPulseTween.SetLoops();
                _overflowPulseTween.TweenProperty(HeatFillBar, "modulate", OverflowPulseColorA, 0.25f);
                _overflowPulseTween.TweenProperty(HeatFillBar, "modulate", OverflowPulseColorB, 0.25f);
            }
            else
            {
                _overflowPulseTween?.Kill();
                _overflowPulseTween = null;
                HeatFillBar!.Modulate = Colors.White;
                HeatFillBar.Position = _barBasePosition;
            }
        }

        /// <summary>
        /// 把当前可见的核心面板定位到玩家位置附近（指数平滑跟随）。
        /// 玩家世界坐标经相机转屏幕坐标 + 偏移；首次调用时把面板锚点重置为 TopLeft
        /// （手动定位模式，避免场景左下角锚点干扰）并直接定位（不插值，防止从原点飞入）。
        /// </summary>
        private void UpdateFollowPlayer(float delta)
        {
            if (_activePanel == null) return;

            var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            var camera = GetViewport().GetCamera2D();
            if (player == null || camera == null) return;

            // 世界坐标 → 屏幕坐标：视口中心 + (玩家位置 - 相机中心) × 缩放
            Vector2 targetPos = GetViewport().GetVisibleRect().Size * 0.5f
                + (player.GlobalPosition - camera.GetScreenCenterPosition()) * camera.Zoom
                + PlayerAnchorOffset;

            if (!_anchorsReset)
            {
                _anchorsReset = true;
                _activePanel.AnchorLeft = 0;
                _activePanel.AnchorTop = 0;
                _activePanel.AnchorRight = 0;
                _activePanel.AnchorBottom = 0;
                _activePanel.Position = targetPos; // 首次直接定位，避免从原点插值飞入
                return;
            }

            // 指数平滑：SmoothSpeed 越大跟得越紧；帧率无关（1 - exp(-speed × delta)）
            float t = 1f - Mathf.Exp(-SmoothSpeed * delta);
            _activePanel.Position = _activePanel.Position.Lerp(targetPos, t);
        }

        /// <summary>
        /// Throw 充能计数驱动（计数型:单个图标 + 数字 = 当前可用发数;恢复期间单扇遮罩渐消,
        /// 表示"下一发还需多久"。满充能时无遮罩,数字 = 上限。
        /// </summary>
        private void UpdateThrowCharge()
        {
            var core = _boundThrowCore;
            if (core == null || !IsInstanceValid(core)) return;

            if (ThrowIconRect != null)
            {
                Texture2D? icon = core.FurnitureIcon;
                if (!ReferenceEquals(_chargeIcon, icon))
                {
                    _chargeIcon = icon;
                    ThrowIconRect.Texture = icon;
                }
            }

            int ready = core.ReadyCharges;
            int max = core.EffectiveMaxCharges;
            bool full = ready >= max;

            if (ThrowCountLabel != null)
            {
                ThrowCountLabel.Text = ready.ToString();
                ThrowCountLabel.Modulate = ready > 0 ? Colors.White : new Color(1f, 1f, 1f, 0.4f);
            }

            if (ThrowIconRect != null)
                ThrowIconRect.Modulate = ready > 0 ? Colors.White : new Color(1f, 1f, 1f, 0.5f);

            var overlay = GetOrCreateChargeOverlay();
            if (overlay != null)
            {
                // 显式同步尺寸/位置(不依赖布局系统,防首帧零面积导致不渲染)
                if (ThrowChargeUnit != null)
                {
                    overlay.Position = Vector2.Zero;
                    if (ThrowChargeUnit.Size.X > 1f && ThrowChargeUnit.Size.Y > 1f)
                        overlay.Size = ThrowChargeUnit.Size;
                }
                overlay.Progress = full ? 0f : 1f - core.ChargingProgress;
            }
        }

        private ThrowCooldownOverlay? GetOrCreateChargeOverlay()
        {
            if (_chargeOverlay != null && IsInstanceValid(_chargeOverlay))
                return _chargeOverlay;
            if (ThrowChargeUnit == null) return null;

            var overlay = new ThrowCooldownOverlay { MaskTexture = ThrowOverlayTexture };
            overlay.Position = Vector2.Zero;
            overlay.Size = ThrowChargeUnit.Size;
            overlay.ZIndex = 2; // 保证盖在图标/底图之上
            ThrowChargeUnit.AddChild(overlay);
            _chargeOverlay = overlay;
            return overlay;
        }

        public void ShowFor(string buildClass)
        {
            _boundMachineCore = null;
            _boundThrowCore = null;
            HideAll();
            _activePanel = null;
            if (_panelMap.TryGetValue(buildClass, out var panel) && panel != null)
            {
                panel.Visible = true;
                _activePanel = panel;
            }
        }

        public void HideAll()
        {
            foreach (var panel in _panelMap.Values)
            {
                if (panel != null) panel.Visible = false;
            }
        }
    }
}

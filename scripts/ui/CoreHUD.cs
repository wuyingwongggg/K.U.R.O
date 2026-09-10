using System.Collections.Generic;
using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Items;
using Kuros.Systems;

namespace Kuros.UI
{
    public partial class CoreHUD : Control
    {
        [Export] public Control? MachinePanel { get; set; }
        [Export] public Control? WaiterPanel { get; set; }
        [Export] public Control? ThrowPanel { get; set; }

        [ExportGroup("Throw Charges")]
        /// <summary>充能图标（显示生成的家具;素材即物品定义 Icon）。</summary>
        [Export] public TextureRect? ThrowIconRect { get; set; }
        /// <summary>等级标签（持有家具时显示 LV1/2/3 = 有效投掷等级;未持有时隐藏;充能计数已由底槽环分段表达）。</summary>
        [Export] public Label? ThrowCountLabel { get; set; }
        /// <summary>底槽环（挂 circular_bar.gdshader;代码驱动 segment_count=充能上限、removed_segments=就绪填充比例）。</summary>
        [Export] public TextureRect? ThrowSlotBaseRect { get; set; }
        /// <summary>默认件图标 - 小型（方块.png;未升级时;null → 回退核心件图标）。</summary>
        [Export] public Texture2D? ThrowIconSmall { get; set; }
        /// <summary>默认件图标 - 中型（方块+.png;A_006 层 1 生成升级时）。</summary>
        [Export] public Texture2D? ThrowIconMedium { get; set; }
        /// <summary>默认件图标 - 大型（方块++.png;A_006 层 2 生成升级时）。</summary>
        [Export] public Texture2D? ThrowIconLarge { get; set; }

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

        // Throw 充能运行态（图标缓存 + 底槽环材质实例）
        private Texture2D? _chargeIcon;
        private ShaderMaterial? _slotBarMaterial;
        private int _slotSegmentCount = -1;

        /// <summary>玩家引用(取"当前 holding 道具"的图标;缺失时回退核心默认图标)。SamplePlayer 在全局命名空间。</summary>
        private global::SamplePlayer? _player;

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
            // 玩家引用(holding 图标来源;玩家换场景时自动刷新)
            if (_player == null || !GodotObject.IsInstanceValid(_player))
                _player = GetTree().GetFirstNodeInGroup("player") as global::SamplePlayer;

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
        /// Throw 充能驱动（图标 + 数字 + 底槽环）。
        /// 底槽环挂 circular_bar.gdshader:segment_count = 充能上限（每格一段）,
        /// removed_segments = 绿色弧段占比 = (就绪充能 + 当前恢复格进度) / 上限。
        /// 满充能 = 满环;用尽后从空环随 CD 逐格填回（该 shader 参数值即显示弧长比例）。
        /// </summary>
        private void UpdateThrowCharge()
        {
            var core = _boundThrowCore;
            if (core == null || !IsInstanceValid(core)) return;

            int ready = core.ReadyCharges;
            int max = core.EffectiveMaxCharges;
            bool full = ready >= max;

            var held = _player?.LeftHandItem;
            Texture2D? heldIcon = held != null && held.IsFurniture ? held.Icon : null;

            if (ThrowIconRect != null)
            {
                // 仅家具(IsFurniture = 一次性投掷物)参与:举着家具时显示该件 Icon;
                // 空手/举投掷武器/其它武器 → 默认件图标(A_006 生成升级时随档切换)。
                Texture2D? icon = heldIcon ?? ResolveDefaultThrowIcon(core);
                if (!ReferenceEquals(_chargeIcon, icon))
                {
                    _chargeIcon = icon;
                    ThrowIconRect.Texture = icon;
                }

                // 充能耗尽的半透明只作用于默认件图标:持有家具的图标保持全亮
                ThrowIconRect.Modulate = heldIcon != null || ready > 0 ? Colors.White : new Color(1f, 1f, 1f, 0.5f);
            }

            // 等级徽章 = 持有家具的有效投掷等级(充能计数改由底槽环分段表达);
            // 未持有家具(或家具无档) → 徽章底图与数字一并隐藏
            int tierValue = 0;
            if (held != null && held.IsFurniture)
                tierValue = (int)held.EffectiveTier(_player?.GetThrowableModifiers() ?? ThrowableModifiers.None, 0);

            if (ThrowCountLabel != null)
            {
                ThrowCountLabel.Visible = tierValue > 0;
                if (tierValue > 0)
                    ThrowCountLabel.Text = $"LV{tierValue}";
            }

            // 底槽环:段数 = 充能上限(每格一段);显示弧长占比 = (就绪充能 + 恢复进度) / 上限
            var mat = GetOrCreateSlotBarMaterial();
            if (mat == null) return;

            if (_slotSegmentCount != max)
            {
                _slotSegmentCount = max;
                mat.SetShaderParameter("segment_count", max);
            }

            float progress = full ? 0f : Mathf.Clamp(core.ChargingProgress, 0f, 1f);
            mat.SetShaderParameter("removed_segments", Mathf.Clamp((ready + progress) / max, 0f, 1f));
        }

        /// <summary>
        /// 默认件图标（空手时显示）:A_006 生成升级按档切换 —— 1 = 小型(默认方块),
        /// 2 = 中型(方块+),3 = 大型(方块++);未配素材或未升级 → 核心件图标。
        /// </summary>
        private Texture2D? ResolveDefaultThrowIcon(ThrowCoreEffect core)
        {
            Texture2D? tierIcon = core.SpawnTierOverride switch
            {
                2 => ThrowIconMedium,
                3 => ThrowIconLarge,
                _ => ThrowIconSmall,
            };
            return tierIcon ?? core.FurnitureIcon;
        }

        /// <summary>底槽环材质（取 SlotBase 实例材质的独立副本:参数驱动不污染 .tscn 共享资源）。</summary>
        private ShaderMaterial? GetOrCreateSlotBarMaterial()
        {
            if (_slotBarMaterial != null && IsInstanceValid(_slotBarMaterial))
                return _slotBarMaterial;
            if (ThrowSlotBaseRect?.Material is not ShaderMaterial shared)
                return null;

            _slotBarMaterial = (ShaderMaterial)shared.Duplicate();
            ThrowSlotBaseRect.Material = _slotBarMaterial;
            _slotSegmentCount = -1; // 强制下一帧重写分段数
            return _slotBarMaterial;
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

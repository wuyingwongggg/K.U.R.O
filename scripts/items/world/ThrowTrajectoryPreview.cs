using System.Collections.Generic;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Items;

namespace Kuros.Items.World
{
    /// <summary>
    /// 投掷武器抛物线预览组件。
    /// 作为 Node2D 添加到玩家场景中，在 IdleHolding / RunHolding 状态下
    /// 实时绘制预测落点、抛物线轨迹点及落点指示圆。
    ///
    /// 颜色、粒子数量等均可在编辑器 Inspector 中调整。
    /// 武器物理参数从对应武器场景（ItemDefinition.WorldScenePath）自动读取并缓存。
    /// </summary>
    [Tool]
    public partial class ThrowTrajectoryPreview : Node2D
    {
        // ─── 显示开关 ──────────────────────────────────────────────────────────────
        [ExportGroup("Display")]
        /// <summary>是否启用预览（可在运行中实时切换）</summary>
        [Export] public bool EnablePreview { get; set; } = true;

        // ─── 轨迹线样式 ───────────────────────────────────────────────────────────
        [ExportGroup("Trajectory Style")]
        /// <summary>水平距离倍率（调整轨迹落点远近）</summary>
        [Export(PropertyHint.Range, "0.5,5,0.1")] public float HorizontalDistanceMultiplier { get; set; } = 2.2f;
        /// <summary>轨迹点颜色</summary>
        [Export] public Color TrailColor { get; set; } = new Color(1f, 0.85f, 0.2f, 0.8f);
        /// <summary>轨迹点半径（像素）</summary>
        [Export(PropertyHint.Range, "1,20,0.5")] public float DotRadius { get; set; } = 4f;
        /// <summary>相邻两点之间跳过的 phase 步数（越大越稀疏）</summary>
        [Export(PropertyHint.Range, "1,8,1")] public int DotStep { get; set; } = 2;
        /// <summary>总采样点数（越大曲线越精细）</summary>
        [Export(PropertyHint.Range, "8,64,1")] public int TotalSamples { get; set; } = 32;

        // ─── 落点指示 ─────────────────────────────────────────────────────────────
        [ExportGroup("Landing Indicator")]
        [Export] public PackedScene? LandingIndicatorScene { get; set; }

        // ─── 武器参数覆盖（留空则从武器场景自动读取）─────────────────────────────
        [ExportGroup("Weapon Param Override (optional)")]
        /// <summary>若不为 0 则覆盖场景内的 ThrowParabolicPeakHeight</summary>
        [Export(PropertyHint.Range, "0,500,10")] public float OverridePeakHeight { get; set; } = 0f;
        /// <summary>若不为 0 则覆盖场景内的 ThrowParabolicLandingYOffset</summary>
        [Export(PropertyHint.Range, "0,500,10")] public float OverrideLandingYOffset { get; set; } = 0f;
        /// <summary>若不为 0 则覆盖场景内的 ThrowParabolicDuration</summary>
        [Export(PropertyHint.Range, "0,5,0.1")] public float OverrideDuration { get; set; } = 0f;
        /// <summary>若不为 0 则覆盖 PlayerItemInteractionComponent.ThrowImpulse</summary>
        [Export(PropertyHint.Range, "0,3000,10")] public float OverrideThrowImpulse { get; set; } = 0f;
        /// <summary>若不为零向量则覆盖 ThrowStartOffset</summary>
        [Export] public Vector2 OverrideThrowStartOffset { get; set; } = Vector2.Zero;

        // ─── 内部状态 ─────────────────────────────────────────────────────────────
        private SamplePlayer? _player;
        private PlayerItemInteractionComponent? _interaction;

        // 武器参数缓存：基于 ItemDefinition 引用
        private ItemDefinition? _cachedItem = null;
        private WeaponThrowParams _cachedParams = new();

        // 当前帧是否应渲染
        private bool _shouldDraw = false;
        // 当前帧的预计算落点（局部坐标）
        private Vector2 _landingLocalPos = Vector2.Zero;
        // 当前帧的采样点列表（局部坐标）
        private readonly List<Vector2> _trailPoints = new();
		private Node2D? _landingIndicatorInstance;

        private struct WeaponThrowParams
        {
            public float PeakHeight;
            public float LandingYOffset;
            public double Duration;
            public float HorizontalDistance;  // 水平飞行距离（像素），而不是速度倍数
            public Vector2 ThrowStartOffset;
            public Vector2 ThrowOffset;   // from PlayerItemInteractionComponent
            public float ThrowImpulse;    // from PlayerItemInteractionComponent
        }

        public override void _Ready()
        {
            base._Ready();
            //ZIndex = 10; // 绘制在普通精灵上方
            // 作为玩家子节点，保持局部坐标在玩家本地空间（Position = Zero）
            _player = GetParent() as SamplePlayer
                   ?? GetOwner() as SamplePlayer
                   ?? GetTree()?.GetFirstNodeInGroup("player") as SamplePlayer;

            if (_player != null)
                _interaction = _player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
        }

        public override void _ExitTree()
        {
            _landingIndicatorInstance?.QueueFree();
            _landingIndicatorInstance = null;
            base._ExitTree();
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (!EnablePreview)
            {
                if (_shouldDraw) { _shouldDraw = false; QueueRedraw(); }
                return;
            UpdateLandingIndicator(false);
            }

            bool wantsDraw = CheckShouldDraw();
            if (wantsDraw)
                ComputeTrajectory();
            else
                _trailPoints.Clear();

            if (wantsDraw != _shouldDraw)
            {
                _shouldDraw = wantsDraw;
                QueueRedraw();
            }
            else if (wantsDraw)
            {
                QueueRedraw();
            }

            UpdateLandingIndicator(wantsDraw);
        }

        public override void _Draw()
        {
            base._Draw();
            if (!_shouldDraw || _trailPoints.Count == 0) return;

            // 绘制轨迹点（每 DotStep 个点画一个圆）
            for (int i = 0; i < _trailPoints.Count; i += DotStep)
            {
                float alpha = Mathf.Lerp(0.3f, 1f, (float)i / Mathf.Max(1, _trailPoints.Count - 1));
                DrawCircle(_trailPoints[i], DotRadius, TrailColor with { A = TrailColor.A * alpha });
            }
        }

        // ─── 辅助方法 ─────────────────────────────────────────────────────────────


        private void UpdateLandingIndicator(bool show)
        {
            if (LandingIndicatorScene == null)
            {
                if (_landingIndicatorInstance != null)
                {
                    _landingIndicatorInstance.QueueFree();
                    _landingIndicatorInstance = null;
                }
                return;
            }

            if (!show || _trailPoints.Count == 0)
            {
                if (_landingIndicatorInstance != null)
                {
                    _landingIndicatorInstance.QueueFree();
                    _landingIndicatorInstance = null;
                }
                return;
            }

            if (_landingIndicatorInstance == null)
            {
                _landingIndicatorInstance = LandingIndicatorScene.Instantiate<Node2D>();
                AddChild(_landingIndicatorInstance);
            }

            _landingIndicatorInstance.Position = _landingLocalPos;
        }
        private bool CheckShouldDraw()
        {
            if (_player == null) return false;

            var state = _player.StateMachine?.CurrentState?.Name;
            if (state != "IdleHolding" && state != "RunHolding") return false;

            var stack = _player.InventoryComponent?.GetSelectedQuickBarStack();
            if (stack == null || stack.IsEmpty || !stack.Item.IsThrowable) return false;

            // 确保武器参数已缓存
            EnsureParamsCached(stack.Item);
            if (_cachedParams.HorizontalDistance <= 0f || _cachedParams.Duration <= 0)
                return false;
            return true;
        }

        private void EnsureParamsCached(ItemDefinition item)
        {
            // 如果已缓存当前选中的武器，直接返回（使用引用相等判断）
            if (ReferenceEquals(_cachedItem, item) && _cachedItem != null)
            {
                return;
            }

            _cachedItem = item;

            _cachedParams = new WeaponThrowParams
            {
                PeakHeight         = item.ThrowParabolicPeakHeight,
                LandingYOffset     = item.ThrowParabolicLandingYOffset,
                Duration           = item.ThrowParabolicDuration,
                HorizontalDistance = item.ThrowHorizontalDistance,
                ThrowStartOffset   = item.ThrowStartOffset,
                ThrowOffset        = _interaction?.ThrowOffset ?? new Vector2(48, -10),
                ThrowImpulse       = _interaction?.ThrowImpulse ?? 800f,
            };

            // 如果使用全覆盖参数，则不需要加载场景
            bool fullOverride = OverridePeakHeight > 0f
                             && OverrideLandingYOffset > 0f
                             && OverrideDuration > 0f
                             && OverrideThrowImpulse > 0f;

            if (fullOverride)
            {
                _cachedParams.PeakHeight         = OverridePeakHeight;
                _cachedParams.LandingYOffset     = OverrideLandingYOffset;
                _cachedParams.Duration           = OverrideDuration;
                _cachedParams.HorizontalDistance = 600f;  // 默认距离
                _cachedParams.ThrowStartOffset   = OverrideThrowStartOffset != Vector2.Zero ? OverrideThrowStartOffset : item.ThrowStartOffset;
                _cachedParams.ThrowOffset        = _interaction?.ThrowOffset ?? new Vector2(48, -10);
                _cachedParams.ThrowImpulse       = OverrideThrowImpulse;
                return;
            }

            // 叠加覆盖值（最终优先级最高）
            if (OverridePeakHeight > 0f)       _cachedParams.PeakHeight      = OverridePeakHeight;
            if (OverrideLandingYOffset > 0f)   _cachedParams.LandingYOffset   = OverrideLandingYOffset;
            if (OverrideDuration > 0f)         _cachedParams.Duration         = OverrideDuration;
            if (OverrideThrowStartOffset != Vector2.Zero) _cachedParams.ThrowStartOffset = OverrideThrowStartOffset;

            _cachedParams.ThrowOffset  = _interaction != null ? _interaction.ThrowOffset : _cachedParams.ThrowOffset;
            _cachedParams.ThrowImpulse = OverrideThrowImpulse > 0f ? OverrideThrowImpulse
                                       : (_interaction?.ThrowImpulse ?? _cachedParams.ThrowImpulse);
        }

        private void ComputeTrajectory()
        {
            _trailPoints.Clear();
            if (_player == null) return;

            var p = _cachedParams;
            float facingX = _player.FacingRight ? 1f : -1f;


            float scaleComp = 1f / Mathf.Max(_player.Scale.X, 0.001f);

            float startLocalX = (facingX * p.ThrowOffset.X + p.ThrowStartOffset.X) * scaleComp;
            float startLocalY = (p.ThrowOffset.Y + p.ThrowStartOffset.Y) * scaleComp;
            float landingY = startLocalY + p.LandingYOffset * scaleComp;

            float totalDX = p.HorizontalDistance * facingX * scaleComp * HorizontalDistanceMultiplier;
            float peakH = p.PeakHeight * scaleComp;
            float duration = (float)p.Duration;

            for (int i = 0; i <= TotalSamples; i++)
            {
                float phase = (float)i / TotalSamples;

                float x = startLocalX + totalDX * phase;

                float y = Mathf.Lerp(startLocalY, landingY, phase)
                        - Mathf.Sin(phase * Mathf.Pi) * peakH;

                _trailPoints.Add(new Vector2(x, y));
            }

            _landingLocalPos = new Vector2(startLocalX + totalDX, landingY);
        }
    }
}

using System.Collections.Generic;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
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

        // ─── 飞行阻挡预测 ─────────────────────────────────────────────────────────
        [ExportGroup("Block Prediction")]
        /// <summary>预测飞行途中阻挡(墙 AirWall / 敌人)并截断落点;投掷即效果武器与 StopOnHit=false 道具自动跳过。</summary>
        [Export] public bool BlockScanEnabled { get; set; } = true;
        /// <summary>阻挡重扫最小间隔(秒;敌人会移动故需周期重扫,蓄力期亦受此节流)。</summary>
        [Export(PropertyHint.Range, "0.03,0.5,0.01")] public float BlockScanInterval { get; set; } = 0.1f;

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

        // 武器参数缓存：基于 ItemDefinition 引用 + 构筑修饰（引用或修饰变化均失效）
        private ItemDefinition? _cachedItem = null;
        private ThrowableModifiers _cachedMods = ThrowableModifiers.None;
        private WeaponThrowParams _cachedParams = new();

        // 当前帧是否应渲染
        private bool _shouldDraw = false;
        // 当前帧的预计算落点（局部坐标）
        private Vector2 _landingLocalPos = Vector2.Zero;
        // 当前帧的采样点列表（局部坐标）
        private readonly List<Vector2> _trailPoints = new();
        // 分裂预览:各枚落点(局部坐标,主落点在[0])
        private readonly List<Vector2> _landingPoints = new();
        // 落地指示器实例池:与 _landingPoints 等量(分裂时每个落点各一个)
        private readonly List<Node2D> _landingIndicators = new();

        // 飞行阻挡预测运行态:阻挡相位(1 = 无阻挡;轨迹/指示器按此截断)
        private float _blockPhase = 1f;
        private ulong _lastBlockScanMs;
        private ItemDefinition? _blockScanItem; // 道具切换 → 立即重扫(不受间隔节流)

        /// <summary>世界场景静态探查结果(实例化不入树读取,_Ready 不跑):StopOnHit 开关与判定盒形状。</summary>
        private sealed class BlockProfile
        {
            public bool StopOnHit = true;
            public Shape2D? Shape;
            public Vector2 ShapeOffset;
        }
        private static readonly Dictionary<string, BlockProfile?> BlockProfiles = new();

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
            foreach (var indicator in _landingIndicators)
                indicator?.QueueFree();
            _landingIndicators.Clear();
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
            {
                _trailPoints.Clear();
                _landingPoints.Clear();
            }

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
            // 需要数量 = 显示中的落点数(分裂 N 枚 → N 个指示器;无场景则 0)
            int need = show && LandingIndicatorScene != null ? _landingPoints.Count : 0;

            // 补建缺失实例
            while (_landingIndicators.Count < need)
            {
                var instance = LandingIndicatorScene!.Instantiate<Node2D>();
                AddChild(instance);
                _landingIndicators.Add(instance);
            }

            // 裁剪多余实例
            while (_landingIndicators.Count > need)
            {
                var last = _landingIndicators[^1];
                _landingIndicators.RemoveAt(_landingIndicators.Count - 1);
                last?.QueueFree();
            }

            for (int i = 0; i < need && i < _landingPoints.Count; i++)
                _landingIndicators[i].Position = _landingPoints[i];
        }
        private bool CheckShouldDraw()
        {
            if (_player == null) return false;

            var state = _player.StateMachine?.CurrentState?.Name;
            bool holdingState = state == "IdleHolding" || state == "RunHolding";
            // B_006 投掷预载:蓄力窗口内(Throw 状态)也显示——修饰每帧变化触发缓存失效,轨迹随蓄力实时增长
            bool charging = !holdingState && state == "Throw"
                && _player.EffectController?.GetEffectByInterface<IThrowChargeModifier>()?.Charging == true;
            if (!holdingState && !charging) return false;

            var stack = _player.InventoryComponent?.GetSelectedQuickBarStack();
            if (stack == null || stack.IsEmpty || !stack.Item.IsThrowable) return false;

            // 投掷即效果武器(回旋镖等)本体不飞行,轨迹由生成的特效表现——仅当定义提供了显式飞行距离
            // (ThrowHorizontalDistance>0,与生成侧注入同一真源)时预览该距离;否则不显示
            if (stack.Item.SpawnEffectOnThrow && stack.Item.ThrowHorizontalDistance <= 0f) return false;

            // 确保武器参数已缓存
            EnsureParamsCached(stack.Item);
            if (_cachedParams.HorizontalDistance <= 0f || _cachedParams.Duration <= 0)
                return false;
            return true;
        }

        private void EnsureParamsCached(ItemDefinition item)
        {
            // 修饰取自构筑提供方(无 → None)；缓存键 = (item, mods)，任一变即失效重算
            ThrowableModifiers mods = _player is IThrowableModifierProvider provider
                ? provider.GetThrowableModifiers()
                : ThrowableModifiers.None;

            if (ReferenceEquals(_cachedItem, item) && _cachedItem != null && _cachedMods == mods)
            {
                return;
            }

            _cachedItem = item;
            _cachedMods = mods;

            _cachedParams = new WeaponThrowParams
            {
                PeakHeight         = item.ThrowParabolicPeakHeight,
                LandingYOffset     = item.ThrowParabolicLandingYOffset,
                Duration           = item.GetEffectiveThrowDuration(mods),
                HorizontalDistance = item.GetEffectiveThrowDistance(mods),
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
            _landingPoints.Clear();
            if (_player == null) return;

            var p = _cachedParams;
            float facingX = _player.FacingRight ? 1f : -1f;


            float scaleComp = 1f / Mathf.Max(_player.Scale.X, 0.001f);

            float startLocalX = (facingX * p.ThrowOffset.X + p.ThrowStartOffset.X) * scaleComp;
            float startLocalY = (p.ThrowOffset.Y + p.ThrowStartOffset.Y) * scaleComp;
            float baseLandingY = startLocalY + p.LandingYOffset * scaleComp;

            // 投掷即效果武器(回旋镖):特效飞行精确停在 effective distance,预览乘数取 1.0 使落点=真实到达距离;
            // 普通投掷保持场景调校乘数(2.1)
            float distanceMultiplier = _cachedItem?.SpawnEffectOnThrow == true ? 1f : HorizontalDistanceMultiplier;
            float totalDX = p.HorizontalDistance * facingX * scaleComp * distanceMultiplier;
            float peakH = p.PeakHeight * scaleComp;
            float duration = (float)p.Duration;

            // 分裂预览:主轨迹(原件落点偏移)+ 各克隆落点偏移;无分裂卡 → 仅主轨迹(偏移 0)
            var offsets = CollectSplitOffsets();

            // 飞行阻挡预测:扫描"绘制路径"本身(与画出的轨迹点同一坐标)——截断点即玩家屏幕上
            // 看到的接触位置;若按真实路径扫描,预览乘数(2.1)会让截断落点与画面严重错位
            float worldSpeed = duration > 0.01f ? (float)(p.HorizontalDistance / duration) : 0f;
            UpdateBlockPhase(_cachedItem, totalDX, worldSpeed, startLocalX, startLocalY,
                baseLandingY + offsets[0] * scaleComp, peakH, duration, distanceMultiplier);
            int maxSample = Mathf.RoundToInt(Mathf.Clamp(_blockPhase, 0f, 1f) * TotalSamples);

            foreach (float offset in offsets)
            {
                float landingY = baseLandingY + offset * scaleComp;
                for (int i = 0; i <= maxSample; i++)
                {
                    float phase = (float)i / TotalSamples;

                    float x = startLocalX + totalDX * phase;

                    float y = Mathf.Lerp(startLocalY, landingY, phase)
                            - Mathf.Sin(phase * Mathf.Pi) * peakH;

                    _trailPoints.Add(new Vector2(x, y));
                }

                // 阻挡:指示器 X = 屏上接触位(绘制路径的截断相位),Y 保持在该枚的落点行
                // (与无障碍时同一行,即玩家 Y/判定行——实体停下后回弹/落定也回判定行,不会悬在弧线高度)
                if (_blockPhase < 1f)
                {
                    float bp = Mathf.Clamp(_blockPhase, 0f, 1f);
                    _landingPoints.Add(new Vector2(startLocalX + totalDX * bp, landingY));
                }
                else
                {
                    _landingPoints.Add(new Vector2(startLocalX + totalDX, landingY));
                }
            }

            _landingLocalPos = _landingPoints.Count > 0 ? _landingPoints[0] : Vector2.Zero;
        }

        // ═══════════════════ 飞行阻挡预测(镜像实体两套碰撞) ═══════════════════

        /// <summary>阻挡预测节流入口:道具切换立即重扫;同道具按 BlockScanInterval 周期重扫(敌人会移动)。
        /// 投掷即效果武器、StopOnHit=false 道具、编辑器环境一律不扫描(相位恒 1)。</summary>
        private void UpdateBlockPhase(ItemDefinition? item, float totalDX, float worldSpeed,
            float startLocalX, float startLocalY, float mainLandingY,
            float peakH, float duration, float distanceMultiplier)
        {
            if (!BlockScanEnabled || item == null || Engine.IsEditorHint() || item.SpawnEffectOnThrow)
            {
                _blockPhase = 1f;
                return;
            }

            ulong now = Time.GetTicksMsec();
            bool itemChanged = !ReferenceEquals(_blockScanItem, item);
            if (!itemChanged && now - _lastBlockScanMs < (ulong)Mathf.Max(BlockScanInterval * 1000f, 30f))
                return; // 节流内:复用上次相位(蓄力期 mods 每帧变也不逐帧重扫)

            _blockScanItem = item;
            _lastBlockScanMs = now;

            var profile = ResolveBlockProfile(item);
            if (profile == null || !profile.StopOnHit || profile.Shape == null)
            {
                _blockPhase = 1f;
                return;
            }

            _blockPhase = ScanBlockPhase(profile, item, totalDX, worldSpeed,
                startLocalX, startLocalY, mainLandingY, peakH, duration, distanceMultiplier);
        }

        /// <summary>沿"绘制路径"(与画出的轨迹点同坐标)扫描最早阻挡,返回阻挡相位(0-1;无阻挡 = 1)。
        /// 镜像实体侧:<br/>
        /// ① 墙 = CheckWallHit 同款前视射线(|v|×0.05+20px,只认名 AirWall,排除 GameActor);<br/>
        /// ② 敌人/障碍 = 判定盒形状查询(mask=1)在判定行(投掷者站位 Y)上——绝不用弧线 y;<br/>
        /// 敌人受 B_008 穿透门控(镜像实体命中停留门控),障碍(IBarrier)恒阻挡(方向性屏障无法预览,近似)。</summary>
        private float ScanBlockPhase(BlockProfile profile, ItemDefinition item, float totalDX, float worldSpeed,
            float startLocalX, float startLocalY, float mainLandingY,
            float peakH, float duration, float distanceMultiplier)
        {
            var space = GetWorld2D()?.DirectSpaceState;
            if (space == null || _player == null || profile.Shape == null) return 1f;

            float travelSign = totalDX >= 0f ? 1f : -1f;
            float lookAhead = worldSpeed * 0.05f + 20f; // 复刻 CheckWallHit 的提前量
            float rowWorldY = _player.GlobalPosition.Y;  // 判定行:投掷者站位(实体 origin = LastDroppedBy.GlobalPosition)

            // 步长 ≈ 实机每帧位移(|v|/60)换算到绘制路径(×预览乘数);细采样防漏(判定盒可能仅 10px 宽)
            int steps = Mathf.Clamp(
                Mathf.CeilToInt(duration * 60f * Mathf.Max(distanceMultiplier, 1f)), 2, 96);

            for (int i = 1; i <= steps; i++)
            {
                float phase = (float)i / steps;
                float lx = startLocalX + totalDX * phase;
                float ly = Mathf.Lerp(startLocalY, mainLandingY, phase) - Mathf.Sin(phase * Mathf.Pi) * peakH;
                Vector2 worldPoint = ToGlobal(new Vector2(lx, ly));

                // ① 墙:前视射线(视觉弧位),镜像 CheckWallHit 过滤
                var ray = new PhysicsRayQueryParameters2D
                {
                    From = worldPoint,
                    To = worldPoint + new Vector2(travelSign * lookAhead, 0f),
                    CollideWithBodies = true,
                    CollideWithAreas = false
                };
                var rayHit = space.IntersectRay(ray);
                if (rayHit.Count > 0 && rayHit.TryGetValue("collider", out var wallVariant)
                    && wallVariant.As<GodotObject>() is not GameActor)
                {
                    if (wallVariant.As<GodotObject>() is Node wallNode
                        && string.Equals(wallNode.Name.ToString(), "AirWall", System.StringComparison.OrdinalIgnoreCase))
                        return phase;
                }

                // ② 敌人/障碍:判定盒形状查询(层1;判定行 Y + 形状局部偏移)
                var query = new PhysicsShapeQueryParameters2D
                {
                    Shape = profile.Shape,
                    Transform = new Transform2D(0f, new Vector2(worldPoint.X, rowWorldY) + profile.ShapeOffset),
                    CollisionMask = 1u,
                    CollideWithAreas = true,
                    CollideWithBodies = true
                };
                foreach (var result in space.IntersectShape(query))
                {
                    if (!result.TryGetValue("collider", out var collider)) continue;
                    if (collider.As<GodotObject>() is not Node hitNode) continue;

                    // 敌人:本体直判,或 area 名为 HitArea(忽略大小写,镜像实机 AreaEntered 判据)+ 父链解析
                    // (敌人攻击盒 MoveAttackArea 同在层1——不可仅按父链含 GameActor 判定,必须认名字)
                    bool isEnemy = hitNode is GameActor;
                    if (!isEnemy && hitNode is Area2D area
                        && string.Equals(area.Name.ToString(), "HitArea", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var actor = ResolveActorFrom(hitNode);
                        isEnemy = actor != null && !ReferenceEquals(actor, _player);
                    }
                    if (isEnemy)
                    {
                        if (EnemyBlocks(item)) return phase;
                        continue;
                    }
                    // 障碍(IBarrier,同实体 ResolveBarrier 判据):恒阻挡
                    if (ResolveBarrierFrom(hitNode) != null) return phase;
                    // 其余忽略(敌人攻击盒等非 HitArea 的层1物体不阻挡)
                }
            }
            return 1f;
        }

        /// <summary>敌人是否阻挡本次投掷(镜像实体命中停留门控:StopOnHit && !(PassThroughEnemies && !IsThrowWeapon))。</summary>
        private bool EnemyBlocks(ItemDefinition item)
            => !_cachedMods.PassThroughEnemies || item.IsThrowWeapon;

        private static GameActor? ResolveActorFrom(Node node)
        {
            Node? current = node;
            while (current != null)
            {
                if (current is GameActor actor) return actor;
                current = current.GetParent();
            }
            return null;
        }

        private static Node? ResolveBarrierFrom(Node node)
        {
            Node? current = node;
            while (current != null)
            {
                if (current is IBarrier) return current;
                current = current.GetParent();
            }
            return null;
        }

        /// <summary>静态探查(按场景路径缓存):实例化不入树读取根节点 StopOnHit 与判定盒形状。
        /// 实例不入树 → _Ready 不跑;Free() 后 Shape 资源引用仍有效(Resource 引用计数)。</summary>
        private static BlockProfile? ResolveBlockProfile(ItemDefinition item)
        {
            var scene = WorldItemSpawner.ResolveScene(item);
            if (scene == null) return null;
            string key = scene.ResourcePath;
            if (BlockProfiles.TryGetValue(key, out var cached)) return cached;

            BlockProfile? profile = null;
            var root = scene.Instantiate();
            try
            {
                profile = new BlockProfile();

                var stopVariant = root.Get("StopOnHit");
                if (stopVariant.VariantType != Variant.Type.Nil)
                    profile.StopOnHit = stopVariant.AsBool();

                // 判定盒路径:根节点 HitboxAreaPath(默认 RigidBody2D/AttackArea)下的 CollisionShape2D
                string hitboxPath = "RigidBody2D/AttackArea";
                var pathVariant = root.Get("HitboxAreaPath");
                if (pathVariant.VariantType == Variant.Type.NodePath && !pathVariant.AsNodePath().IsEmpty)
                    hitboxPath = pathVariant.AsNodePath();

                var shapeNode = root.GetNodeOrNull<CollisionShape2D>(hitboxPath + "/CollisionShape2D")
                    ?? root.FindChild("AttackArea", recursive: true, owned: false)?
                        .GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                if (shapeNode?.Shape != null)
                {
                    profile.Shape = shapeNode.Shape;
                    profile.ShapeOffset = shapeNode.Position;
                }
            }
            catch
            {
                profile = null; // 探查失败 → 不扫描(安全降级)
            }
            finally
            {
                root.Free(); // 未入树节点:直接 Free(不经 QueueFree)
            }

            BlockProfiles[key] = profile;
            return profile;
        }

        /// <summary>本帧应绘制的轨迹落点偏移列表:主轨迹在前;玩家持有分裂卡且手持件可分裂时追加克隆偏移。</summary>
        private List<float> CollectSplitOffsets()
        {
            var offsets = new List<float> { 0f };
            var provider = _player?.EffectController?.GetEffectByInterface<IThrowSplitPreview>();
            if (provider != null && provider.IsSplittableForHeldItem())
            {
                float center = provider.CenterLandingOffsetY;
                var clones = provider.CloneLandingOffsetsY;
                if (center != 0f || (clones != null && clones.Length > 0))
                {
                    offsets.Clear();
                    offsets.Add(center);
                    if (clones != null) offsets.AddRange(clones);
                }
            }
            return offsets;
        }
    }
}

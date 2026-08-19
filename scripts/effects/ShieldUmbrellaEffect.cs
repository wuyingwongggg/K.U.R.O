using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 雨伞护盾（ActorEffect）：武器攻击（无论是否命中）时生成，跟随玩家前方移动并随朝向镜像反转。
    /// 视觉为 FireWallA 同款 4 层结构（每片 Core/BuildMask/GlowWall/ScanFX，复用 meeting_barrier_* shader）：
    /// - 生成：build_progress 扫描显现（同 MeetingBarrier.PlaySpawnAnimation）
    /// - 常态：core 主体 + glow fresnel 呼吸 + scan 扫描线/闪烁（shader 内部 TIME 自驱动）
    /// - 受到攻击：damage_level 拉到 1（core 变色 + scan 故障腐蚀），随后消失
    /// - 消失：core/glow/scan alpha 淡出后整体隐藏
    /// 正面受到伤害时代替玩家承受（DamageIntercepted 拦截，同 DirectionalBlockEffect），
    /// 抵挡一次后消失，随后进入 CooldownSeconds 冷却，冷却结束后的下一次攻击重新生成。
    /// </summary>
    [GlobalClass]
    public partial class ShieldUmbrellaEffect : ActorEffect
    {
        private enum UmbrellaState
        {
            Ready,        // 冷却结束待生成（隐藏）
            Spawning,     // 生成中（build 扫描动画，未武装）
            Idle,         // 常态（跟随 + 格挡武装）
            Hit,          // 受击（damage_level=1 腐蚀表现）
            Despawning,   // 消失（淡出）
            Cooldown,     // 冷却中（隐藏）
        }

        [ExportCategory("Follow")]
        /// <summary>跟随玩家的前方偏移（X 按朝向取符号，Y 固定）。</summary>
        [Export] public Vector2 FrontOffset { get; set; } = new Vector2(90f, -110f);
        /// <summary>生成瞬间的位置偏移（比 FrontOffset 更远）：生成后平滑飞向跟随位置。</summary>
        [Export] public Vector2 SpawnOffset { get; set; } = new Vector2(162f, -198f);
        /// <summary>跟随平滑度（指数收敛速率，同 P2 FollowSmoothing）：越大越快贴向目标点。</summary>
        [Export(PropertyHint.Range, "0.1,30,0.1")] public float FollowSmoothing { get; set; } = 8.5f;
        /// <summary>上下浮动幅度（像素，同 P2 FloatAmplitude）：0 关闭浮动。</summary>
        [Export(PropertyHint.Range, "0,60,1")] public float HoverAmplitude { get; set; } = 12f;
        /// <summary>上下浮动频率（次/秒，同 P2 FloatFrequency）。</summary>
        [Export(PropertyHint.Range, "0,5,0.1")] public float HoverFrequency { get; set; } = 1.2f;

        [ExportCategory("Block")]
        /// <summary>正面格挡角度（度）：攻击方向与朝向夹角小于一半角度时判定为正面。</summary>
        [Export(PropertyHint.Range, "30,360,5")] public float BlockArcDegrees { get; set; } = 150f;
        /// <summary>护盾破碎后的冷却时间（秒）：冷却期间攻击不会重新生成。</summary>
        [Export(PropertyHint.Range, "0,60,0.5")] public float CooldownSeconds { get; set; } = 3f;

        [ExportCategory("Visual")]
        /// <summary>生成动画时长（秒）：build_progress 0 → 1 扫描显现。</summary>
        [Export(PropertyHint.Range, "0.05,2,0.05")] public float BuildDuration { get; set; } = 0.35f;
        /// <summary>受击表现时长（秒）：damage_level=1 持续后进入消失。</summary>
        [Export(PropertyHint.Range, "0.05,1,0.05")] public float HitFlashDuration { get; set; } = 0.3f;
        /// <summary>消失淡出时长（秒）：core/glow/scan alpha 1 → 0。</summary>
        [Export(PropertyHint.Range, "0.05,2,0.05")] public float DespawnDuration { get; set; } = 0.5f;

        [ExportCategory("Nodes")]
        [Export] public NodePath UmbrellaTopPath { get; set; } = new("UmbrellaTop");
        [Export] public NodePath UmbrellaButtonPath { get; set; } = new("UmbrellaButton");

        /// <summary>单片的 4 层（同 FireWallA：Core/BuildMask/GlowWall/ScanFX）节点与每实例独立材质。</summary>
        private sealed class LayerSet
        {
            public Node2D? Root;
            public Sprite2D? Core;
            public Sprite2D? Build;
            public Sprite2D? Glow;
            public Sprite2D? Scan;
            public ShaderMaterial? CoreMat;
            public ShaderMaterial? BuildMat;
            public ShaderMaterial? GlowMat;
            public ShaderMaterial? ScanMat;
        }

        private readonly LayerSet _top = new();
        private readonly LayerSet _button = new();
        private AnimationPlayer? _animPlayer;
        private bool _openDone;

        private UmbrellaState _state = UmbrellaState.Ready;
        private float _stateElapsed;
        private float _cooldownRemaining;
        private float _baseScaleX = 1f;
        private float _hoverClock;

        protected override void OnApply()
        {
            base.OnApply();

            if (Actor != null)
            {
                Actor.DamageIntercepted += OnDamageIntercepted;

                // 脱离到玩家父级成为独立场景物体（同 BunnySwardFloatingCannon），用全局坐标平滑移动
                var parent = Actor.GetParent();
                if (parent != null && GetParent() != parent)
                {
                    Reparent(parent);
                }
            }

            ResolveVisuals();
            _baseScaleX = Mathf.Abs(GetScale().X);
            StartSpawn();
        }

        public override void OnRemoved()
        {
            if (Actor != null)
            {
                Actor.DamageIntercepted -= OnDamageIntercepted;
            }
            if (_animPlayer != null)
            {
                _animPlayer.AnimationFinished -= OnAnimationFinished;
            }
            base.OnRemoved();
        }

        /// <summary>攻击刷新：仅在生命周期结束（冷却完毕回到 Ready）后的攻击才重新生成并播放 open 动画；
        /// 特效持续存在期间（生成中/常态/受击/消失/冷却中）的连续攻击不重播生成动画。</summary>
        protected override void OnStackRefreshed()
        {
            if (_state == UmbrellaState.Ready)
            {
                StartSpawn();
            }
        }

        protected override void OnTick(double delta)
        {
            base.OnTick(delta);

            if (Actor == null || !IsInstanceValid(Actor) || Actor.IsDeadOrDying)
            {
                Controller?.RemoveEffect(this);
                return;
            }

            float dt = (float)delta;

            switch (_state)
            {
                case UmbrellaState.Ready:
                    break;

                case UmbrellaState.Spawning:
                    // 锁：open 动画播放完之前停留在生成位置（只更新朝向），播完才开始飞向跟随位置
                    if (_openDone)
                    {
                        FollowPlayer(dt);
                    }
                    else
                    {
                        UpdateFacing();
                    }
                    _stateElapsed += dt;
                    float t = BuildDuration > 0f
                        ? Mathf.Clamp(_stateElapsed / BuildDuration, 0f, 1f)
                        : 1f;
                    // 同 MeetingBarrier：Cubic 缓出扫描
                    float progress = 1f - Mathf.Pow(1f - t, 3f);
                    SetBuildProgress(progress);
                    if (t >= 1f)
                    {
                        SetBuildProgress(1f);
                        _state = UmbrellaState.Idle;
                    }
                    break;

                case UmbrellaState.Idle:
                    FollowPlayer(dt);
                    break;

                case UmbrellaState.Hit:
                    FollowPlayer(dt);
                    _stateElapsed += dt;
                    if (_stateElapsed >= HitFlashDuration)
                    {
                        _state = UmbrellaState.Despawning;
                        _stateElapsed = 0f;
                    }
                    break;

                case UmbrellaState.Despawning:
                    FollowPlayer(dt);
                    _stateElapsed += dt;
                    // 同 MeetingBarrier 消失：core/glow/scan alpha 淡出（build 层跟随整体隐藏）
                    float alpha = DespawnDuration > 0f
                        ? Mathf.Clamp(1f - _stateElapsed / DespawnDuration, 0f, 1f)
                        : 0f;
                    SetShaderAlpha(alpha);
                    if (alpha <= 0f)
                    {
                        SetLayerVisible(false);
                        _cooldownRemaining = Mathf.Max(0f, CooldownSeconds);
                        _state = UmbrellaState.Cooldown;
                    }
                    break;

                case UmbrellaState.Cooldown:
                    if (CooldownSeconds <= 0f || _cooldownRemaining <= 0f)
                    {
                        _state = UmbrellaState.Ready;
                        break;
                    }
                    _cooldownRemaining -= dt;
                    if (_cooldownRemaining <= 0f)
                    {
                        _state = UmbrellaState.Ready;
                    }
                    break;
            }
        }

        /// <summary>正面伤害拦截（同 DirectionalBlockEffect）：仅在 Idle 武装状态下生效，成功格挡一次后进入受击状态。</summary>
        private bool OnDamageIntercepted(GameActor.DamageEventArgs args)
        {
            if (_state != UmbrellaState.Idle) return false;
            if (args.Target != Actor) return false;
            if (!IsWithinFrontArc(args)) return false;

            args.Damage = 0;
            args.IsBlocked = true;

            _state = UmbrellaState.Hit;
            _stateElapsed = 0f;
            SetDamageLevel(1f);
            return true;
        }

        /// <summary>攻击方向是否在正面格挡角度内（同 DirectionalBlockEffect.IsWithinArc）。</summary>
        private bool IsWithinFrontArc(GameActor.DamageEventArgs args)
        {
            var forward = args.Forward;
            var attackDir = args.AttackDirection;
            if (attackDir == Vector2.Zero)
            {
                // 无攻击来源方向时按正面处理
                attackDir = -forward;
            }

            attackDir = attackDir.Normalized();
            forward = forward.Normalized();

            var toAttacker = -attackDir;
            float dot = Mathf.Clamp(forward.Dot(toAttacker), -1f, 1f);
            float angle = Mathf.RadToDeg(Mathf.Acos(dot));
            return angle <= BlockArcDegrees * 0.5f;
        }

        /// <summary>开始生成：显示全部图层、重置 shader 参数（同 MeetingBarrier.PlaySpawnAnimation）、
        /// 播放 open 动画（autoplay 只在节点首次进树时触发，重新生成需手动重播）、定位到玩家前方。</summary>
        private void StartSpawn()
        {
            if (Actor == null) return;

            SetLayerVisible(true);
            SetShaderAlpha(1f);
            SetBuildProgress(0f);
            SetDamageLevel(0f);
            _state = UmbrellaState.Spawning;
            _stateElapsed = 0f;

            if (_animPlayer != null && _animPlayer.HasAnimation("open"))
            {
                _openDone = false;
                _animPlayer.Play("open");
            }
            else
            {
                _openDone = true;
            }

            Set("global_position", ComputeSpawnPosition());
            UpdateFacing();
        }

        /// <summary>指数平滑跟随玩家前方（同 P2：blend = 1 - exp(-smoothing * dt)）+ 朝向镜像。</summary>
        private void FollowPlayer(float delta)
        {
            _hoverClock += delta;
            var target = ComputeFollowPosition();
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, FollowSmoothing) * delta);
            Set("global_position", GetGlobalPos().Lerp(target, blend));
            UpdateFacing();
        }

        /// <summary>跟随目标点：玩家前方（FrontOffset.X 按朝向取符号）+ P2 同款正弦上下浮动。</summary>
        private Vector2 ComputeFollowPosition()
        {
            float sideSign = Actor!.FacingRight ? 1f : -1f;
            float hover = Mathf.Sin(_hoverClock * Mathf.Tau * HoverFrequency) * HoverAmplitude;
            return Actor.GlobalPosition + new Vector2(FrontOffset.X * sideSign, FrontOffset.Y + hover);
        }

        /// <summary>生成位置：玩家前方更远处（SpawnOffset.X 按朝向取符号），生成后平滑飞向跟随位置。</summary>
        private Vector2 ComputeSpawnPosition()
        {
            float sideSign = Actor!.FacingRight ? 1f : -1f;
            return Actor.GlobalPosition + new Vector2(SpawnOffset.X * sideSign, SpawnOffset.Y);
        }

        /// <summary>朝向镜像：朝右正常，朝左根节点 X 缩放取负（子 Sprite 结构不变）。</summary>
        private void UpdateFacing()
        {
            float sideSign = Actor!.FacingRight ? 1f : -1f;
            var s = GetScale();
            s.X = _baseScaleX * sideSign;
            Set("scale", s);
        }

        private void ResolveVisuals()
        {
            ResolveLayerSet(_top, UmbrellaTopPath);
            ResolveLayerSet(_button, UmbrellaButtonPath);
            _animPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            if (_animPlayer != null)
            {
                _animPlayer.AnimationFinished += OnAnimationFinished;
            }
        }

        private void OnAnimationFinished(StringName animName)
        {
            if (animName == "open")
            {
                _openDone = true;
            }
        }

        /// <summary>解析一组的 4 层节点（同 FireWallA 命名：Core/BuildMask/GlowWall/ScanFX），并每实例独立复制材质。</summary>
        private void ResolveLayerSet(LayerSet set, NodePath path)
        {
            set.Root = GetNodeOrNull<Node2D>(path);
            if (set.Root == null) return;

            set.Core = set.Root.GetNodeOrNull<Sprite2D>("Core");
            set.Build = set.Root.GetNodeOrNull<Sprite2D>("BuildMask");
            set.Glow = set.Root.GetNodeOrNull<Sprite2D>("GlowWall");
            set.Scan = set.Root.GetNodeOrNull<Sprite2D>("ScanFX");

            set.CoreMat = DuplicateSpriteMaterial(set.Core);
            set.BuildMat = DuplicateSpriteMaterial(set.Build);
            set.GlowMat = DuplicateSpriteMaterial(set.Glow);
            set.ScanMat = DuplicateSpriteMaterial(set.Scan);
        }

        /// <summary>每个实例独立复制材质（同 FireWallA/激光笔），防止多实例共享导致动画参数残留。</summary>
        private static ShaderMaterial? DuplicateSpriteMaterial(Sprite2D? sprite)
        {
            if (sprite?.Material is not ShaderMaterial mat) return null;
            var unique = (ShaderMaterial)mat.Duplicate();
            sprite.Material = unique;
            return unique;
        }

        private void SetBuildProgress(float value)
        {
            _top.BuildMat?.SetShaderParameter("build_progress", value);
            _button.BuildMat?.SetShaderParameter("build_progress", value);
        }

        private void SetDamageLevel(float value)
        {
            _top.CoreMat?.SetShaderParameter("damage_level", value);
            _button.CoreMat?.SetShaderParameter("damage_level", value);
            _top.ScanMat?.SetShaderParameter("damage_level", value);
            _button.ScanMat?.SetShaderParameter("damage_level", value);
        }

        private void SetShaderAlpha(float value)
        {
            _top.CoreMat?.SetShaderParameter("alpha", value);
            _button.CoreMat?.SetShaderParameter("alpha", value);
            _top.GlowMat?.SetShaderParameter("alpha", value);
            _button.GlowMat?.SetShaderParameter("alpha", value);
            _top.ScanMat?.SetShaderParameter("alpha", value);
            _button.ScanMat?.SetShaderParameter("alpha", value);
        }

        private void SetLayerVisible(bool visible)
        {
            if (_top.Root != null) _top.Root.Visible = visible;
            if (_button.Root != null) _button.Root.Visible = visible;
        }

        private Vector2 GetGlobalPos()
        {
            return Get("global_position").AsVector2();
        }

        private Vector2 GetScale()
        {
            return Get("scale").AsVector2();
        }
    }
}

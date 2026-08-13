using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 激光笔光束（ActorEffect）：技能释放（attack）时在玩家前方生成天蓝色激光。
    /// 生成逻辑与 LaserBeamA 一致：RayCast2D 检测命中点决定光束长度（撞墙/障碍物即止），
    /// 生长动画（GrowDuration 内 0 → 命中长度）展开，结束时 shader fade 淡出。
    /// 挂在玩家身上跟随移动，每帧按玩家朝向（FacingRight）镜像反转——玩家转身光束转到另一侧。
    /// </summary>
    [GlobalClass]
    public partial class LaserPointerBeam : ActorEffect
    {
        [ExportCategory("Nodes")]
        /// <summary>命中检测射线节点路径（RayCast2D，决定光束长度）。</summary>
        [Export] public NodePath RayCastPath { get; set; } = new("RayCast2D");
        /// <summary>光晕层节点路径（Sprite2D，宽 GlowWidth）。</summary>
        [Export] public NodePath GlowSpritePath { get; set; } = new("GlowSprite");
        /// <summary>核心光束层节点路径（Sprite2D，宽 BeamWidth）。</summary>
        [Export] public NodePath BeamSpritePath { get; set; } = new("BeamSprite");
        /// <summary>发光点节点路径（Sprite2D，独立生命周期）。</summary>
        [Export] public NodePath SpotlightPath { get; set; } = new("Spotlight");

        [ExportCategory("Delay")]
        /// <summary>延迟射出时长（秒）：施加后整体隐藏（光束+光点），到点才显示并开始生长/淡入。
        /// 延迟是纯前置时间，不占用 Duration（光束显示时长）与 SpotlightDuration（光点时长）。</summary>
        [Export(PropertyHint.Range, "0,2,0.05")] public float DelaySeconds { get; set; } = 0f;

        [ExportCategory("Beam")]
        /// <summary>光束最大长度（像素，无遮挡时的长度）。</summary>
        [Export(PropertyHint.Range, "100,3000,10")] public float BeamLength { get; set; } = 1200f;
        /// <summary>核心光束宽度（像素）。</summary>
        [Export(PropertyHint.Range, "1,200,1")] public float BeamWidth { get; set; } = 20f;
        /// <summary>光晕宽度（像素）。</summary>
        [Export(PropertyHint.Range, "1,200,1")] public float GlowWidth { get; set; } = 60f;
        /// <summary>光束相对玩家原点的偏移（X 按朝向取符号，Y 固定）。</summary>
        [Export] public Vector2 BeamOffset { get; set; } = new Vector2(80f, -30f);
        /// <summary>生长动画时长（秒）：从 0 生长到命中长度。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float GrowDuration { get; set; } = 0.12f;
        /// <summary>光束淡出时长（秒）：到期前开始 shader fade。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float FadeDuration { get; set; } = 0.25f;
        /// <summary>攻击结束收尾时长（秒）：玩家退出攻击状态后光束与光点同步快速淡出销毁，不残留跟随朝向反转。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float EndingFadeDuration { get; set; } = 0.18f;

        [ExportCategory("Spotlight")]
        /// <summary>发光点独立存活时长（秒）：光束结束后光点继续存在并自毁（不受 Duration 限制）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float SpotlightDuration { get; set; } = 1.6f;
        /// <summary>发光点淡入时长（秒）：生成时从透明渐显。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeIn { get; set; } = 0.15f;
        /// <summary>发光点淡出时长（秒）：分离后到点前渐隐再销毁。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeOut { get; set; } = 0.3f;

        private RayCast2D? _ray;          // 命中检测射线（沿朝向，决定光束长度）
        private Sprite2D? _glowSprite;    // 光晕层（宽 GlowWidth，高亮）
        private Sprite2D? _beamSprite;    // 核心光束层（宽 BeamWidth）
        private Sprite2D? _spotlight;     // 发光点（淡出时同步衰减）
        private ShaderMaterial? _glowMat; // 光晕材质副本（fade 独立控制）
        private ShaderMaterial? _beamMat; // 光束材质副本
        private float _lifeElapsed;       // 已存活时长
        private float _texW, _texH;       // 光束纹理尺寸（缩放换算用）
        private float _currentLength;     // 当前光束长度（生长动画插值）
        private bool _emitted;            // 是否已射出（延迟结束后置位，首次显示特效）
        private float _configDuration;    // 配置的光束显示时长（不含延迟；基类 Duration 延迟期间被临时放大）
        private bool _ending;             // 攻击状态结束收尾中（锁定朝向 + 快速淡出）
        private float _endingElapsed;     // 收尾已进行时长

        /// <summary>施加时：重挂到角色自身（Node2D，有变换）保证位置继承玩家；
        /// 否则留在 Node 类型的 EffectController 下会丢失玩家位置，生成在场景原点附近。</summary>
        protected override void OnApply()
        {
            base.OnApply();
            if (Actor != null && GetParent() != Actor)
                Reparent(Actor);

            // 节点引用全部走导出路径（可重命名场景节点，无需改脚本）
            _ray = RayCastPath != null && !RayCastPath.IsEmpty ? GetNodeOrNull<RayCast2D>(RayCastPath) : null;
            _glowSprite = GlowSpritePath != null && !GlowSpritePath.IsEmpty ? GetNodeOrNull<Sprite2D>(GlowSpritePath) : null;
            _beamSprite = BeamSpritePath != null && !BeamSpritePath.IsEmpty ? GetNodeOrNull<Sprite2D>(BeamSpritePath) : null;
            _spotlight = SpotlightPath != null && !SpotlightPath.IsEmpty ? GetNodeOrNull<Sprite2D>(SpotlightPath) : null;
            if (_ray == null || _beamSprite == null)
            {
                QueueFree();
                return;
            }

            // 每个实例独立复制材质，防止多实例共享导致 fade 残留（同 LaserBeamA）
            if (_glowSprite?.Material is ShaderMaterial gm)
            {
                _glowMat = (ShaderMaterial)gm.Duplicate();
                _glowMat.SetShaderParameter("fade", 1.0f);
                _glowSprite.Material = _glowMat;
            }
            if (_beamSprite?.Material is ShaderMaterial bm)
            {
                _beamMat = (ShaderMaterial)bm.Duplicate();
                _beamMat.SetShaderParameter("fade", 1.0f);
                _beamSprite.Material = _beamMat;
            }

            _texW = _beamSprite.Texture?.GetWidth() ?? 2f;
            _texH = _beamSprite.Texture?.GetHeight() ?? 2f;
            if (_texW <= 0f) _texW = 2f;
            if (_texH <= 0f) _texH = 2f;

            // 射线沿朝向（局部 +X），长度 = BeamLength
            _ray.TargetPosition = new Vector2(BeamLength, 0f);
            _ray.Enabled = true;

            _lifeElapsed = 0f;
            _emitted = false;

            // 光点淡入：初始透明，射出后按 SpotlightFadeIn 渐显
            if (_spotlight != null)
            {
                var sc = _spotlight.Modulate;
                _spotlight.Modulate = new Color(sc.R, sc.G, sc.B, 0f);
            }

            // 延迟射出：DelaySeconds 内整体隐藏，到点才显示（ActorEffect 基类是 Node，用属性访问）。
            // 基类 Duration 从施加起计时且无法重置——延迟期间临时放大到不触发到期，
            // 射出瞬间恢复为 config + delay（到期时刻 = 射出 + config，即 Duration 语义 = 显示时长，不含延迟）。
            _configDuration = Duration;
            Duration = float.MaxValue;
            Set("visible", DelaySeconds <= 0f);
            if (DelaySeconds <= 0f)
            {
                Duration = _configDuration;
                _emitted = true;
            }

            UpdateFacing();
            UpdateBeam();
        }

        /// <summary>每帧：延迟控制 + 淡出控制 + 跟随朝向 + 光束长度更新（射线检测 + 生长动画）。
        /// 所有动画（生长/淡入）基于"射出后时间"（_lifeElapsed - DelaySeconds），延迟期间整体隐藏。</summary>
        protected override void OnTick(double delta)
        {
            base.OnTick(delta);
            _lifeElapsed += (float)delta;

            // 攻击状态感知收尾：玩家离开 Attack 状态（攻击结束/打断/dash/后摇移动）→
            // 锁定朝向快速淡出，防止残留激光跟随 FacingRight 反转
            if (!_ending && ShouldEndWithAttack())
            {
                _ending = true;
                _endingElapsed = 0f;
                if (!_emitted)
                {
                    // 延迟期被打断：无可见内容，直接移除（跳过光点分离）
                    if (_spotlight != null) { _spotlight.QueueFree(); _spotlight = null; }
                    Controller?.RemoveEffect(this);
                    return;
                }
            }

            if (_ending)
            {
                _endingElapsed += (float)delta;
                float t = Mathf.Clamp(1f - _endingElapsed / Mathf.Max(EndingFadeDuration, 0.01f), 0f, 1f);
                _glowMat?.SetShaderParameter("fade", t);
                _beamMat?.SetShaderParameter("fade", t);
                if (_spotlight != null)
                {
                    var sc = _spotlight.Modulate;
                    _spotlight.Modulate = new Color(sc.R, sc.G, sc.B, t);
                }
                if (_endingElapsed >= EndingFadeDuration)
                {
                    // 收尾结束：光点一并销毁，不分离残留
                    if (_spotlight != null) { _spotlight.QueueFree(); _spotlight = null; }
                    Controller?.RemoveEffect(this);
                }
                return;
            }

            // 延迟阶段：整体隐藏，只跟随朝向（光束出现时朝向已正确），到点首次显示
            if (_lifeElapsed < DelaySeconds)
            {
                UpdateFacing();
                return;
            }
            if (!_emitted)
            {
                _emitted = true;
                Set("visible", true);
                // 射出瞬间恢复 Duration：到期时刻 = 射出 + config（延迟期间被放大，不占用显示时长）
                Duration = _configDuration + DelaySeconds;
            }

            float emitElapsed = _lifeElapsed - DelaySeconds;

            // 到期前淡出：shader fade uniform 线性衰减（仅光束层）。
            // Spotlight 不参与光束淡出——它有独立生命周期（SpotlightDuration），
            // 否则 FadeDuration ≥ Duration 时光点从第一帧就被衰减到不可见，结束后又"突然出现"。
            float remaining = GetRemainingDuration();
            if (remaining < FadeDuration && FadeDuration > 0f)
            {
                float t = FadeDuration > 0f ? Mathf.Clamp(remaining / FadeDuration, 0f, 1f) : 0f;
                _glowMat?.SetShaderParameter("fade", t);
                _beamMat?.SetShaderParameter("fade", t);
            }

            // 光点淡入（射出后前 SpotlightFadeIn 秒 0 → 1），完成后保持全亮
            if (_spotlight != null && SpotlightFadeIn > 0f)
            {
                var sc = _spotlight.Modulate;
                float fadeInT = Mathf.Clamp(emitElapsed / SpotlightFadeIn, 0f, 1f);
                _spotlight.Modulate = new Color(sc.R, sc.G, sc.B, fadeInT);
            }

            UpdateFacing();
            UpdateBeam();
        }

        /// <summary>效果移除时：把发光点分离到玩家身上继续存活（光束销毁不影响光点）。</summary>
        public override void OnRemoved()
        {
            DetachSpotlight();
            base.OnRemoved();
        }

        /// <summary>玩家是否已退出攻击状态（无状态机的 actor 不做感知，保持原生命周期）。</summary>
        private bool ShouldEndWithAttack()
        {
            if (Actor == null || Actor.StateMachine == null) return false;
            return Actor.StateMachine.CurrentState?.Name != "Attack";
        }

        /// <summary>朝向跟随：朝右 → 光束伸向右（Rotation 0）；朝左 → 旋转 180° 镜像 + 偏移取反侧。</summary>
        private void UpdateFacing()
        {
            if (Actor == null) return;

            bool facingRight = Actor.FacingRight;
            Set("rotation", facingRight ? 0f : Mathf.Pi);
            Set("position", new Vector2(facingRight ? BeamOffset.X : -BeamOffset.X, BeamOffset.Y));
        }

        /// <summary>
        /// 分离发光点：特效销毁时把 Spotlight 重新挂到当前父级（玩家），保持全局位置跟随移动，
        /// 重新亮起（光束淡出可能已压低 alpha），再存活 SpotlightDuration 秒后自毁。
        /// </summary>
        private void DetachSpotlight()
        {
            if (_spotlight == null || !IsInstanceValid(_spotlight)) return;

            var newParent = GetParent();
            if (newParent == null) return;

            Vector2 globalPos = _spotlight.GlobalPosition;
            _spotlight.GetParent()?.RemoveChild(_spotlight);
            newParent.AddChild(_spotlight);
            _spotlight.GlobalPosition = globalPos;

            var c = _spotlight.Modulate;
            _spotlight.Modulate = new Color(c.R, c.G, c.B, 1f);

            // 光点独立完整时长（不含延迟、不扣光束 Duration）：光束结束后光点再活 SpotlightDuration 秒
            float extra = Mathf.Max(0f, SpotlightDuration);
            var spotlight = _spotlight;
            _spotlight = null;
            var tree = GetTree();
            if (tree == null) return;

            // 分离后存活 extra 秒，最后 SpotlightFadeOut 秒淡出再销毁
            float fadeOut = Mathf.Max(0f, SpotlightFadeOut);
            float delay = Mathf.Max(0f, extra - fadeOut);
            tree.CreateTimer(delay).Timeout += () =>
            {
                if (!IsInstanceValid(spotlight)) return;
                if (fadeOut <= 0f)
                {
                    spotlight.QueueFree();
                    return;
                }
                var tween = tree.CreateTween();
                tween.TweenProperty(spotlight, "modulate:a", 0f, fadeOut);
                tween.TweenCallback(Callable.From(() =>
                {
                    if (IsInstanceValid(spotlight))
                        spotlight.QueueFree();
                }));
            };
        }

        /// <summary>光束长度：射线命中点距离（撞墙即止）或 BeamLength，生长动画插值后按纹理尺寸缩放。</summary>
        private void UpdateBeam()
        {
            if (_ray == null || _beamSprite == null) return;

            _ray.ForceRaycastUpdate();
            // ActorEffect 基类是 Node（无 ToLocal），用全局坐标差计算命中距离
            Vector2 selfGlobal = Get("global_position").AsVector2();
            float rawLength = _ray.IsColliding()
                ? (_ray.GetCollisionPoint() - selfGlobal).Length()
                : BeamLength;

            float grow = GrowDuration > 0f
                ? Mathf.Clamp((_lifeElapsed - DelaySeconds) / GrowDuration, 0f, 1f)
                : 1f;
            _currentLength = Mathf.Lerp(0f, rawLength, grow);

            if (_glowSprite != null)
                _glowSprite.Scale = new Vector2(_currentLength / _texW, GlowWidth / _texH);
            _beamSprite.Scale = new Vector2(_currentLength / _texW, BeamWidth / _texH);
        }
    }
}

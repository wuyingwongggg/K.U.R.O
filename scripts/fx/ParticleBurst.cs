using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
    /// <summary>
    /// 通用粒子爆散特效。
    /// 在 SpawnDuration 内逐帧生成多个粒子实例，每个以随机速度/角度/缩放向外飞行，
    /// 到期后自动淡出销毁。同时可生成一个中心爆炸特效。
    /// 粒子素材可以是任意 Node2D 场景（如 CubeData_small），
    /// 若为 RotatingCube 类型则额外接管其移动与生命周期。
    /// </summary>
    public partial class ParticleBurst : Node2D, IFacingDirectional
    {
        private bool _facingRight = true;

        /// <summary>
        /// 兼容 EnemyAttackTemplate.SpawnEffectAtEnemy 的朝向传递。
        /// 始终将 FacingRight 映射到 BaseDirection（Right/Left）。
        /// 调用方如需自定义方向，请在设置 FacingRight 之后单独覆盖 BaseDirection。
        /// </summary>
        public bool FacingRight
        {
            get => _facingRight;
            set
            {
                _facingRight = value;
                BaseDirection = value ? Vector2.Right : Vector2.Left;
            }
        }

        /// <summary>粒子素材场景。</summary>
        [Export] public PackedScene? ParticleScene { get; set; }
        /// <summary>中心爆炸特效场景，在首帧生成于爆散中心。</summary>
        [Export] public PackedScene? CenterEffect { get; set; }

        [ExportCategory("Spawn")]
        /// <summary>粒子总数。</summary>
        [Export(PropertyHint.Range, "1,100,1")] public int SpawnCount = 12;
        /// <summary>生成起点在爆散中心周围的随机偏移半径（像素）。</summary>
        [Export(PropertyHint.Range, "0,200,1")] public float SpawnRadius = 10f;
        /// <summary>全部粒子生成完毕所需时间。0 = 瞬间全部生成。</summary>
        [Export(PropertyHint.Range, "0.05,5,0.05")] public float SpawnDuration = 0.15f;

        [ExportCategory("Velocity")]
        /// <summary>单个粒子最小飞行速度（像素/秒）。</summary>
        [Export(PropertyHint.Range, "50,3000,10")] public float MinSpeed = 200f;
        /// <summary>单个粒子最大飞行速度（像素/秒）。每个粒子在范围内随机。</summary>
        [Export(PropertyHint.Range, "50,3000,10")] public float MaxSpeed = 600f;
        /// <summary>散射角度范围（度），以 BaseDirection 为中心左右对称展开。360 = 全周散射。</summary>
        [Export(PropertyHint.Range, "0,360,1")] public float SpreadAngle = 360f;
        /// <summary>模拟重力加速度（像素/秒^2），正值向下。0 = 无重力。</summary>
        [Export(PropertyHint.Range, "-500,500,10")] public float Gravity = 200f;
        /// <summary>散射锥心的基准方向。零向量时回退到全随机方向（忽略 SpreadAngle）。</summary>
        [Export] public Vector2 BaseDirection;

        [ExportCategory("Damage")]
        /// <summary>单个粒子的伤害值。仅对 RotatingCube 类型粒子生效。</summary>
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 10;
        /// <summary>单个粒子的可攻击阵营。仅对 RotatingCube 类型粒子生效。</summary>
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Player;

        [ExportCategory("Scale")]
        /// <summary>单个粒子最小缩放。每个粒子在范围内随机。</summary>
        [Export(PropertyHint.Range, "0.1,5,0.05")] public float ScaleMin = 0.3f;
        /// <summary>单个粒子最大缩放。</summary>
        [Export(PropertyHint.Range, "0.1,5,0.05")] public float ScaleMax = 1.2f;

        [ExportCategory("Lifetime")]
        /// <summary>单个粒子最短飞行阶段时长（秒）。到期后进入 despawn 消解。</summary>
        [Export(PropertyHint.Range, "0.01,10,0.01")] public float MinLifetime = 0.5f;
        /// <summary>单个粒子最长飞行阶段时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.01,10,0.01")] public float MaxLifetime = 1.5f;

        [ExportCategory("Auto")]
        /// <summary>加入场景树时自动播放。设为 false 时需手动调用 Play()。</summary>
        [Export] public bool AutoPlay = true;

        private int _spawned;
        private float _spawnTimer;
        private bool _playing;
        private bool _centerEffectSpawned;

        public override void _Ready()
        {
            if (AutoPlay)
                Play();
        }

        /// <summary>
        /// 手动触发爆散。可重复调用以重播。
        /// </summary>
        public void Play()
        {
            _playing = true;
            _spawned = 0;
            _spawnTimer = 0f;
            _centerEffectSpawned = false;
        }

        public override void _Process(double delta)
        {
            if (!_playing) return;

            // 首帧生成中心爆炸特效——此时 GlobalPosition 已被调用方正确设置
            if (!_centerEffectSpawned)
            {
                _centerEffectSpawned = true;
                SpawnCenterEffect();
            }

            float dt = (float)delta;
            _spawnTimer += dt;

            // 按 SpawnDuration 线性插值计算当前应生成的粒子数，实现逐帧均匀散布
            int targetSpawned = SpawnDuration > 0f
                ? Mathf.RoundToInt(Mathf.Lerp(0, SpawnCount, Mathf.Min(_spawnTimer / SpawnDuration, 1f)))
                : SpawnCount;

            while (_spawned < targetSpawned)
            {
                SpawnParticle();
                _spawned++;
            }

            // 全部生成完毕 → 自毁。子粒子已独立于父节点，不受影响。
            if (_spawned >= SpawnCount)
            {
                _playing = false;
                QueueFree();
            }
        }

        /// <summary>
        /// 在爆散中心实例化 CenterEffect。
        /// 自动适配 AnimatedSprite2D / GPUParticles2D 的播放与自毁。
        /// </summary>
        private void SpawnCenterEffect()
        {
            if (CenterEffect == null) return;
            var parent = GetParent();
            if (parent == null) return;

            var instance = CenterEffect.Instantiate<Node2D>();
            if (instance == null) return;

            parent.AddChild(instance);
            instance.GlobalPosition = GlobalPosition;

            if (instance is AnimatedSprite2D anim)
            {
                anim.Play();
                anim.AnimationFinished += () => instance.QueueFree();
            }
            else if (instance.HasMethod("restart"))
            {
                instance.Call("restart");
            }

            if (instance is GpuParticles2D particles)
            {
                particles.Emitting = true;
                particles.Finished += () => instance.QueueFree();
            }
        }

        /// <summary>
        /// 生成一个粒子实例。
        /// 若为 RotatingCube 类型则接管移动（Speed=0，tween 驱动抛物线飞行）
        /// 并设置 BaseScale/Duration/Despawn 等生命周期参数。
        /// </summary>
        private void SpawnParticle()
        {
            if (ParticleScene == null) return;
            var parent = GetParent();
            if (parent == null) return;

            var instance = ParticleScene.Instantiate<Node2D>();
            if (instance == null) return;

            float halfAngle = SpreadAngle * 0.5f;
            float angleOffset = Mathf.DegToRad((float)GD.RandRange(-halfAngle, halfAngle));
            Vector2 dir = BaseDirection.LengthSquared() > 0.01f
                ? BaseDirection.Normalized().Rotated(angleOffset)
                : Vector2.FromAngle((float)GD.RandRange(0f, Mathf.Tau));

            float speed = (float)GD.RandRange(MinSpeed, MaxSpeed);
            float lifetime = (float)GD.RandRange(MinLifetime, MaxLifetime);
            float scale = (float)GD.RandRange(ScaleMin, ScaleMax);

            if (instance is RotatingCube cube)
            {
                cube.BaseScale = scale;
                cube.Speed = 0f;
                cube.Duration = lifetime;
                cube.FacingRight = GD.Randf() > 0.5f;
                cube.MaxVerticalTiltDegrees = (float)GD.RandRange(0f, 90f);
                cube.Damage = Damage;
                cube.TargetableFactions = TargetableFactions;
            }

            if (instance is ParabolicProjectile stone)
            {
                stone.Direction = dir;
                stone.Distance = speed * lifetime;
                stone.Duration = lifetime;
                stone.Damage = Damage;
                stone.TargetableFactions = TargetableFactions;
            }

            parent.AddChild(instance);
            instance.GlobalPosition = GlobalPosition + RandomInsideCircle(SpawnRadius);

            if (instance is RotatingCube cube2)
            {
                float flightDuration = lifetime + cube2.DespawnDuration;
                Vector2 endPos = instance.GlobalPosition
                    + dir * (speed * flightDuration)
                    + new Vector2(0f, Gravity * flightDuration * flightDuration * 0.5f);

                var tween = instance.CreateTween();
                tween.TweenProperty(instance, "global_position", endPos, flightDuration);
                tween.SetEase(Tween.EaseType.Out);
                tween.SetTrans(Tween.TransitionType.Cubic);
            }
        }

        private static Vector2 RandomInsideCircle(float radius)
        {
            float angle = (float)GD.RandRange(0f, Mathf.Tau);
            float r = (float)GD.RandRange(0f, radius);
            return new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }
    }
}

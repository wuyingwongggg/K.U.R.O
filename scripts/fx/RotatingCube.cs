using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
    /// <summary>
    /// 伪 3D 旋转线框立方体弹幕。
    /// 三层 Sprite2D 叠加渲染：FaceFill（面+二进制雨）+ Wireframe（发光线框）+ BuildMask（构建扫描线）。
    /// 生命周期：spawn 构建动画 → 直线飞行 → 超时或命中后 despawn 消解动画。
    /// </summary>
    public partial class RotatingCube : Node2D, IFacingDirectional
    {
        [ExportCategory("Movement")]
        [Export] public bool FacingRight { get; set; } = true;
        /// <summary>飞行速度（像素/秒）。外部（如 CubeDataBoom）可设为 0 接管移动。</summary>
        [Export(PropertyHint.Range, "50,6000,10")] public float Speed = 600f;
        /// <summary>朝玩家瞄准时的最大垂直偏移角度（度）。0 = 纯水平飞行。</summary>
        [Export(PropertyHint.Range, "0,360,0.5")] public float MaxVerticalTiltDegrees = 30f;

        [ExportCategory("Timing")]
        /// <summary>飞行阶段时长。到期后进入 despawn。</summary>
        [Export(PropertyHint.Range, "0.01,30,0.01")] public float Duration = 8.0f;

        [ExportCategory("Lifecycle")]
        /// <summary>spawn 构建动画时长（build_progress 0→1 + scale pop）。</summary>
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float BuildDuration = 0.3f;
        /// <summary>despawn 消解动画时长（build_progress 1→0）。</summary>
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float DespawnDuration = 0.5f;
        /// <summary>销毁时在当前位置生成的额外特效（如爆炸粒子）。</summary>
        [Export] public PackedScene? DestroyEffect { get; set; }
        /// <summary>spawn 动画结束后的目标缩放。外部（如 CubeDataBoom）可设置。</summary>
        public float BaseScale { get; set; } = 1f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem;
        [Export] public bool AllowSelfDamage { get; set; } = false;
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 25;

        [ExportCategory("Collision Layers")]
        [Export(PropertyHint.Range, "1,32,1")] public int PlayerCollisionLayer = 3;
        [Export(PropertyHint.Range, "1,32,1")] public int EnemyCollisionLayer = 2;
        [Export(PropertyHint.Range, "1,32,1")] public int WorldItemCollisionLayer = 1;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,3000,1")] public float KnockbackSpeed = 400f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

        // 三层渲染 Sprite
        private Area2D? _attackArea;
        private Sprite2D? _wireframeSprite;
        private Sprite2D? _buildSprite;
        private Sprite2D? _faceSprite;
        // 每个 Sprite 持有独立的 Material 副本，避免多个 cube 共享材质参数
        private ShaderMaterial? _wireframeMaterial;
        private ShaderMaterial? _buildMaterial;
        private ShaderMaterial? _faceMaterial;
        // 飞行向量（由 PlaySpawnAnimation 在 spawn 完成后计算）
        private Vector2 _velocity;
        // 飞行计时器，归零时触发 Destroy
        private float _timer;
        private bool _spawning;
        private bool _despawning;
        private float _despawnTimer;
        // 命中后不再造成伤害，直接销毁
        private bool _hit;
        // 攻击来源（父节点下的敌人），用于同阵营伤害过滤
        private GameActor? _attacker;

        public override void _Ready()
        {
            _timer = Duration;
            _spawning = true;
            _despawning = false;
            _hit = false;

            ResolveSprites();
            ResolveMaterials();

            _attackArea = GetNodeOrNull<Area2D>("AttackArea");
            if (_attackArea != null)
            {
                ApplyCollisionMaskOverride();
                _attackArea.BodyEntered += OnAttackAreaBodyEntered;
                _attackArea.AreaEntered += OnAttackAreaAreaEntered;
            }

            ResolveAttacker();
            PlaySpawnAnimation();
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            // ———— 消解阶段 ————
            if (_despawning)
            {
                _despawnTimer -= dt;
                if (_despawnTimer <= 0f)
                {
                    SpawnDestroyEffect();
                    QueueFree();
                    return;
                }
            }

            // spawn 中或已命中 → 暂停更新
            if (_spawning || _hit) return;

            // 匀速直线飞行
            GlobalPosition += _velocity * dt;

            // 飞行计时（仅非 despawn 期间，避免重复触发 Destroy）
            if (!_despawning)
            {
                _timer -= dt;
                if (_timer <= 0f)
                    Destroy();
            }

            // despawn 期间驱动 build_progress 从 1→0（消解扫描线从上往下），各shader的 alpha从1-0
            if (_despawning)
            {
                float t = Mathf.Max(0f, _despawnTimer / DespawnDuration);
                _wireframeMaterial?.SetShaderParameter("alpha", t);
                _faceMaterial?.SetShaderParameter("face_alpha", t);
                _buildMaterial?.SetShaderParameter("build_progress", t);
            }
        }

        public override void _ExitTree()
        {
            // 断开信号，防止悬空引用
            if (_attackArea != null)
            {
                _attackArea.BodyEntered -= OnAttackAreaBodyEntered;
                _attackArea.AreaEntered -= OnAttackAreaAreaEntered;
            }
            base._ExitTree();
        }

        /// <summary>
        /// spawn 构建动画：build_progress 0→1 + scale 从 0.1 pop 到 BaseScale。
        /// 动画完成后计算朝向玩家的飞行向量。
        /// </summary>
        private void PlaySpawnAnimation()
        {
            // 初始状态：所有层不可见 + 最小缩放
            _wireframeMaterial?.SetShaderParameter("alpha", 0f);
            _buildMaterial?.SetShaderParameter("build_progress", 0f);
            _faceMaterial?.SetShaderParameter("face_alpha", 0f);
            if (_buildSprite != null) _buildSprite.Visible = true;
            Scale = new Vector2(0.1f, 0.1f);

            var tree = GetTree();
            if (tree == null) return;

            var tween = tree.CreateTween();
            tween.SetParallel(true);

            // 构建进度 0→1：扫描线从下往上 reveal 线框 + 面，同时 alpha 淡入
            tween.TweenMethod(Callable.From<float>(p =>
            {
                _buildMaterial?.SetShaderParameter("build_progress", p);
                _wireframeMaterial?.SetShaderParameter("alpha", p);
                _faceMaterial?.SetShaderParameter("face_alpha", p);
            }), 0f, 1f, BuildDuration);
            tween.SetEase(Tween.EaseType.Out);
            tween.SetTrans(Tween.TransitionType.Cubic);

            // Scale pop 带 overshoot 回弹
            tween.TweenProperty(this, "scale", new Vector2(BaseScale, BaseScale), BuildDuration);
            tween.SetEase(Tween.EaseType.Out);
            tween.SetTrans(Tween.TransitionType.Back);

            // 动画完成后：隐藏构建遮罩、计算飞行方向
            tween.Chain().TweenCallback(Callable.From(() =>
            {
                _spawning = false;
                if (_buildSprite != null) _buildSprite.Visible = false;

                // 基准方向：面朝右 = 右飞，面朝左 = 左飞
                float baseAngle = FacingRight ? 0f : Mathf.Pi;

                // 若玩家在前方 → 加入垂直偏移角，朝玩家方向倾斜
                var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
                if (player != null)
                {
                    Vector2 toPlayer = GetPlayerAimCenter(player) - GlobalPosition;
                    if (toPlayer != Vector2.Zero)
                    {
                        float maxTilt = Mathf.DegToRad(MaxVerticalTiltDegrees);
                        float dySign = FacingRight ? 1f : -1f;
                        float tiltAngle = Mathf.Atan2(toPlayer.Y * dySign, Mathf.Abs(toPlayer.X));
                        tiltAngle = Mathf.Clamp(tiltAngle, -maxTilt, maxTilt);
                        baseAngle += tiltAngle;
                    }
                }
                _velocity = new Vector2(Mathf.Cos(baseAngle), Mathf.Sin(baseAngle)) * Speed;
            }));
        }

        /// <summary>
        /// 进入 despawn 阶段：显示 BuildMask，build_progress 将在 _Process 中 1→0 消解。
        /// </summary>
        private void Destroy()
        {
            if (_despawning) return;
            _despawning = true;
            _despawnTimer = DespawnDuration;
            // 重新显示 build 遮罩，初始进度 1（全部可见）
            if (_buildSprite != null) _buildSprite.Visible = true;
            _buildMaterial?.SetShaderParameter("build_progress", 1f);
            // 停止伤害检测
            if (_attackArea != null)
                _attackArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
        }

        /// <summary>
        /// 在当前位置实例化 DestroyEffect 并挂到父节点下。
        /// 若为 ParticleBurst 类型则传入飞行方向，使爆散朝向与 cube 移动方向一致。
        /// </summary>
        private void SpawnDestroyEffect()
        {
            if (DestroyEffect == null) return;

            var instance = DestroyEffect.Instantiate();
            if (instance is Node2D node2D)
            {
                GetParent()?.AddChild(node2D);
                node2D.GlobalPosition = GlobalPosition;

                // 将 cube 的飞行方向传递给 ParticleBurst，使其爆散方向与飞行方向一致
                if (node2D is ParticleBurst burst && _velocity.LengthSquared() > 0.01f)
                {
                    burst.BaseDirection = _velocity.Normalized();
                }
            }
            else
            {
                instance.QueueFree();
            }
        }

        /// <summary>
        /// 按名称查找三层 Sprite：Wireframe / BuildMask / FaceFill。
        /// </summary>
        private void ResolveSprites()
        {
            _wireframeSprite = GetNodeOrNull<Sprite2D>("Wireframe");
            _buildSprite = GetNodeOrNull<Sprite2D>("BuildMask");
            _faceSprite = GetNodeOrNull<Sprite2D>("FaceFill");
        }

        /// <summary>
        /// 为每个 Sprite 的 ShaderMaterial 创建独立副本。
        /// 必须复制：场景中的 material 是所有实例共享的，直接修改会污染其他 cube。
        /// </summary>
        private void ResolveMaterials()
        {
            if (_wireframeSprite?.Material is ShaderMaterial sm)
            {
                _wireframeMaterial = (ShaderMaterial)sm.Duplicate();
                _wireframeSprite.Material = _wireframeMaterial;
            }
            if (_buildSprite?.Material is ShaderMaterial smb)
            {
                _buildMaterial = (ShaderMaterial)smb.Duplicate();
                _buildSprite.Material = _buildMaterial;
            }
            if (_faceSprite?.Material is ShaderMaterial smf)
            {
                _faceMaterial = (ShaderMaterial)smf.Duplicate();
                _faceSprite.Material = _faceMaterial;
            }
        }

        /// <summary>
        /// 根据 TargetableFactions 构建碰撞掩码，追加到 AttackArea 的 CollisionMask 上。
        /// </summary>
        private uint BuildFactionMask()
        {
            uint mask = 0;
            if (TargetableFactions.HasFlag(TargetableFactions.Player))
                mask |= 1u << (PlayerCollisionLayer - 1);
            if (TargetableFactions.HasFlag(TargetableFactions.Enemy))
                mask |= 1u << (EnemyCollisionLayer - 1);
            if (TargetableFactions.HasFlag(TargetableFactions.WorldItem))
                mask |= 1u << (WorldItemCollisionLayer - 1);
            return mask;
        }

        private void ApplyCollisionMaskOverride()
        {
            if (_attackArea == null) return;
            uint factionMask = BuildFactionMask();
            if (factionMask == 0) return;
            _attackArea.CollisionMask |= factionMask;
        }

        /// <summary>
        /// 物理体进入攻击区域：判定阵营 → 造成伤害 → 击退 → 自毁。
        /// 命中即消失（单次伤害），由 _hit 标记防止重复触发。
        /// </summary>
        private void OnAttackAreaBodyEntered(Node body)
        {
            if (_hit || _spawning) return;
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, _attacker)) return;

            // MainCharacter 无敌状态跳过击退但仍造成伤害
            bool alreadyInvincible = body is Actors.Heroes.MainCharacter mc && mc.IsHitInvincible;

            bool dealt = DamageDispatcher.DealDamage(body, Damage, GlobalPosition, _attacker,
                DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _attackArea);
            if (!dealt) return;

            if (!alreadyInvincible && body is GameActor hitActor)
                ApplyKnockback(hitActor);

            _hit = true;
            SpawnDestroyEffect();
            QueueFree();
        }

        /// <summary>
        /// Area2D 进入攻击区域：逻辑同 BodyEntered，处理 Area 类型的目标。
        /// </summary>
        private void OnAttackAreaAreaEntered(Area2D area)
        {
            if (_hit || _spawning) return;
            var target = area.Owner ?? area;
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(target, _attacker)) return;

            bool alreadyInvincible = area.Owner is Actors.Heroes.MainCharacter mc && mc.IsHitInvincible;

            bool dealt = DamageDispatcher.DealDamage(target, Damage, GlobalPosition, _attacker,
                DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _attackArea);
            if (!dealt) return;

            if (!alreadyInvincible && area.Owner is GameActor hitActor)
                ApplyKnockback(hitActor);

            _hit = true;
            SpawnDestroyEffect();
            QueueFree();
        }

        /// <summary>
        /// 沿飞行方向施加击退速度。
        /// </summary>
        private void ApplyKnockback(GameActor actor)
        {
            if (KnockbackSpeed > 0f && _velocity.LengthSquared() > 0.01f)
                actor.Velocity = _velocity.Normalized() * KnockbackSpeed;
        }

        /// <summary>
        /// 在父节点的子节点中查找第一个 "enemies" 组 GameActor 作为攻击来源。
        /// </summary>
        private void ResolveAttacker()
        {
            var parent = GetParent();
            if (parent == null) return;
            foreach (var child in parent.GetChildren())
            {
                if (child.IsInGroup("enemies") && child is GameActor ga)
                {
                    _attacker = ga;
                    break;
                }
            }
        }

        /// <summary>
        /// 获取玩家的受击判定中心（HitArea 的 CollisionShape2D 全局位置），
        /// 用于计算飞行方向时的垂直偏移。
        /// </summary>
        private static Vector2 GetPlayerAimCenter(Node2D player)
        {
            var hitArea = player.GetNodeOrNull<Area2D>("HitArea")
                ?? player.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? player.GlobalPosition;
        }
    }
}

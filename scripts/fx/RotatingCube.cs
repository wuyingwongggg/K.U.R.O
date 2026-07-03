using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
    public partial class RotatingCube : Node2D, IFacingDirectional
    {
        [ExportCategory("Movement")]
        [Export] public bool FacingRight { get; set; } = true;
        [Export(PropertyHint.Range, "50,6000,10")] public float Speed = 600f;
        [Export(PropertyHint.Range, "0,360,0.5")] public float MaxVerticalTiltDegrees = 30f;

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.01,30,0.01")] public float Duration = 8.0f;

        [ExportCategory("Lifecycle")]
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float BuildDuration = 0.3f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float DespawnDuration = 0.5f;
        [Export] public PackedScene? DestroyEffect { get; set; }
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

        private Area2D? _attackArea;
        private Sprite2D? _wireframeSprite;
        private Sprite2D? _buildSprite;
        private Sprite2D? _faceSprite;
        private ShaderMaterial? _wireframeMaterial;
        private ShaderMaterial? _buildMaterial;
        private ShaderMaterial? _faceMaterial;
        private Vector2 _velocity;
        private float _timer;
        private bool _spawning;
        private bool _despawning;
        private float _despawnTimer;
        private bool _hit;
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

            if (_spawning || _hit) return;

            GlobalPosition += _velocity * dt;

            if (!_despawning)
            {
                _timer -= dt;
                if (_timer <= 0f)
                    Destroy();
            }

            if (_despawning)
            {
                float t = Mathf.Max(0f, _despawnTimer / DespawnDuration);
                _wireframeMaterial?.SetShaderParameter("alpha", t);
                _faceMaterial?.SetShaderParameter("face_alpha", t);
            }
        }

        public override void _ExitTree()
        {
            if (_attackArea != null)
            {
                _attackArea.BodyEntered -= OnAttackAreaBodyEntered;
                _attackArea.AreaEntered -= OnAttackAreaAreaEntered;
            }
            base._ExitTree();
        }

        private void PlaySpawnAnimation()
        {
            _wireframeMaterial?.SetShaderParameter("alpha", 0f);
            _buildMaterial?.SetShaderParameter("build_progress", 0f);
            _faceMaterial?.SetShaderParameter("face_alpha", 0f);
            if (_buildSprite != null) _buildSprite.Visible = true;
            Scale = new Vector2(0.1f, 0.1f);

            var tree = GetTree();
            if (tree == null) return;

            var tween = tree.CreateTween();
            tween.SetParallel(true);

            // Build progress: 0→1 reveal
            tween.TweenMethod(Callable.From<float>(p =>
            {
                _buildMaterial?.SetShaderParameter("build_progress", p);
                _wireframeMaterial?.SetShaderParameter("alpha", p);
                _faceMaterial?.SetShaderParameter("face_alpha", p);
            }), 0f, 1f, BuildDuration);
            tween.SetEase(Tween.EaseType.Out);
            tween.SetTrans(Tween.TransitionType.Cubic);

            // Scale pop with overshoot
            tween.TweenProperty(this, "scale", new Vector2(BaseScale, BaseScale), BuildDuration);
            tween.SetEase(Tween.EaseType.Out);
            tween.SetTrans(Tween.TransitionType.Back);

            tween.Chain().TweenCallback(Callable.From(() =>
            {
                _spawning = false;
                if (_buildSprite != null) _buildSprite.Visible = false;
                float baseAngle = FacingRight ? 0f : Mathf.Pi;
                var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
                if (player != null)
                {
                    Vector2 toPlayer = GetPlayerAimCenter(player) - GlobalPosition;
                    bool playerInFront = FacingRight ? toPlayer.X >= 0f : toPlayer.X <= 0f;
                    if (playerInFront && toPlayer != Vector2.Zero)
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

        private void Destroy()
        {
            if (_despawning) return;
            _despawning = true;
            _despawnTimer = DespawnDuration;
            if (_attackArea != null)
                _attackArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
        }

        private void SpawnDestroyEffect()
        {
            if (DestroyEffect == null) return;

            var instance = DestroyEffect.Instantiate();
            if (instance is Node2D node2D)
            {
                GetParent()?.AddChild(node2D);
                node2D.GlobalPosition = GlobalPosition;
            }
            else
            {
                instance.QueueFree();
            }
        }

        private void ResolveSprites()
        {
            _wireframeSprite = GetNodeOrNull<Sprite2D>("Wireframe");
            _buildSprite = GetNodeOrNull<Sprite2D>("BuildMask");
            _faceSprite = GetNodeOrNull<Sprite2D>("FaceFill");
        }

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

        private void OnAttackAreaBodyEntered(Node body)
        {
            if (_hit || _spawning) return;
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, _attacker)) return;

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

        private void ApplyKnockback(GameActor actor)
        {
            if (KnockbackSpeed > 0f && _velocity.LengthSquared() > 0.01f)
                actor.Velocity = _velocity.Normalized() * KnockbackSpeed;
        }

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

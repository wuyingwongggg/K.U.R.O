using Godot;
using Kuros.Core;
using Kuros.Items.Effects;

namespace Kuros.Items.World
{
    [GlobalClass]
    public partial class DestructibleWorldItem : Node, IDamageable
    {
        [ExportCategory("Health")]
        [Export(PropertyHint.Range, "1,9999,1")] public float MaxHP = 100f;
        public float CurrentHP { get; private set; }

        [ExportCategory("Destruction")]
        [Export] public NodePath DestroyTargetPath { get; set; } = new("..");
        [Export(PropertyHint.Range, "0,10,0.01")] public float DestructionDelay = 0.5f;
        [Export] public Vector2 EffectOffset = Vector2.Zero;

        [ExportCategory("Hit Flash")]
        [Export] public NodePath HitFlashSpritePath { get; set; } = new();
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float HitFlashDuration = 0.15f;
        [Export(PropertyHint.Range, "1,120,1")] public float HitFlashSpeed = 30f;
        [Export(PropertyHint.Range, "0,500,0.1")] public float HitShakeIntensity = 100f;
        [Export] public Color HitFlashColor = new Color(1f, 1f, 1f, 1f);

        private ItemDefinition? _itemDefinition;
        private Vector2 _spawnPos;
        private Sprite2D? _hitFlashSprite;
        private ShaderMaterial? _hitFlashMaterial;
        private Material? _hitFlashOriginalMaterial;
        private float _hitFlashTimer;
        private bool _hitFlashActive;

        public bool IsDead => CurrentHP <= 0f;

        public override void _Ready()
        {
            if (Engine.IsEditorHint()) return;

            CurrentHP = MaxHP;

            var parent = GetParent();
            if (parent != null)
                _itemDefinition = parent.Get("ItemDefinition").As<ItemDefinition>();

            SetupHitFlash();
        }

        public override void _Process(double delta)
        {
            if (!_hitFlashActive) return;

            _hitFlashTimer -= (float)delta;
            if (_hitFlashTimer <= 0f)
            {
                EndHitFlash();
                return;
            }

            float t = _hitFlashTimer / HitFlashDuration;
            _hitFlashMaterial?.SetShaderParameter("hit_effect", t);
        }

        public void TakeDamage(float damage)
        {
            if (IsDead) return;

            CurrentHP = Mathf.Max(0f, CurrentHP - damage);
            TriggerHitFlash();

            if (CurrentHP <= 0f)
                Destroy();
        }

        private void Destroy()
        {
            var rigidBody = GetParent()?.GetNodeOrNull<Node2D>("RigidBody2D");
            _spawnPos = (rigidBody?.GlobalPosition ?? Vector2.Zero) + EffectOffset;

            SpawnDestroyEffects();

            var target = ResolveDestroyTarget();
            if (target == null)
            {
                QueueFree();
                return;
            }

            if (DestructionDelay > 0f)
            {
                GetTree().CreateTimer(DestructionDelay).Timeout += () =>
                {
                    if (IsInstanceValid(target))
                        target.QueueFree();
                };
            }
            else
            {
                target.QueueFree();
            }
        }

        private void SpawnDestroyEffects()
        {
            if (_itemDefinition == null) return;

            foreach (var entry in _itemDefinition.GetEffectEntries(ItemEffectTrigger.OnThrowDestroy))
            {
                if (entry?.EffectScene == null) continue;

                try
                {
                    var node = entry.EffectScene.Instantiate();

                    if (node is Node2D node2D)
                    {
                        var worldNode = GetTree().CurrentScene?.GetNodeOrNull<Node>("World")
                            ?? GetTree().CurrentScene;
                        worldNode?.AddChild(node2D);
                        node2D.GlobalPosition = _spawnPos;
                    }
                    else if (node is Kuros.Core.Effects.ActorEffect actorEffect)
                    {
                        entry.ApplyOverrides(actorEffect);

                        if (actorEffect is Kuros.Core.Effects.IWorldSpawnable worldSpawnable)
                            worldSpawnable.WorldSpawnPosition = _spawnPos;

                        var lastDroppedBy = GetParent()?.Get("LastDroppedBy").As<GameActor>();
                        if (lastDroppedBy?.EffectController != null)
                            lastDroppedBy.ApplyEffect(actorEffect);
                        else
                            actorEffect.QueueFree();
                    }
                    else
                    {
                        node?.QueueFree();
                    }
                }
                catch (System.Exception ex)
                {
                    GD.PushWarning($"[DestructibleWorldItem] 无法生成销毁效果: {ex.Message}");
                }
            }
        }

        private void SetupHitFlash()
        {
            if (HitFlashSpritePath.IsEmpty) return;

            _hitFlashSprite = GetNodeOrNull<Sprite2D>(HitFlashSpritePath);
            if (_hitFlashSprite == null) return;

            var shader = GD.Load<Shader>("res://shaders/materials/trigger_hit.gdshader");
            if (shader == null) return;

            _hitFlashMaterial = new ShaderMaterial();
            _hitFlashMaterial.Shader = shader;
            _hitFlashMaterial.SetShaderParameter("get_hit", false);
            _hitFlashMaterial.SetShaderParameter("hit_effect", 0f);
            _hitFlashMaterial.SetShaderParameter("flash_color", HitFlashColor);
            _hitFlashMaterial.SetShaderParameter("flash_speed", HitFlashSpeed);
            _hitFlashMaterial.SetShaderParameter("shake_intensity", HitShakeIntensity);
        }

        private void TriggerHitFlash()
        {
            if (_hitFlashSprite == null || _hitFlashMaterial == null) return;

            if (!_hitFlashActive)
                _hitFlashOriginalMaterial = _hitFlashSprite.Material;

            _hitFlashSprite.Material = _hitFlashMaterial;
            _hitFlashMaterial.SetShaderParameter("get_hit", true);
            _hitFlashMaterial.SetShaderParameter("hit_effect", 1f);
            _hitFlashTimer = HitFlashDuration;
            _hitFlashActive = true;
        }

        private void EndHitFlash()
        {
            _hitFlashActive = false;
            _hitFlashMaterial?.SetShaderParameter("get_hit", false);
            _hitFlashMaterial?.SetShaderParameter("hit_effect", 0f);

            if (_hitFlashSprite != null)
                _hitFlashSprite.Material = _hitFlashOriginalMaterial;
        }

        private Node? ResolveDestroyTarget()
        {
            if (!DestroyTargetPath.IsEmpty)
                return GetNodeOrNull<Node>(DestroyTargetPath);
            return GetParent();
        }
    }
}

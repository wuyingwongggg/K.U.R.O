using System.Collections.Generic;
using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 热流冲击：释放核心技能后的 buff 期间，奔跑/闪避状态下接触范围内
    /// 的目标进入即触发一次伤害，之后按 DamageInterval 持续伤害。
    /// 伤害固定为 Damage，RangeValues = 每层接触范围（px）。
    /// 结算模型参考 ECoreAttackEffect：区域信号驱动 + 最小间隔保护
    /// （GetSecondsSinceLastDamageTaken，防止快速进出刷伤害）。
    /// </summary>
    [GlobalClass]
    public partial class MachineHeatFlowImpactEffect : ActorEffect
    {
        [Export] public float[] RangeValues { get; set; } = { 100f, 200f, 300f };   // 每层接触范围（px）
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float DamageInterval = 1f;
        [Export(PropertyHint.Range, "1,500,1")] public float Damage = 10f;           // 每次伤害
        [Export(PropertyHint.Layers2DPhysics)] public uint TargetCollisionMask = 1;
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Enemy;

        /// <summary>玩家身上的特效场景（如火焰 Sprite2D），实例化后直接挂到玩家身上跟随移动。</summary>
        [Export] public PackedScene? VisualEffectScene = null;
        /// <summary>退出奔跑/闪避（或 buff 结束）后，特效延迟 N 秒才消失。</summary>
        [Export(PropertyHint.Range, "0,5,0.1")] public float VisualExitDelay = 1f;
        /// <summary>视觉尺寸随接触范围线性变化：最小层范围对应 VisualScaleMin，最大层范围对应 VisualScaleMax。</summary>
        [Export(PropertyHint.Range, "0.1,20,0.1")] public float VisualScaleMin = 3f;
        [Export(PropertyHint.Range, "0.1,20,0.1")] public float VisualScaleMax = 6f;

        [ExportCategory("Debug")]
        [Export] public bool ShowDebugRadius { get; set; } = false;
        [Export] public Color DebugRadiusColor { get; set; } = new(1f, 0.2f, 0.2f, 0.35f);

        private MachineCoreEffect? _core;
        private int _tier;
        private Area2D? _contactArea;
        private CollisionShape2D? _contactShape;
        private ContactRadiusDrawer? _radiusDrawer;
        private Node2D? _visualInstance;
        private float _exitDelayRemaining;
        private readonly Dictionary<GameActor, float> _actorTimers = new();
        private readonly Dictionary<GameActor, int> _actorRefs = new();

        private float CurrentRadius => _tier < RangeValues.Length ? RangeValues[_tier] : RangeValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            if (Actor == null) return;

            CreateContactArea();

            // 特效场景实例化后直接挂到玩家身上（必定跟随玩家移动），初始隐藏
            if (VisualEffectScene != null)
            {
                var node = VisualEffectScene.Instantiate();
                if (node is Node2D node2d)
                {
                    Actor.AddChild(node2d);
                    node2d.Visible = false;
                    _visualInstance = node2d;
                    UpdateVisualScale();
                }
                else
                {
                    node?.QueueFree();
                }
            }
        }

        /// <summary>视觉缩放随当前层接触范围线性插值：最小范围→VisualScaleMin，最大范围→VisualScaleMax。</summary>
        private void UpdateVisualScale()
        {
            if (_visualInstance == null) return;
            float minR = RangeValues.Length > 0 ? RangeValues[0] : 0f;
            float maxR = RangeValues.Length > 1 ? RangeValues[^1] : minR;
            float t = maxR > minR ? Mathf.Clamp((CurrentRadius - minR) / (maxR - minR), 0f, 1f) : 0f;
            _visualInstance.Scale = Vector2.One * Mathf.Lerp(VisualScaleMin, VisualScaleMax, t);
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, RangeValues.Length - 1);
            // 叠层后同步更新接触区域半径与调试绘制
            if (_contactShape?.Shape is CircleShape2D circle)
                circle.Radius = CurrentRadius;
            if (_radiusDrawer != null)
            {
                _radiusDrawer.Radius = CurrentRadius;
                _radiusDrawer.QueueRedraw();
            }
            UpdateVisualScale();
        }

        public override void OnRemoved()
        {
            if (_contactArea != null && GodotObject.IsInstanceValid(_contactArea))
            {
                _contactArea.BodyEntered -= OnBodyEntered;
                _contactArea.BodyExited -= OnBodyExited;
                _contactArea.AreaEntered -= OnAreaEntered;
                _contactArea.AreaExited -= OnAreaExited;
                _contactArea.QueueFree();
            }
            // 特效实例挂在玩家身上，效果移除时一并销毁
            if (_visualInstance != null && GodotObject.IsInstanceValid(_visualInstance))
                _visualInstance.QueueFree();
            _actorTimers.Clear();
            _actorRefs.Clear();
            base.OnRemoved();
        }

        /// <summary>在玩家身上创建接触区域（圆形，只做监测不参与碰撞）。</summary>
        private void CreateContactArea()
        {
            _contactArea = new Area2D
            {
                Name = "MachineHeatFlowContactArea",
                CollisionLayer = 0,
                CollisionMask = TargetCollisionMask,
                Monitoring = true,
                Monitorable = false,
            };
            _contactArea.AddChild(_contactShape = new CollisionShape2D
            {
                Shape = new CircleShape2D { Radius = CurrentRadius },
            });
            if (ShowDebugRadius)
            {
                _radiusDrawer = new ContactRadiusDrawer
                {
                    Radius = CurrentRadius,
                    DrawColor = DebugRadiusColor,
                };
                _contactArea.AddChild(_radiusDrawer);
            }
            Actor!.AddChild(_contactArea);

            _contactArea.BodyEntered += OnBodyEntered;
            _contactArea.BodyExited += OnBodyExited;
            _contactArea.AreaEntered += OnAreaEntered;
            _contactArea.AreaExited += OnAreaExited;
        }

        protected override void OnTick(double delta)
        {
            if (Actor == null) return;

            // 效果生效（buff + 奔跑/闪避）时显示特效；退出后延迟 VisualExitDelay 秒再隐藏
            // （放在 _core 判空之前，避免核心缺失时特效永不可见）
            bool active = _core != null && _core.IsBuffActive && IsInRunOrDashState();
            if (_visualInstance != null)
            {
                if (active)
                {
                    _exitDelayRemaining = VisualExitDelay; // 生效期间持续武装延迟
                    if (!_visualInstance.Visible)
                        _visualInstance.Visible = true;
                }
                else if (_exitDelayRemaining > 0f)
                {
                    _exitDelayRemaining -= (float)delta; // 退出后延迟倒计时，期间保持显示
                }
                else if (_visualInstance.Visible)
                {
                    _visualInstance.Visible = false; // 延迟结束才隐藏
                }
            }

            // 条件不满足时暂停计时（已进入的目标保留记录，条件恢复后继续结算）
            if (!active) return;

            TickDamage((float)delta);
        }

        private bool IsInRunOrDashState()
        {
            var current = Actor?.StateMachine?.CurrentState;
            if (current == null) return false;
            return current.Name == "Run" || current.Name == "Dash";
        }

        // ── 区域计时伤害（参考 ECoreAttackEffect） ─────────────────

        private bool CanDealDamageNow()
        {
            return _core != null && _core.IsBuffActive && IsInRunOrDashState();
        }

        private void TickDamage(float dt)
        {
            if (_actorTimers.Count == 0) return;

            var dead = new List<GameActor>();
            foreach (var (actor, timer) in _actorTimers)
            {
                if (!GodotObject.IsInstanceValid(actor) || actor.IsDead)
                {
                    dead.Add(actor);
                    continue;
                }

                float accumulated = timer + dt;
                if (accumulated >= DamageInterval)
                {
                    _actorTimers[actor] = 0f;
                    DealDamageToActor(actor);
                }
                else
                {
                    _actorTimers[actor] = accumulated;
                }
            }

            foreach (var a in dead)
            {
                _actorTimers.Remove(a);
                _actorRefs.Remove(a);
            }
        }

        private void DealDamageToActor(GameActor actor)
        {
            // 最小间隔保护：目标刚受过伤（含快速进出区域、被其他来源命中）时跳过
            if (actor.GetSecondsSinceLastDamageTaken() < DamageInterval) return;

            DamageDispatcher.DealDamage(actor, Damage, Actor!.GlobalPosition, Actor,
                DamageSource.AreaEffect, TargetableFactions, false);
        }

        private void OnBodyEntered(Node body)
        {
            if (body is not GameActor actor) return;
            if (DamageDispatcher.BelongsToActor(body, Actor)) return;
            AddActorRef(actor);
        }

        private void OnBodyExited(Node body)
        {
            if (body is GameActor actor)
                RemoveActorRef(actor);
        }

        private void OnAreaEntered(Area2D area)
        {
            if ((string)area.Name != "HitArea") return;

            var actor = area.Owner as GameActor
                ?? area.GetParent() as GameActor
                ?? area.GetParent()?.GetParent() as GameActor;
            if (actor == null) return;
            if (DamageDispatcher.BelongsToActor(actor, Actor)) return;
            AddActorRef(actor);
        }

        private void OnAreaExited(Area2D area)
        {
            if ((string)area.Name != "HitArea") return;

            var actor = area.Owner as GameActor
                ?? area.GetParent() as GameActor
                ?? area.GetParent()?.GetParent() as GameActor;
            if (actor != null)
                RemoveActorRef(actor);
        }

        private void AddActorRef(GameActor actor)
        {
            if (_actorRefs.TryGetValue(actor, out int count))
            {
                _actorRefs[actor] = count + 1;
                return;
            }

            _actorRefs[actor] = 1;
            _actorTimers[actor] = 0f;

            // 进入范围立即触发一次伤害（带最小间隔保护）
            if (CanDealDamageNow())
                DealDamageToActor(actor);
        }

        private void RemoveActorRef(GameActor actor)
        {
            if (!_actorRefs.TryGetValue(actor, out int count)) return;
            if (count > 1)
            {
                _actorRefs[actor] = count - 1;
                return;
            }

            _actorRefs.Remove(actor);
            _actorTimers.Remove(actor);
        }

        /// <summary>调试用：实时绘制 ContactRadius 圆（挂在接触区域下，随玩家移动）。</summary>
        private partial class ContactRadiusDrawer : Node2D
        {
            public float Radius;
            public Color DrawColor;

            public override void _Draw()
            {
                DrawCircle(Vector2.Zero, Radius, DrawColor);
                DrawArc(Vector2.Zero, Radius, 0f, Mathf.Tau, 64, new Color(DrawColor.R, DrawColor.G, DrawColor.B, 1f), 2f);
            }
        }
    }
}

using System.Collections.Generic;
using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Fx;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 脉冲扩散（BuildThrow_A_002）：场上每个乱码块每 PulseInterval 秒向外发出一圈扩散脉冲,
    /// 伤害**跟随扩散环前沿**扫出(与视觉同步:敌人被环碰到的一瞬间才受伤),
    /// 环走完 PulseSpreadSeconds 后对圈内剩余敌人补一刀,随后结束本次脉冲。
    /// 集中卡驱动:OnTick 遍历 throwcore_generated_furniture 组,每块独立计时
    /// (新块首次入场随机相位,避免齐射);块销毁/离树自动剔除,不影响其它块。
    /// TierValues = 各层脉冲半径(层1/2/3 → 300/400/500px)。
    /// </summary>
    [GlobalClass]
    public partial class ThrowPulseBurstEffect : ActorEffect
    {
        /// <summary>脉冲伤害区域形状:伪3D 视觉为斜置平面投影(横宽竖窄),默认椭圆贴合视觉。</summary>
        public enum PulseAreaShape
        {
            Circle,
            Ellipse,
            HorizontalCapsule,
        }

        [Export] public float[] TierValues { get; set; } = { 300f, 400f, 500f };

        /// <summary>单次脉冲对单敌伤害(与层无关)。</summary>
        [Export(PropertyHint.Range, "1,100,1")] public float Damage { get; set; } = 10f;
        /// <summary>每块脉冲间隔(秒)。</summary>
        [Export(PropertyHint.Range, "0.2,5,0.1")] public float PulseInterval { get; set; } = 1f;
        /// <summary>扩散波前沿从中心走到最大半径的耗时(须与视觉环 Duration 一致,伤害随环扫出)。</summary>
        [Export(PropertyHint.Range, "0.1,2,0.05")] public float PulseSpreadSeconds { get; set; } = 0.45f;
        /// <summary>每次脉冲生成的一次性扩散环视觉(为空则纯伤害无视觉)。</summary>
        [Export] public PackedScene? PulseVisualScene { get; set; }

        /// <summary>伤害区域形状(伪3D 视觉为斜置平面投影,横宽竖窄)。</summary>
        [Export] public PulseAreaShape DamageShape { get; set; } = PulseAreaShape.Ellipse;
        /// <summary>竖轴半高 ÷ 水平半长(0.55 ≈ 视觉环 rot_x60° 投影压缩;改视觉角度需同步)。</summary>
        [Export(PropertyHint.Range, "0.1,1,0.05")] public float VerticalHeightFactor { get; set; } = 0.55f;

        // ── 调试:绘制每块实际伤害区域(与视觉环叠加对比)──
        [Export] public bool ShowDebugRegion { get; set; } = false;
        [Export] public Color DebugRegionColor { get; set; } = new Color(1f, 0f, 0f, 0.9f);

        /// <summary>扩散环前沿离中心的半径比例(与 pulse_ring.gdshader 的 radius 曲线严格一致)。</summary>
        private const float FrontStart = 0.18f;
        private const float FrontEasePower = 0.75f;

        private readonly Dictionary<Node, float> _pieceTimers = new();
        private readonly List<PulseSweep> _sweeps = new();
        private DebugRegionOverlay? _debugOverlay;
        private int _tier = 1;
        private float _pruneTimer;

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
        }

        private float CurrentRadius => TierValues.Length > 0
            ? TierValues[Mathf.Clamp(_tier, 1, TierValues.Length) - 1]
            : 0f;

        protected override void OnTick(double delta)
        {
            if (Actor == null || TierValues.Length == 0) return;
            float dt = (float)delta;

            foreach (Node piece in GetTree().GetNodesInGroup(ThrowCoreEffect.ThrowCoreFurnitureGroup))
            {
                if (piece == null || !GodotObject.IsInstanceValid(piece) || !piece.IsInsideTree())
                    continue;
                if (piece is not Node2D piece2D) continue;

                if (!_pieceTimers.TryGetValue(piece, out float timer))
                {
                    timer = (float)GD.RandRange(0.0, PulseInterval); // 随机相位,错开齐射
                    _pieceTimers[piece] = timer;
                }

                timer += dt;
                if (timer >= PulseInterval)
                {
                    timer = 0f;
                    StartSweep(piece2D);
                }
                _pieceTimers[piece] = timer;
            }

            UpdateSweeps(dt);

            UpdateDebugOverlay();

            // 惰性清理:块销毁/拾取(离树)/长按清场后移除悬挂键(每 2s 一次,避免每帧分配)
            _pruneTimer += dt;
            if (_pruneTimer >= 2f)
            {
                _pruneTimer = 0f;
                foreach (Node key in new List<Node>(_pieceTimers.Keys))
                {
                    if (key == null || !GodotObject.IsInstanceValid(key) || !key.IsInsideTree())
                        _pieceTimers.Remove(key);
                }
            }
        }

        /// <summary>一次脉冲:在块当前位置生成扩散环视觉,并登记一个扩散波(伤害沿前沿扫出)。</summary>
        private void StartSweep(Node2D piece)
        {
            SpawnPulseVisual(piece);
            _sweeps.Add(new PulseSweep
            {
                Origin = piece.GlobalPosition,
                Radius = CurrentRadius,
                Age = 0f,
                Hit = new HashSet<ulong>(),
            });
        }

        private void UpdateSweeps(float dt)
        {
            if (_sweeps.Count == 0) return;

            for (int i = _sweeps.Count - 1; i >= 0; i--)
            {
                PulseSweep sweep = _sweeps[i];
                sweep.Age += dt;

                float front = sweep.Radius * FrontFraction(sweep.Age / Mathf.Max(PulseSpreadSeconds, 0.01f));
                DamageEnemiesInside(sweep, front);

                if (sweep.Age >= PulseSpreadSeconds)
                {
                    // 环走完:对仍处于最终半径内、尚未被扫到的敌人补结算(如中途走入圈内的)
                    DamageEnemiesInside(sweep, sweep.Radius);
                    _sweeps.RemoveAt(i);
                }
                else
                {
                    _sweeps[i] = sweep;
                }
            }
        }

        /// <summary>对区域内未命中敌人造成一次伤害(与视觉前沿位置同步,每敌每波至多一次)。</summary>
        private void DamageEnemiesInside(PulseSweep sweep, float insideRadius)
        {
            foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            {
                if (node is not GameActor enemy
                    || !GodotObject.IsInstanceValid(enemy)
                    || enemy.IsDeadOrDying)
                    continue;
                if (sweep.Hit.Contains(enemy.GetInstanceId())) continue;
                if (!IsInsideRegion(sweep.Origin, enemy.GlobalPosition, insideRadius)) continue;

                sweep.Hit.Add(enemy.GetInstanceId());
                DamageDispatcher.DealDamage(enemy, Damage, sweep.Origin, Actor, DamageSource.AreaEffect,
                    TargetableFactions.Enemy, false);
            }
        }

        /// <summary>区域包含测试(前沿半径随扩散缩放,形状与缩放同构)。</summary>
        private bool IsInsideRegion(Vector2 origin, Vector2 point, float radius)
        {
            Vector2 d = point - origin;
            switch (DamageShape)
            {
                case PulseAreaShape.Ellipse:
                {
                    float vf = Mathf.Max(VerticalHeightFactor, 0.05f);
                    float rx = radius;
                    float ry = radius * vf;
                    return d.X * d.X / (rx * rx) + d.Y * d.Y / (ry * ry) <= 1f;
                }
                case PulseAreaShape.HorizontalCapsule:
                {
                    float halfH = radius * Mathf.Max(VerticalHeightFactor, 0.05f);
                    float halfL = radius - halfH; // 中段直线半长
                    if (Mathf.Abs(d.X) <= halfL)
                        return Mathf.Abs(d.Y) <= halfH;
                    float dx = Mathf.Abs(d.X) - halfL;
                    return dx * dx + d.Y * d.Y <= halfH * halfH;
                }
                default: // Circle
                    return d.LengthSquared() <= radius * radius;
            }
        }

        // ── 调试区域绘制(逐帧把每块伤害区域画在效果节点本地坐标)──

        private void UpdateDebugOverlay()
        {
            if (ShowDebugRegion)
            {
                if (_debugOverlay == null || !IsInstanceValid(_debugOverlay))
                {
                    _debugOverlay = new DebugRegionOverlay
                    {
                        Name = "PulseDebugRegion",
                        DrawColor = DebugRegionColor,
                    };
                    AddChild(_debugOverlay);
                }

                _debugOverlay.Regions.Clear();
                foreach (Node piece in GetTree().GetNodesInGroup(ThrowCoreEffect.ThrowCoreFurnitureGroup))
                {
                    if (piece is not Node2D piece2D || !IsInstanceValid(piece2D) || !piece2D.IsInsideTree())
                        continue;
                    _debugOverlay.Regions.Add((_debugOverlay.ToLocal(piece2D.GlobalPosition),
                        CurrentRadius, DamageShape, VerticalHeightFactor));
                }
                _debugOverlay.QueueRedraw();
            }
            else if (_debugOverlay != null)
            {
                _debugOverlay.QueueFree();
                _debugOverlay = null;
            }
        }

        public override void OnRemoved()
        {
            if (_debugOverlay != null)
            {
                _debugOverlay.QueueFree();
                _debugOverlay = null;
            }
            base.OnRemoved();
        }

        /// <summary>环前沿半径比例曲线,与 pulse_ring.gdshader 中 mix(0.18, 1.0, pow(progress, 0.75)) 一致。</summary>
        private static float FrontFraction(float t)
        {
            t = Mathf.Clamp(t, 0f, 1f);
            return FrontStart + (1f - FrontStart) * Mathf.Pow(t, FrontEasePower);
        }

        /// <summary>在块位置生成一次性扩散环视觉(半径=当前层半径,时长对齐 PulseSpreadSeconds,播完自毁)。</summary>
        private void SpawnPulseVisual(Node2D piece)
        {
            if (PulseVisualScene == null) return;

            var visual = PulseVisualScene.Instantiate<Node2D>();
            if (visual is PulseRingVisual ring)
            {
                ring.Radius = CurrentRadius;
                ring.Duration = PulseSpreadSeconds; // 视觉走完时长 = 伤害扫完时长
            }

            if (piece.GetParent() is Node parent)
                parent.AddChild(visual);
            else
            {
                visual.QueueFree();
                return;
            }
            visual.GlobalPosition = piece.GlobalPosition;
        }

        /// <summary>一次进行中的扩散波(每块每脉冲一个;块位置在触发瞬间定格,视觉与伤害同源)。</summary>
        private struct PulseSweep
        {
            public Vector2 Origin;
            public float Radius;
            public float Age;
            public HashSet<ulong> Hit; // 已命中敌人实例(同波不重复结算)
        }
    }

    /// <summary>调试:逐帧绘制每块实际脉冲伤害区域(挂在效果节点下,坐标为效果本地系)。</summary>
    public partial class DebugRegionOverlay : Node2D
    {
        public Color DrawColor { get; set; } = new Color(1f, 0f, 0f, 0.9f);
        public readonly List<(Vector2 Center, float Radius, ThrowPulseBurstEffect.PulseAreaShape Shape, float Vf)> Regions = new();

        public override void _Draw()
        {
            foreach (var r in Regions)
            {
                if (r.Radius <= 0f) continue;
                switch (r.Shape)
                {
                    case ThrowPulseBurstEffect.PulseAreaShape.Circle:
                        DrawArc(r.Center, r.Radius, 0f, Mathf.Tau, 96, DrawColor, 2f);
                        break;
                    case ThrowPulseBurstEffect.PulseAreaShape.Ellipse:
                        DrawEllipse(r);
                        break;
                    case ThrowPulseBurstEffect.PulseAreaShape.HorizontalCapsule:
                        DrawHorizontalCapsule(r);
                        break;
                }
            }
        }

        private void DrawEllipse((Vector2 Center, float Radius, ThrowPulseBurstEffect.PulseAreaShape Shape, float Vf) r)
        {
            int n = 72;
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Tau * i / n;
                pts[i] = r.Center + new Vector2(Mathf.Cos(a) * r.Radius, Mathf.Sin(a) * r.Radius * r.Vf);
            }
            DrawPolyline(pts, DrawColor, 2f, true);
        }

        private void DrawHorizontalCapsule((Vector2 Center, float Radius, ThrowPulseBurstEffect.PulseAreaShape Shape, float Vf) r)
        {
            float halfH = r.Radius * r.Vf;
            float halfL = r.Radius - halfH;
            var pts = new List<Vector2>();
            pts.Add(r.Center + new Vector2(-halfL, halfH));   // 顶线左端
            pts.Add(r.Center + new Vector2(halfL, halfH));    // 顶线右端
            for (int i = 1; i <= 18; i++)                     // 右半圆 90°→-90°(上→下)
            {
                float a = Mathf.Pi * 0.5f - Mathf.Pi * i / 18f;
                pts.Add(r.Center + new Vector2(halfL + Mathf.Cos(a) * halfH, Mathf.Sin(a) * halfH));
            }
            pts.Add(r.Center + new Vector2(-halfL, -halfH));  // 底线右端
            for (int i = 1; i <= 18; i++)                     // 左半圆 270°→90°(下→上)
            {
                float a = Mathf.Pi * 1.5f - Mathf.Pi * i / 18f;
                pts.Add(r.Center + new Vector2(-halfL + Mathf.Cos(a) * halfH, Mathf.Sin(a) * halfH));
            }
            DrawPolyline(pts.ToArray(), DrawColor, 2f, true);
        }
    }
}

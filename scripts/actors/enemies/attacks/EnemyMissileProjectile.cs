using System;
using Godot;
using Kuros.Effects;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 导弹投掷物：垂直上升 → 瞬移至玩家X → 翻转 → 下坠到玩家位置快照。
    ///
    /// 阶段：
    ///   0. Rise — 从起点垂直上升 RiseHeight 像素
    ///   1. Fall — 瞬移到目标 X、翻转 Sprite、垂直下坠到玩家快照位置
    ///
    /// 玩家位置在 _Ready() 时快照（与 EnemyWaiterAThrowProjectile 一致）。
    /// </summary>
    public partial class EnemyMissileProjectile : Node2D
    {
        [ExportCategory("Movement")]
        [Export(PropertyHint.Range, "100,3000,10")] public float RiseHeight { get; set; } = 600f;
        /// <summary>上升阶段时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,3,0.05")] public float RiseDuration { get; set; } = 0.5f;
        /// <summary>下坠阶段时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,3,0.05")] public float FallDuration { get; set; } = 0.3f;
        /// <summary>下坠阶段是否启用弹道弧线（sin 曲线微调，0 为直线坠落）。</summary>
        [Export(PropertyHint.Range, "0,200,1")] public float FallArcHeight { get; set; } = 0f;

        [ExportCategory("Visual")]
        /// <summary>翻转的 Sprite2D 节点路径（可选，留空则自动查找）。</summary>
        [Export] public NodePath? SpritePath { get; set; }

        [ExportCategory("Impact")]
        [Export] public PackedScene[] ImpactEffectScenes { get; set; } = Array.Empty<PackedScene>();
        [Export] public PackedScene? LandingIndicatorPrefab { get; set; }

        private Vector2 _startPos;
        private Vector2 _targetPos;
        private bool _launched;
        private Sprite2D? _sprite;
        private Node? _landingIndicator;

        private enum Phase { Rise, Fall, Done }
        private Phase _phase;
        private float _phaseElapsed;

        public override void _Ready()
        {
            var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            _targetPos = player?.GlobalPosition ?? Vector2.Zero;

            if (SpritePath != null && !SpritePath.IsEmpty)
                _sprite = GetNodeOrNull<Sprite2D>(SpritePath);
            else
                _sprite = FindChild("Sprite2D") as Sprite2D;

            SpawnLandingIndicator();
            SetPhysicsProcess(true);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (!_launched)
            {
                _startPos = GlobalPosition;
                _launched = true;
                _phase = Phase.Rise;
                _phaseElapsed = 0f;
                return;
            }

            if (_phase == Phase.Done) return;

            _phaseElapsed += (float)delta;

            switch (_phase)
            {
                case Phase.Rise:
                    ProcessRise();
                    break;
                case Phase.Fall:
                    ProcessFall();
                    break;
            }
        }

        private void ProcessRise()
        {
            float t = Mathf.Clamp(_phaseElapsed / RiseDuration, 0f, 1f);
            float y = Mathf.Lerp(_startPos.Y, _startPos.Y - RiseHeight, t);
            GlobalPosition = new Vector2(_startPos.X, y);

            if (t >= 1f)
            {
                // 瞬移至目标 X，翻转精灵
                GlobalPosition = new Vector2(_targetPos.X, _startPos.Y - RiseHeight);
                if (_sprite != null)
                    _sprite.FlipV = true;

                _phase = Phase.Fall;
                _phaseElapsed = 0f;
            }
        }

        private void ProcessFall()
        {
            float t = Mathf.Clamp(_phaseElapsed / FallDuration, 0f, 1f);
            float y = Mathf.Lerp(_startPos.Y - RiseHeight, _targetPos.Y, t);
            if (FallArcHeight > 0f)
                y -= Mathf.Sin(t * Mathf.Pi) * FallArcHeight;
            GlobalPosition = new Vector2(_targetPos.X, y);

            if (t >= 1f)
            {
                _phase = Phase.Done;
                OnArrived();
            }
        }

        private void OnArrived()
        {
            SetPhysicsProcess(false);

            foreach (var scene in ImpactEffectScenes)
            {
                if (scene == null) continue;
                var fx = scene.Instantiate<Node>();
                GetParent()?.AddChild(fx);
                if (fx is Node2D fx2d)
                    fx2d.GlobalPosition = GlobalPosition;
                else
                {
                    foreach (var child in fx.GetChildren())
                    {
                        if (child is Node2D child2D)
                        {
                            child2D.GlobalPosition = GlobalPosition;
                            break;
                        }
                    }
                }
            }

            _landingIndicator?.QueueFree();
            QueueFree();
        }

        public override void _ExitTree()
        {
            _landingIndicator?.QueueFree();
            base._ExitTree();
        }

        private void SpawnLandingIndicator()
        {
            if (LandingIndicatorPrefab == null) return;

            var indicator = LandingIndicatorPrefab.Instantiate<Node>();
            _landingIndicator = indicator;
            GetParent()?.AddChild(indicator);
            if (indicator is Node2D indicator2D)
                indicator2D.GlobalPosition = _targetPos;

            var li = indicator is LandingIndicator li0 ? li0 : indicator.GetNodeOrNull<LandingIndicator>(".");
            if (li != null)
            {
                li.WarningDuration = RiseDuration + FallDuration;
                li.Start();
            }
        }
    }
}

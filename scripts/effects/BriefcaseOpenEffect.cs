using Godot;
using Kuros.Core;
using Kuros.Fx;

namespace Kuros.Effects
{
    /// <summary>
    /// 公文包打开效果（世界空间 Node2D 效果，非 ActorEffect）。
    /// 延迟 DelaySeconds 秒后在落点生成 EffectScene，生成的特效独立存在（不随本场景销毁）。
    /// SpawnInterval > 0 时在 DelaySeconds → Duration 期间每 SpawnInterval 秒重复生成；
    /// SpawnInterval = 0 时仅生成一次。Duration 到期后自我销毁。
    ///
    /// 使用 Node2D 而非 ActorEffect：效果本质是投掷落点的世界效果，不参与
    /// EffectController 的 EffectId 去重与 Actor 生命周期绑定——多公文包投掷各自独立生成。
    /// </summary>
    [GlobalClass]
    public partial class BriefcaseOpenEffect : Node2D, IAttackerProvider
    {
        [Export] public PackedScene? EffectScene { get; set; }
        [Export] public Godot.Collections.Dictionary<string, Variant> EffectOverrides { get; set; } = new();
        [Export(PropertyHint.Range, "0,10,0.1")] public float DelaySeconds = 1.0f;
        [Export(PropertyHint.Range, "0,10,0.01")] public float SpawnInterval = 0f;
        /// <summary>
        /// 特效生成锚点（Marker2D 放在本场景节点下）。不为空时按数组顺序依次轮换使用
        /// Marker2D.GlobalPosition 作为生成位置，否则用当前 global_position（投掷落点）。
        /// </summary>
        [Export] public Marker2D[] SpawnMarkers = System.Array.Empty<Marker2D>();
        [Export(PropertyHint.Range, "0,600,0.1")] public float Duration = 10.0f;

        /// <summary>投掷者（由投掷系统 IAttackerProvider 注入）。</summary>
        public GameActor? Attacker { get; set; }

        private float _elapsed;
        private float _delayElapsed;
        private float _intervalElapsed;
        private int _spawnCount;
        private int _spawnMarkerIndex;

        public override void _Ready()
        {
            // 按投掷者朝向翻转自身场景
            ApplyFacing(this, Attacker?.FacingRight ?? true);
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;
            _elapsed += dt;
            _delayElapsed += dt;

            if (_delayElapsed >= DelaySeconds)
            {
                if (_spawnCount == 0)
                {
                    // 延迟结束 → 首次生成
                    SpawnEffect();
                    _intervalElapsed = 0f;
                }
                else if (SpawnInterval > 0f)
                {
                    // 周期性重复生成
                    _intervalElapsed += dt;
                    while (_intervalElapsed >= SpawnInterval)
                    {
                        _intervalElapsed -= SpawnInterval;
                        SpawnEffect();
                    }
                }
            }

            if (Duration > 0f && _elapsed >= Duration)
            {
                QueueFree();
            }
        }

        public override void _ExitTree()
        {
            // 兜底：被销毁但尚未生成时补生成一次（时长未到即被外部移除的场景）
            if (_spawnCount == 0)
                SpawnEffect();
            base._ExitTree();
        }

        private void SpawnEffect()
        {
            _spawnCount++;

            var world = GetParent();
            if (world == null || EffectScene == null) return;

            var pos = GlobalPosition;

            // 指定 SpawnMarkers 时按数组顺序轮换取 Marker2D.GlobalPosition 作为生成位置
            if (SpawnMarkers.Length > 0)
            {
                var marker = SpawnMarkers[_spawnMarkerIndex % SpawnMarkers.Length];
                _spawnMarkerIndex++;
                if (marker != null && GodotObject.IsInstanceValid(marker))
                    pos = marker.GlobalPosition;
            }

            var node = EffectScene.Instantiate();

            if (node is Node2D node2D)
            {
                // 独立加入世界层：不挂在本场景下，随本场景销毁而销毁
                world.AddChild(node2D);
                node2D.GlobalPosition = pos;

                if (node2D is BoomDmgEffect boom)
                    boom.Attacker = Attacker;
                if (node2D is RotatingCube cube)
                    cube.Attacker = Attacker;

                foreach (var pair in EffectOverrides)
                {
                    if (pair.Key == null) continue;
                    try { node2D.Set(pair.Key, pair.Value); }
                    catch (System.Exception ex) { GD.PushWarning($"[BriefcaseOpenEffect] override '{pair.Key}' failed: {ex.Message}"); }
                }

                // 朝向翻转放最后：确保 overrides 覆盖 scale 后仍按投掷者朝向翻转
                ApplyFacing(node2D, Attacker?.FacingRight ?? true);
                // 同步飞行基准方向（RotatingCube 等 IFacingDirectional 目标在无追踪目标时按此朝向飞行）
                if (node2D is IFacingDirectional facing)
                    facing.FacingRight = Attacker?.FacingRight ?? true;
            }
            else
            {
                node?.QueueFree();
            }
        }

        /// <summary>按投掷者朝向翻转节点（面向左时 Scale.X 取反）。</summary>
        private static void ApplyFacing(Node2D node, bool facingRight)
        {
            var scale = node.Scale;
            scale.X = Mathf.Abs(scale.X) * (facingRight ? 1f : -1f);
            node.Scale = scale;
        }
    }
}

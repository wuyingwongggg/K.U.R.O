using Godot;
using Kuros.Core;
using Kuros.Fx;

namespace Kuros.Effects
{
    /// <summary>
    /// Emoji 炸弹效果（世界空间 Node2D 效果，非 ActorEffect）。
    /// 到达落点后延迟 Duration 秒（或提前被销毁时）在世界落点生成 Boom/BoomDmg 场景。
    ///
    /// 玩家路径：SpawnThrowDestroyEffects → AddChild + GlobalPosition=落点 + Attacker=投掷者
    /// 敌人路径：EnemyWaiterAThrowProjectile 同 Node2D 分支
    ///
    /// 使用 Node2D 而非 ActorEffect：不参与 EffectController 的 EffectId 去重与 Actor 生命周期
    /// 绑定——多投掷各自独立爆炸。
    /// </summary>
    [GlobalClass]
    public partial class EmojiBoomEffect : Node2D, IAttackerProvider
    {
        [Export] public PackedScene? BoomScene { get; set; }
        [Export] public PackedScene? BoomDmgScene { get; set; }
        [Export] public Godot.Collections.Dictionary<string, Variant> BoomSceneOverrides { get; set; } = new();
        [Export] public Godot.Collections.Dictionary<string, Variant> BoomDmgOverrides { get; set; } = new();
        [Export(PropertyHint.Range, "0,600,0.1")] public float Duration { get; set; } = 5.0f;

        /// <summary>投掷者（由投掷系统 IAttackerProvider 注入）。</summary>
        public GameActor? Attacker { get; set; }

        private bool _spawned;
        private float _elapsed;

        public override void _Process(double delta)
        {
            _elapsed += (float)delta;
            if (Duration > 0f && _elapsed >= Duration)
            {
                SpawnChildEffects();
                QueueFree();
            }
        }

        public override void _ExitTree()
        {
            // 兜底：被外部销毁但尚未生成时补生成（时长未到即被移除的场景）
            if (!_spawned)
                SpawnChildEffects();
            base._ExitTree();
        }

        private void SpawnChildEffects()
        {
            if (_spawned) return;
            _spawned = true;

            var world = GetParent();
            if (world == null) return;

            SpawnScene(BoomScene, world, GlobalPosition, BoomSceneOverrides);
            SpawnScene(BoomDmgScene, world, GlobalPosition, BoomDmgOverrides);
        }

        private void SpawnScene(PackedScene? scene, Node world, Vector2 pos, Godot.Collections.Dictionary<string, Variant> overrides)
        {
            if (scene == null) return;

            var node = scene.Instantiate();
            if (node is Node2D node2D)
            {
                // 先应用覆盖属性（AddChild 之前），确保子场景 _Ready 时读取到正确值。
                // 若在 AddChild 之后才 Set，_Ready 已按默认值分支执行（如 SpawnDelay=0
                // 时定时器分支未挂载、ScaleSpawnWithSelf 读取到 false），覆盖全部失效
                foreach (var pair in overrides)
                {
                    if (pair.Key == null) continue;
                    try { node2D.Set(pair.Key, pair.Value); }
                    catch (System.Exception ex) { GD.PushWarning($"[EmojiBoomEffect] override '{pair.Key}' failed: {ex.Message}"); }
                }

                if (node2D is BoomDmgEffect boom)
                    boom.Attacker = Attacker;

                world.AddChild(node2D);
                node2D.GlobalPosition = pos;
            }
            else
            {
                node?.QueueFree();
            }
        }
    }
}

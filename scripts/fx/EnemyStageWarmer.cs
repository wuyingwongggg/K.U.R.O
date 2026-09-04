using Godot;

namespace Kuros.Fx
{
    /// <summary>
    /// 敌人资源预热器（Autoload）：启动时静默实例化所有敌人场景（透明渲染两帧后销毁）——
    /// 把 Spine 骨骼数据解析 + 敌人材质 Shader 编译等一次性成本从"战斗中首次生成敌人"挪到开局。
    /// 与 ParticleEffectWarmer 同模式（实例化 + 渲染强制触发编译）；串行预热（每场景 2 帧）不阻塞主流程。
    /// 无 Camera2D 时 CanvasItem 照常渲染（默认画布变换），Shader 编译照常触发——无需等待相机。
    /// </summary>
    public partial class EnemyStageWarmer : Node
    {
        [Export] public bool WarmOnReady { get; set; } = true;
        [Export] public Godot.Collections.Array<string> EnemySceneDirs { get; set; } = new() { "res://scenes/actors/characters" };
        /// <summary>只预热此前缀的场景（空 = 预热目录内全部 .tscn）。</summary>
        [Export] public string EnemyFilePrefix { get; set; } = "Enemy_";

        private bool _warmed;

        public override void _Ready()
        {
            if (WarmOnReady)
                CallDeferred(nameof(StartWarm));
        }

        /// <summary>启动即预热（无需等相机/玩家——无 Camera2D 时 CanvasItem 照常渲染，Shader 编译照常触发；
        /// Autoload 上下文里 GetCamera2D/GetFirstNodeInGroup 均不可靠）。</summary>
        private async void StartWarm()
        {
            await WarmUpAllAsync();
        }

        /// <summary>预热所有敌人场景（串行，每场景渲染两帧）。重复调用安全。
        /// headless（导出/CI 工具场景）下跳过——无渲染，预热无意义且部分资源加载会抛异常。</summary>
        public async System.Threading.Tasks.Task WarmUpAllAsync()
        {
            if (_warmed) return;
            _warmed = true;
            SetProcess(false);

            if (DisplayServer.GetName() == "headless") return;

            foreach (var dir in EnemySceneDirs)
            {
                using var d = DirAccess.Open(dir);
                if (d == null) continue;

                d.ListDirBegin();
                string fileName;
                while ((fileName = d.GetNext()) != "")
                {
                    if (d.CurrentIsDir()) continue;
                    if (!fileName.EndsWith(".tscn")) continue;
                    if (!string.IsNullOrEmpty(EnemyFilePrefix) && !fileName.StartsWith(EnemyFilePrefix)) continue;

                    try
                    {
                        var scene = GD.Load<PackedScene>($"{dir}/{fileName}");
                        if (scene != null)
                            await WarmUpAsync(scene);
                    }
                    catch (System.Exception ex)
                    {
                        GD.PushWarning($"[EnemyStageWarmer] 预热失败（跳过）：{fileName} — {ex.Message}");
                    }
                }
                d.ListDirEnd();
            }
        }

        /// <summary>静默实例化单个敌人场景：全透明（可见但不可见）、渲染两帧后销毁。
        /// 位置固定在画布原点——启动预热时玩家必不存在，不依赖玩家/相机。</summary>
        private async System.Threading.Tasks.Task WarmUpAsync(PackedScene scene)
        {
            var instance = scene.Instantiate<Node>();
            AddChild(instance);

            if (instance is Node2D n2d)
            {
                n2d.GlobalPosition = Vector2.Zero;
                n2d.Modulate = new Color(1f, 1f, 1f, 0f); // 可见但全透明——照常提交渲染触发编译
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            instance.QueueFree();
        }
    }
}

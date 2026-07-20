using Godot;
using Godot.Collections;
using System.Collections.Generic;
using Kuros.Utils;

namespace Kuros.Managers
{
    /// <summary>
    /// 关卡生成器：根据配置随机拼接房间场景，生成无缝横向关卡。
    ///
    /// 拼接规则：
    ///   每个房间场景根节点下必须有 AreaSize/Area2D → CollisionShape2D(RectangleShape2D)
    ///   生成器从该矩形读取本地左右边界，依次将下一间房间的左边缘对齐上一间的右边缘。
    ///
    /// 注意：
    ///   若房间内有 top_level = true 的子节点（如 BattleArena、EnemySpawnManager），
    ///   它们使用世界坐标，不随父节点平移。生成器会自动为这些节点补偿偏移量。
    /// </summary>
    [GlobalClass]
    public partial class StageGeneratorManager : Node
    {
        [ExportCategory("Begin Room")]
        [Export] public Array<PackedScene> BeginPool { get; set; } = new();

        [ExportCategory("End Room")]
        [Export] public Array<PackedScene> EndPool { get; set; } = new();

        [ExportCategory("Middle Rooms — Easy")]
        [Export] public Array<PackedScene> EasyMiddlePool { get; set; } = new();
        [Export(PropertyHint.Range, "0,10,1")] public int EasyRoomMin { get; set; } = 0;
        [Export(PropertyHint.Range, "0,10,1")] public int EasyRoomMax { get; set; } = 0;

        [ExportCategory("Middle Rooms — Normal")]
        [Export] public Array<PackedScene> NormalMiddlePool { get; set; } = new();
        [Export(PropertyHint.Range, "0,10,1")] public int NormalRoomMin { get; set; } = 0;
        [Export(PropertyHint.Range, "0,10,1")] public int NormalRoomMax { get; set; } = 0;

        [ExportCategory("Middle Rooms — Hard")]
        [Export] public Array<PackedScene> HardMiddlePool { get; set; } = new();
        [Export(PropertyHint.Range, "0,10,1")] public int HardRoomMin { get; set; } = 0;
        [Export(PropertyHint.Range, "0,10,1")] public int HardRoomMax { get; set; } = 0;

        [ExportCategory("Generation")]
        /// <summary>随机种子，0 = 每次随机。</summary>
        [Export] public int RandomSeed { get; set; } = 0;  

        [ExportCategory("Camera Limits")]
        [Export] public int CameraLimitTop { get; set; } = -1500;
        [Export] public int CameraLimitBottom { get; set; } = 1500;

        [ExportCategory("Node Paths")]
        [Export] public NodePath WorldNodePath { get; set; } = new NodePath("../World");

        /// <summary>关卡生成完毕后触发。</summary>
        [Signal] public delegate void StageGeneratedEventHandler();

        public override void _Ready()
        {
            CallDeferred(MethodName.GenerateStage);
        }

        private void GenerateStage()
        {
            var world = GetNodeOrNull<Node2D>(WorldNodePath);
            if (world == null)
            {
                GD.PushError($"[StageGeneratorManager] 未找到 World 节点，路径：{WorldNodePath}");
                EmitSignal(SignalName.StageGenerated);
                return;
            }

            var rng = new RandomNumberGenerator();
            rng.Seed = RandomSeed != 0 ? (ulong)RandomSeed : (ulong)Time.GetTicksMsec();

            var roomScenes = BuildRoomSequence(rng);
            if (roomScenes.Count == 0)
            {
                GD.PushWarning("[StageGeneratorManager] 没有配置任何房间场景，跳过生成。");
                EmitSignal(SignalName.StageGenerated);
                return;
            }

            GameLogger.Info(nameof(StageGeneratorManager),
                $"开始生成关卡：{roomScenes.Count} 个房间（Begin + {roomScenes.Count - 2} 中间 + End）");

            float currentRightEdge = 0f;
            float stageLeft = float.MaxValue;
            float stageRight = float.MinValue;
            Node2D? playerSpawn = null;

            foreach (var scene in roomScenes)
            {
                var room = scene.Instantiate<Node2D>();
                world.AddChild(room);

                // 从 AreaSize 读取本地左右边界
                var (localLeft, localRight) = GetRoomLocalBounds(room);

                // 将本房间左边缘对齐上一房间右边缘
                float offsetX = currentRightEdge - localLeft;
                room.Position = new Vector2(offsetX, 0f);

                // top_level=true 的直接子节点不随父节点平移，需手动补偿
                OffsetTopLevelChildren(room, offsetX);

                // 取第一个 PlayerSpawnPoint 作为玩家起始点（仅 B_begin 应有）
                playerSpawn ??= room.GetNodeOrNull<Node2D>("PlayerSpawnPoint");

                float worldLeft  = offsetX + localLeft;
                float worldRight = offsetX + localRight;
                stageLeft  = Mathf.Min(stageLeft,  worldLeft);
                stageRight = Mathf.Max(stageRight, worldRight);
                currentRightEdge = worldRight;

                GameLogger.Info(nameof(StageGeneratorManager),
                    $"  {room.Name}: offsetX={offsetX:F0}，世界范围 [{worldLeft:F0}, {worldRight:F0}]");
            }

            // 更新相机全局边界
            var camMgr = GetParent()?.GetNodeOrNull<CameraZoneManager>("CameraZoneManager");
            if (camMgr != null)
            {
                camMgr.SetGlobalBounds((int)stageLeft, CameraLimitTop, (int)stageRight, CameraLimitBottom);
            }
            else
            {
                GD.PushWarning("[StageGeneratorManager] 未找到 CameraZoneManager，相机边界未更新。");
            }

            // 重定位玩家和同伴
            RepositionActors(world, playerSpawn, stageLeft);

            // 强制 NavigationServer 立即同步所有房间的导航网格
            // （各房间 AddChild + 设置 Position 后，NavigationServer 在下一物理帧才处理）
            // 调用此方法确保生成信号发出前导航数据已就绪
            NavigationServer2D.MapForceUpdate(GetViewport().FindWorld2D().NavigationMap);

            EmitSignal(SignalName.StageGenerated);

            GameLogger.Info(nameof(StageGeneratorManager),
                $"关卡生成完成：总宽度 {stageRight - stageLeft:F0}，X[{(int)stageLeft}, {(int)stageRight}]");
        }

        private List<PackedScene> BuildRoomSequence(RandomNumberGenerator rng)
        {
            var list = new List<PackedScene>();

            if (BeginPool.Count > 0)
                list.Add(BeginPool[rng.RandiRange(0, BeginPool.Count - 1)]);

            // Easy → Normal → Hard 顺序，每档数量在 Min/Max 间随机
            AppendRoomsFromPool(list, EasyMiddlePool,
                rng.RandiRange(EasyRoomMin, EasyRoomMax), rng);
            AppendRoomsFromPool(list, NormalMiddlePool,
                rng.RandiRange(NormalRoomMin, NormalRoomMax), rng);
            AppendRoomsFromPool(list, HardMiddlePool,
                rng.RandiRange(HardRoomMin, HardRoomMax), rng);

            if (EndPool.Count > 0)
                list.Add(EndPool[rng.RandiRange(0, EndPool.Count - 1)]);

            return list;
        }

        private static void AppendRoomsFromPool(List<PackedScene> list,
            Array<PackedScene> pool, int count, RandomNumberGenerator rng)
        {
            if (pool.Count == 0 || count <= 0) return;

            var available = new List<PackedScene>(pool);
            for (int i = 0; i < count; i++)
            {
                if (available.Count == 0)
                    available = new List<PackedScene>(pool);
                int idx = rng.RandiRange(0, available.Count - 1);
                list.Add(available[idx]);
                available.RemoveAt(idx);
            }
        }

        /// <summary>
        /// 从房间根节点的 AreaSize/CollisionShape2D 获取本地左右边界（相对于房间根节点）。
        /// </summary>
        private static (float left, float right) GetRoomLocalBounds(Node2D room)
        {
            var areaSize = room.GetNodeOrNull<Area2D>("AreaSize");
            if (areaSize != null)
            {
                var shape = areaSize.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                if (shape?.Shape is RectangleShape2D rect)
                {
                    float left  = shape.Position.X - rect.Size.X / 2f;
                    float right = shape.Position.X + rect.Size.X / 2f;
                    return (left, right);
                }
            }

            GD.PushWarning($"[StageGeneratorManager] 房间 {room.Name} 缺少 AreaSize/CollisionShape2D(RectangleShape2D)，使用默认宽度 5000。");
            return (-2500f, 2500f);
        }

        /// <summary>
        /// 递归查找所有 TopLevel=true 的节点并手动追加世界偏移。
        /// TopLevel 节点不随父节点移动，父节点 Position 改变后需手动补偿。
        /// 注意：需要递归处理，因为 TopLevel 节点可能嵌套在普通子节点内（如 WaveSpawnManager/TriggerArea）。
        /// </summary>
        private static void OffsetTopLevelChildren(Node room, float offsetX)
        {
            if (Mathf.IsZeroApprox(offsetX)) return;

            OffsetTopLevelRecursive(room, offsetX);
        }

        private static void OffsetTopLevelRecursive(Node parent, float offsetX)
        {
            foreach (var child in parent.GetChildren())
            {
                if (child is Node2D node2D && node2D.TopLevel)
                {
                    // TopLevel 节点使用世界坐标，直接补偿偏移
                    node2D.GlobalPosition += new Vector2(offsetX, 0f);
                    // TopLevel 节点的子树跟随自身移动（除非子节点也是 TopLevel），继续向下递归
                }
                // 不管当前节点是否 TopLevel，都继续向下找嵌套的 TopLevel 节点
                OffsetTopLevelRecursive(child, offsetX);
            }
        }

        private void RepositionActors(Node2D world, Node2D? spawnPoint, float stageLeft)
        {
            // 若场景中没有 PlayerSpawnPoint，默认放在关卡左边缘右侧 1500 处
            var target = spawnPoint?.GlobalPosition ?? new Vector2(stageLeft + 1500f, 200f);

            var player = world.GetNodeOrNull<Node2D>("MainCharacter");
            if (player != null)
            {
                player.GlobalPosition = target;
                GameLogger.Info(nameof(StageGeneratorManager), $"玩家重定位 → {target}");
            }

            // P2 同伴稍微偏右
            var p2 = world.GetNodeOrNull<Node2D>("P2");
            if (p2 != null)
                p2.GlobalPosition = target + new Vector2(200f, 0f);
        }
    }
}

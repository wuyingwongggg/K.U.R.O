using Godot;

namespace Kuros.Core
{
    /// <summary>
    /// 瞄准点解析器（A_010 坐标寻址）：把"指定位置"从鼠标抽象为**设备无关**的瞄准点。
    /// 按最后输入的设备自动选择来源（鼠标为主，手柄/键盘回退），所有来源统一钳制在 MaxRange 内；
    /// 最差（无指针）回退"玩家朝向 × 最大距离"，保证任何输入设备下都有效。
    /// 常驻组件：由卡效果创建（存在即启用），预览可查询它以显示放置标记。
    /// </summary>
    public partial class AimPointResolver : Node
    {
        public const string NodeName = "AimPointResolver";

        public static AimPointResolver GetOrCreate(GameActor player)
        {
            var existing = player.GetNodeOrNull<AimPointResolver>(NodeName);
            if (existing != null) return existing;

            var resolver = new AimPointResolver { Name = NodeName };
            player.AddChild(resolver);
            return resolver;
        }

        public static AimPointResolver? Find(GameActor player)
            => player.GetNodeOrNull<AimPointResolver>(NodeName);

        private enum Device { Mouse, Keyboard, Gamepad }

        // 设备判定:鼠标/手柄"最近输入者胜"**永久保持**,键盘**不参与切换**——
        // 键鼠同用时(WASD 持续产生按键回显、鼠标随时移动)若让键盘参与则永远来回切;
        // 也不做时间窗:鼠标静止数秒不应回退(否则停手片刻再放置会又滑回"朝向×距离")。
        private Device _lastDevice = Device.Mouse;
        private bool _hasPointerActivity;      // 是否见过鼠标/手柄输入(没有则全程键盘回退)

        /// <summary>最大瞄准距离（px）：**仅作手柄回退的基准距离**（摇杆量程折算远近）；
        /// 鼠标/键盘瞄准不做钳制——落点即为指示位置（卡配置的 AimRange 注入此值）。</summary>
        public float MaxRange { get; set; } = 600f;

        /// <summary>手柄摇杆死区：低于该量程视为中立（回退朝向瞄准）。</summary>
        [Export(PropertyHint.Range, "0.1,0.9,0.05")] public float StickDeadzone { get; set; } = 0.3f;

        private GameActor _player = null!;

        public override void _Ready()
        {
            _player = GetParent<GameActor>();
        }

        public override void _Input(InputEvent @event)
        {
            switch (@event)
            {
                case InputEventMouseMotion:
                case InputEventMouseButton:
                    _lastDevice = Device.Mouse;
                    _hasPointerActivity = true;
                    break;
                case InputEventJoypadButton:
                case InputEventJoypadMotion:
                    _lastDevice = Device.Gamepad;
                    _hasPointerActivity = true;
                    break;
                // 键盘不切换设备:仅当从未见过鼠标/手柄输入时,才用键盘回退瞄准
            }
        }

        /// <summary>当前应使用的设备(确定性,无时间窗):未见指针输入→键盘回退;否则最近输入者胜。</summary>
        private Device CurrentDevice()
            => _hasPointerActivity ? _lastDevice : Device.Keyboard;

        /// <summary>当前设备对应的瞄准世界点（始终可用：最差回退"朝向×最大距离"）。
        /// 鼠标/键盘源不做距离钳制——指哪落哪。</summary>
        public bool TryGetAimWorldPoint(out Vector2 point)
        {
            point = Vector2.Zero;
            if (_player == null || !GodotObject.IsInstanceValid(_player)) return false;

            Vector2 playerPos = _player.GlobalPosition;
            point = CurrentDevice() switch
            {
                Device.Mouse => _player.GetGlobalMousePosition(),
                Device.Gamepad => GamepadAim(playerPos),
                _ => FacingAim(playerPos),
            };
            return true;
        }

        /// <summary>回退瞄准：玩家朝向 × 最大距离（键盘/无指针设备；与投掷轨迹预览的落点体系一致）。</summary>
        private Vector2 FacingAim(Vector2 playerPos)
            => playerPos + (_player.FacingRight ? Vector2.Right : Vector2.Left) * MaxRange;

        /// <summary>手柄瞄准：右摇杆方向 + 量程按 MaxRange 折算距离；中立回退朝向。</summary>
        private Vector2 GamepadAim(Vector2 playerPos)
        {
            var pads = Input.GetConnectedJoypads();
            if (pads.Count == 0) return FacingAim(playerPos);

            int pad = pads[0];
            Vector2 stick = new(Input.GetJoyAxis(pad, JoyAxis.RightX),
                                Input.GetJoyAxis(pad, JoyAxis.RightY));
            if (stick.Length() < StickDeadzone) return FacingAim(playerPos);

            float distance = MaxRange * Mathf.Clamp(stick.Length(), StickDeadzone, 1f);
            return playerPos + stick.Normalized() * distance;
        }

    }
}

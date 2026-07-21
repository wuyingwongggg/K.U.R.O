using Godot;

namespace Kuros.Effects
{
    [GlobalClass]
    public partial class SceneSpawnEntry : Resource
    {
        [Export] public PackedScene? Scene { get; set; }
        [Export] public Vector2 Position { get; set; }
    }
}

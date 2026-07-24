using Godot;

namespace Kuros.Effects
{
    [GlobalClass]
    public partial class FireWall : Node2D
    {
        [Export] public Godot.Collections.Array<SceneSpawnEntry> Segments { get; set; } = new();

        public override void _Ready()
        {
            CallDeferred(nameof(SpawnSegments));
        }

        private void SpawnSegments()
        {
            var parent = GetParent();
            foreach (var seg in Segments)
            {
                if (seg?.Scene == null) continue;
                var instance = seg.Scene.Instantiate();
                parent.AddChild(instance);
                if (instance is Node2D n)
                {
                    n.GlobalPosition = GlobalPosition + seg.Position * Scale;
                    n.Scale *= Scale;
                }
            }
        }
    }
}

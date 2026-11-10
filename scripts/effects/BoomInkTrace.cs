using Godot;

namespace Kuros.Fx;

public partial class BoomInkTrace : FadeInOutDestroy
{
    [Export] public Texture2D? AlternateMask { get; set; }

    public override void _Ready()
    {
        if (AlternateMask != null && GD.Randf() < 0.5f) Texture = AlternateMask;
        FlipH = GD.Randf() < 0.5f;
        if (Material is ShaderMaterial source)
        {
            var variant = (ShaderMaterial)source.Duplicate();
            float sizeMultiplier = (float)GD.RandRange(0.91, 1.06);
            variant.SetShaderParameter("stretch", Vector2.One * sizeMultiplier);
            Material = variant;
        }
        base._Ready();
    }
}

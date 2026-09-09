using System;
using Godot;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// A_007 复制件的"乱码块"视觉装饰器(独立于 ThrowCoreEffect 生成管线)。
    /// 注意:项目渲染器为 gl_compatibility,CanvasGroup 组内合成不支持,
    /// 因此采用**逐 Sprite 材质滤镜**(同一贴图素材,整件含描边一并损坏)。
    /// 规则:
    ///   1. 仅视觉 Sprite 含 assets/furnitures 真实贴图的家具才应用;
    ///      cube 型(程序字符面、贴图仅占位)保持原样。
    ///   2. 影子(Shadow)不参与;物理/区域节点无贴图天然跳过。
    ///   3. 同一件所有艺术 Sprite 共享一个材质实例与 seed → 故障同源同步。
    /// 时序:须在件实体 _Ready 之后调用(实体已缓存节点引用,替换材质不破坏)。
    /// </summary>
    public static class PieceCopyGlitchDecorator
    {
        private static Shader? _glitchShader;
        private static bool _shaderLoadFailedLogged;

        /// <returns>true = 已应用滤镜;false = 不适用或加载失败。</returns>
        public static bool Apply(Node2D piece)
        {
            if (piece == null || !GodotObject.IsInstanceValid(piece)) return false;

            if (!TryFindVisualContainer(piece, out var container))
            {
                GD.Print($"[PieceCopyGlitchDecorator] {piece.Name} 跳过滤镜:找不到视觉容器(RigidBody2D)");
                return false;
            }

            var shader = LoadGlitchShader();
            if (shader == null)
            {
                if (!_shaderLoadFailedLogged)
                {
                    _shaderLoadFailedLogged = true;
                    GD.PrintErr("[PieceCopyGlitchDecorator] 无法加载 furniture_glitch.gdshader,滤镜未应用");
                }
                return false;
            }

            // 同一件共用一个材质:故障同源同步(不同件 seed 不同 → 不同步)
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("seed", GD.RandRange(0.0, 100.0));

            int applied = 0;
            foreach (Node node in container.FindChildren("*", "Sprite2D", recursive: true, owned: false))
            {
                if (node is not Sprite2D sprite || sprite.Name == "Shadow") continue;
                if (!IsFurnitureArtTexture(sprite.Texture)) continue;

                sprite.Material = material;
                applied++;
            }

            if (applied == 0)
            {
                GD.Print($"[PieceCopyGlitchDecorator] {piece.Name} 跳过滤镜:无真实家具贴图(cube 型程序面或占位贴图)");
                return false;
            }

            GD.Print($"[PieceCopyGlitchDecorator] {piece.Name} 已应用乱码滤镜({applied} 个艺术 Sprite)");
            return true;
        }

        // ── 内部辅助 ─────────────────────────────────────────────

        private static bool TryFindVisualContainer(Node2D piece, out Node2D container)
        {
            var body = piece.GetNodeOrNull<RigidBody2D>("RigidBody2D");
            if (body == null)
            {
                foreach (var child in piece.GetChildren())
                {
                    if (child is RigidBody2D rb) { body = rb; break; }
                }
            }
            if (body != null)
            {
                container = body;
                return true;
            }
            container = piece; // 兜底:根即视觉容器
            return piece.GetChildCount() > 0;
        }

        private static bool IsFurnitureArtTexture(Texture2D? texture)
        {
            var path = texture?.ResourcePath;
            return path != null && path.Contains("/furnitures/", StringComparison.OrdinalIgnoreCase);
        }

        private static Shader? LoadGlitchShader()
        {
            _glitchShader ??= GD.Load<Shader>("res://shaders/materials/furniture_glitch.gdshader");
            return _glitchShader;
        }
    }
}

using System;
using Godot;

namespace Kuros.Fx
{
    /// <summary>
    /// A_007 复制件的"乱码块"视觉装饰器(中立视觉层,无 build 依赖,heroes/items 皆可调用)。
    /// 逐 Sprite 材质滤镜(gl_compatibility 无 CanvasGroup 合成,逐 sprite 是兼容做法),
    /// 同一件所有艺术 Sprite 共享一个材质实例与 seed → 故障同源同步。
    /// 规则:
    ///   1. requireFurnitureArt=true(默认):仅 assets/furnitures 真实贴图 Sprite 参与(cube 型跳过);
    ///   2. requireFurnitureArt=false:任何带贴图的 Sprite 都参与(举起 Icon 回退等场景,纹理路径不固定)。
    ///   3. 影子(Shadow)不参与。
    /// </summary>
    public static class PieceCopyGlitchDecorator
    {
        private static Shader? _glitchShader;
        private static bool _shaderLoadFailedLogged;

        /// <returns>true = 已应用滤镜;false = 不适用或加载失败。</returns>
        public static bool Apply(Node2D piece, bool requireFurnitureArt = true)
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

            int applied = ApplyToSprite(container, material, container is Sprite2D selfSprite
                ? selfSprite : null, requireFurnitureArt);

            if (applied == 0)
            {
                GD.Print($"[PieceCopyGlitchDecorator] {piece.Name} 跳过滤镜:无匹配贴图 Sprite(requireFurnitureArt={requireFurnitureArt})");
                return false;
            }

            GD.Print($"[PieceCopyGlitchDecorator] {piece.Name} 已应用乱码滤镜({applied} 个 Sprite)");
            return true;
        }

        private static int ApplyToSprite(Node2D root, ShaderMaterial material,
            Sprite2D? includeRootSprite, bool requireFurnitureArt)
        {
            int applied = 0;

            // 根自身也是 Sprite(图标回退等无子结构的视觉)
            if (includeRootSprite != null
                && IsTargetSprite(includeRootSprite, requireFurnitureArt))
            {
                includeRootSprite.Material = material;
                applied++;
            }

            foreach (Node node in root.FindChildren("*", "Sprite2D", recursive: true, owned: false))
            {
                if (node is not Sprite2D sprite || sprite.Name == "Shadow") continue;
                if (!IsTargetSprite(sprite, requireFurnitureArt)) continue;
                sprite.Material = material;
                applied++;
            }

            return applied;
        }

        private static bool IsTargetSprite(Sprite2D sprite, bool requireFurnitureArt)
        {
            if (sprite.Texture == null) return false;
            if (!requireFurnitureArt) return true;

            var path = sprite.Texture.ResourcePath;
            return path != null && path.Contains("/furnitures/", StringComparison.OrdinalIgnoreCase);
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
            container = piece; // 兜底:根即视觉容器(图标回退等无子结构也适用)
            return true;
        }

        private static Shader? LoadGlitchShader()
        {
            _glitchShader ??= GD.Load<Shader>("res://shaders/materials/furniture_glitch.gdshader");
            return _glitchShader;
        }
    }
}

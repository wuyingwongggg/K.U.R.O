using System;
using Godot;
using Kuros.Core;
using Kuros.Fx;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 玩家次数盾控制器(挂在玩家身上的常驻组件):护盾次数 + "1 秒免疫窗口"语义——
    /// 首次受击开启窗口(伤害全免),窗口内受击不重复消耗,窗口结束才扣 1 次;次数耗尽自动清除。
    /// 多来源共享与覆盖:ThrowShieldTransformEffect(A_009)与 P2 护盾都经 <see cref="Apply"/> 施加,
    /// 后施加者覆盖前者(次数/窗口/视觉/配色一体覆盖),互相不叠加。
    /// 视觉:ShieldRingEffect(hexagon shader)——窗口内按进度渐变到"消耗后档位"(最后一格渐隐),
    /// 受击叠加红闪混合;颜色走 shader 参数 Color_Shield(Modulate 对 hexagon 无效)。
    /// 事件:ChargesChanged(次数变化)、Blocked(格挡成功,参数=被免掉的伤害)、Depleted(耗尽)。
    /// </summary>
    public partial class ChargeShieldController : Node
    {
        public const string NodeName = "ChargeShield";

        public static ChargeShieldController GetOrCreate(GameActor player)
        {
            var existing = player.GetNodeOrNull<ChargeShieldController>(NodeName);
            if (existing != null) return existing;

            var controller = new ChargeShieldController { Name = NodeName };
            player.AddChild(controller);
            return controller;
        }

        public static ChargeShieldController? Find(GameActor player)
            => player.GetNodeOrNull<ChargeShieldController>(NodeName);

        public event Action<int>? ChargesChanged;
        public event Action<int>? Blocked;   // 参数=本次被免掉的伤害
        public event Action? Depleted;

        public int Charges => _charges;
        public bool IsActive => _charges > 0;
        public float WindowRemaining => _windowRemaining;

        private GameActor _player = null!;
        private object? _owner;
        private int _charges;
        private float _windowRemaining, _windowTotal;
        private float _flashRemaining, _flashDuration;
        private Color _flashColor = new(1f, 0.42f, 0.42f, 1f);
        private float _immunitySeconds = 1f, _popScale = 1.12f;

        private PackedScene? _visualScene;
        private Node2D? _visual;
        private Sprite2D? _ringSprite;
        private ShaderMaterial? _material;
        private Color[]? _tierColors; // null = 保留场景既定配色(P2)
        private Color _baseColor = Colors.White, _windowStartColor, _windowTargetColor;
        private float _baseAlpha = 1f, _windowStartAlpha, _windowTargetAlpha;
        private Vector2 _baseScale = Vector2.One;
        private Tween? _popTween;

        public override void _Ready()
        {
            _player = GetParent<GameActor>();
            _player.DamageIntercepted += OnDamageIntercepted;
        }

        public override void _ExitTree()
        {
            if (_player != null && GodotObject.IsInstanceValid(_player))
                _player.DamageIntercepted -= OnDamageIntercepted;
            FreeVisual();
        }

        /// <summary>施加/覆盖护盾(次数、窗口时长、视觉与配色一体覆盖)。</summary>
        public void Apply(object owner, int charges, PackedScene? visualScene, Color[]? tierColors,
            float immunitySeconds, Color flashColor, float flashDuration, float popScale)
        {
            _owner = owner;
            _charges = Mathf.Max(1, charges);
            _immunitySeconds = Mathf.Max(0.1f, immunitySeconds);
            _flashColor = flashColor;
            _flashDuration = Mathf.Max(0f, flashDuration);
            _popScale = Mathf.Max(1f, popScale);
            _windowRemaining = 0f;
            _flashRemaining = 0f;

            EnsureVisual(visualScene, tierColors);
            ApplyChargesVisual(_charges);
            ChargesChanged?.Invoke(_charges);
        }

        /// <summary>来源移除时清除(仅当当前护盾属于该来源,防误删他人覆盖的护盾)。</summary>
        public void ClearIfOwned(object owner)
        {
            if (!ReferenceEquals(_owner, owner)) return;
            Clear();
        }

        public void Clear()
        {
            _owner = null;
            _charges = 0;
            _windowRemaining = 0f;
            _flashRemaining = 0f;
            FreeVisual();
            ChargesChanged?.Invoke(0);
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            if (_flashRemaining > 0f)
                _flashRemaining = Mathf.Max(0f, _flashRemaining - dt);

            if (_charges <= 0) return;

            if (_windowRemaining <= 0f)
            {
                if (_flashRemaining > 0f) UpdateColor(0f); // 窗口已结束但红闪未尽
                return;
            }

            _windowRemaining -= dt;
            float progress = Mathf.Clamp(1f - _windowRemaining / Mathf.Max(_windowTotal, 0.0001f), 0f, 1f);
            UpdateColor(progress);

            if (_windowRemaining > 0f) return;

            _windowRemaining = 0f;
            _charges--;
            ChargesChanged?.Invoke(_charges);
            if (_charges <= 0)
            {
                Clear();
                Depleted?.Invoke();
                return;
            }
            ApplyChargesVisual(_charges);
        }

        // ── 格挡结算 ────────────────────────────────────────────

        private bool OnDamageIntercepted(GameActor.DamageEventArgs args)
        {
            if (_charges <= 0 || args.Target != _player || args.Damage <= 0) return false;

            int blocked = args.Damage;
            args.Damage = 0;
            args.IsBlocked = true;

            // 首次受击开启窗口;窗口内受击只享受免疫,不额外消耗
            if (_windowRemaining <= 0f)
            {
                _windowRemaining = _immunitySeconds;
                _windowTotal = _windowRemaining;
                _windowStartColor = _baseColor;
                _windowStartAlpha = _baseAlpha;
                if (_charges - 1 <= 0)
                {
                    _windowTargetColor = _baseColor; // 最后一格:窗口内渐隐
                    _windowTargetAlpha = 0f;
                }
                else
                {
                    Color next = TierColorOf(_charges - 1);
                    _windowTargetColor = new Color(next.R, next.G, next.B, 1f);
                    _windowTargetAlpha = next.A;
                }
            }

            Blocked?.Invoke(blocked);
            TriggerBlockFlash();
            return true;
        }

        private void TriggerBlockFlash()
        {
            if (_ringSprite == null || !IsInstanceValid(_ringSprite)) return;

            if (_flashDuration > 0f)
                _flashRemaining = _flashDuration;

            if (_popScale > 1.001f)
            {
                if (_popTween != null && _popTween.IsRunning())
                    _popTween.Kill();

                _popTween = CreateTween();
                Vector2 pop = _baseScale * _popScale;
                float half = Mathf.Max(0.03f, _flashDuration * 0.5f);
                _popTween.TweenProperty(_ringSprite, "scale", pop, half);
                _popTween.TweenProperty(_ringSprite, "scale", _baseScale, half).SetDelay(half);
            }

            UpdateColor(0f); // 立即闪一次(不等下一帧)
        }

        // ── 视觉(ShieldRingEffect;颜色走 shader 参数)──────────

        private void EnsureVisual(PackedScene? scene, Color[]? tierColors)
        {
            _tierColors = tierColors;

            if (scene == null)
            {
                FreeVisual();
                return;
            }

            // 同场景复用实例(避免重复转化时闪一次淡入);换场景则重建(覆盖语义)
            if (_visual != null && IsInstanceValid(_visual) && _visualScene == scene)
            {
                CacheSprite();
                return;
            }

            FreeVisual();
            _visualScene = scene;
            _visual = scene.Instantiate<Node2D>();

            // FadeInOutDestroy 会在入树前缓存总时长:必须在 AddChild 之前覆写为常驻
            var fade = _visual.GetNodeOrNull<Node>("Ring");
            if (fade != null)
            {
                fade.Set("HoldDuration", 999999f);
                fade.Set("FadeOutDuration", 0f);
            }

            VisualAnchorAttach.Attach(_visual, _player);
            CacheSprite();

            // 无档位配色(P2):保留场景既定颜色与透明度为基准
            if (_tierColors == null && _material != null)
            {
                _baseColor = _material.GetShaderParameter("Color_Shield").AsColor();
                _baseColor.A = 1f;
                _baseAlpha = _material.GetShaderParameter("Opaticy").AsSingle();
            }
        }

        private void CacheSprite()
        {
            if (_visual == null || !IsInstanceValid(_visual)) return;

            if (_ringSprite == null || !IsInstanceValid(_ringSprite))
            {
                _ringSprite = _visual.GetNodeOrNull<Sprite2D>("Ring")
                    ?? _visual.FindChild("Ring", recursive: true, owned: false) as Sprite2D;
                if (_ringSprite != null)
                    _baseScale = _ringSprite.Scale;
            }
            if (_ringSprite == null) return;

            if (_material == null || _ringSprite.Material != _material)
            {
                if (_ringSprite.Material is not ShaderMaterial shared) return;
                _material = (ShaderMaterial)shared.Duplicate();
                _ringSprite.Material = _material;
            }
        }

        private void ApplyChargesVisual(int level)
        {
            if (_material == null) return;

            if (_tierColors != null && _tierColors.Length > 0)
            {
                Color tint = TierColorOf(level);
                _baseColor = new Color(tint.R, tint.G, tint.B, 1f);
                _baseAlpha = tint.A;
                _material.SetShaderParameter("Color_Shield", _baseColor);
                _material.SetShaderParameter("Opaticy", _baseAlpha);
            }
            else
            {
                // 场景既定配色:仅落透明度(窗口渐变的起点)
                _material.SetShaderParameter("Color_Shield", _baseColor);
                _material.SetShaderParameter("Opaticy", _baseAlpha);
            }
        }

        private void UpdateColor(float progress)
        {
            if (_material == null) return;

            Color color = _windowStartColor.Lerp(_windowTargetColor, progress);
            float alpha = Mathf.Lerp(_windowStartAlpha, _windowTargetAlpha, progress);

            if (_flashRemaining > 0f && _flashDuration > 0f)
            {
                float flashT = Mathf.Clamp(_flashRemaining / _flashDuration, 0f, 1f);
                color = color.Lerp(_flashColor, flashT);
            }

            _material.SetShaderParameter("Color_Shield", new Color(color.R, color.G, color.B, 1f));
            _material.SetShaderParameter("Opaticy", alpha);
        }

        private Color TierColorOf(int level)
            => _tierColors![Mathf.Clamp(level, 1, _tierColors.Length) - 1];

        private void FreeVisual()
        {
            if (_popTween != null && _popTween.IsRunning())
                _popTween.Kill();
            _popTween = null;

            if (_visual != null && GodotObject.IsInstanceValid(_visual))
                _visual.QueueFree();
            _visual = null;
            _ringSprite = null;
            _material = null;
            _visualScene = null;
            _baseScale = Vector2.One;
        }
    }
}

using Godot;

namespace Kuros.Effects
{
	/// <summary>
	/// 全屏数字干扰（纯视觉 · EFFECT_STANDARD「全局反馈」）：由攻击特效生成，自管理时长。
	/// 子 CanvasLayer 铺满视口并采样 screen_texture；不参与伤害/碰撞/锁定。
	/// 强度按**自身 Duration** 衰减：FadeIn 快速起量 → pow(1-t, DecayExponent) 平滑归零，
	/// 播放结束自毁（暂停时也继续计时，避免全屏层冻在暂停菜单上）。
	/// </summary>
	[GlobalClass]
	public partial class ScreenInterferenceEffect : Node2D
	{
		[ExportCategory("Timing 时长")]
		/// <summary>总时长（秒）。默认 2.4 = RogueAISlamB 的 ActiveDuration，干扰随攻击一起收尾。</summary>
		[Export(PropertyHint.Range, "0.1,10,0.05")] public float Duration { get; set; } = 2.4f;
		/// <summary>起量时间（秒）：从 0 拉到 MaxStrength，避免第一帧突然全屏故障。</summary>
		[Export(PropertyHint.Range, "0,1,0.01")] public float FadeInDuration { get; set; } = 0.1f;
		/// <summary>峰值干扰强度（0~1）。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float MaxStrength { get; set; } = 1f;
		/// <summary>衰减曲线指数：1 = 线性，越大后期衰减越快（默认 1.3，前段保持、末段快速归零）。</summary>
		[Export(PropertyHint.Range, "0.2,4,0.1")] public float DecayExponent { get; set; } = 1.3f;

		private ColorRect? _rect;
		private ShaderMaterial? _material;
		private float _elapsed;
		private bool _finished;

		public override void _Ready()
		{
			ProcessMode = ProcessModeEnum.Always;

			_rect = GetNodeOrNull<ColorRect>("InterferenceLayer/InterferenceRect");
			if (_rect?.Material is not ShaderMaterial shared)
			{
				GD.PushWarning($"[{nameof(ScreenInterferenceEffect)}] 缺少 InterferenceRect/ShaderMaterial，特效自毁");
				QueueFree();
				return;
			}

			// 每个实例独立材质：避免多个干扰实例共享 interference_strength
			_material = (ShaderMaterial)shared.Duplicate();
			_rect.Material = _material;
			_material.SetShaderParameter("interference_strength", 0f);
			_rect.Visible = false;
		}

		public override void _Process(double delta)
		{
			if (_finished || _rect == null || _material == null) return;

			_elapsed += (float)delta;
			if (_elapsed >= Duration)
			{
				_finished = true;
				_material.SetShaderParameter("interference_strength", 0f);
				_rect.Visible = false;
				QueueFree();
				return;
			}

			float t = Mathf.Clamp(_elapsed / Mathf.Max(Duration, 0.001f), 0f, 1f);
			float fadeIn = 1f;
			if (FadeInDuration > 0.001f)
			{
				float f = Mathf.Clamp(_elapsed / FadeInDuration, 0f, 1f);
				fadeIn = f * f * (3f - 2f * f);   // smoothstep
			}

			float strength = MaxStrength * fadeIn * Mathf.Pow(1f - t, Mathf.Max(DecayExponent, 0.01f));
			_rect.Visible = strength > 0.001f;
			_material.SetShaderParameter("interference_strength", strength);
		}
	}
}

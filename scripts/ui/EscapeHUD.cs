using System;
using Godot;

namespace Kuros.UI
{
	public partial class EscapeHUD : Control
	{
		[Export] public float FlashDuration = 0.2f;
		[Export] public Color LeftHighlightColor = new Color(0.5f, 0.75f, 1.0f, 1.0f);
		[Export] public Color RightHighlightColor = new Color(1.0f, 0.8f, 0.35f, 1.0f);

		private Control? _overlay;
		private ProgressBar? _timerBar;
		private Label? _timerLabel;
		private Button? _leftButton;
		private Button? _rightButton;
		private Label? _leftCounter;
		private Label? _rightCounter;

		private int _requiredLeft;
		private int _requiredRight;
		private int _currentLeft;
		private int _currentRight;
		private float _sequenceDuration;
		private float _timeRemaining;
		private bool _sequenceActive;
		private float _leftFlashTimer;
		private float _rightFlashTimer;
		private Color _leftBaseColor = Colors.White;
		private Color _rightBaseColor = Colors.White;

		public override void _Ready()
		{
			_overlay = GetNodeOrNull<Control>("Overlay");
			_timerBar = GetNodeOrNull<ProgressBar>("Overlay/CenterContainer/Panel/VBox/TimerBar");
			_timerLabel = GetNodeOrNull<Label>("Overlay/CenterContainer/Panel/VBox/TimerLabel");
			_leftButton = GetNodeOrNull<Button>("Overlay/CenterContainer/Panel/VBox/HBox/LeftBox/ButtonLeft");
			_rightButton = GetNodeOrNull<Button>("Overlay/CenterContainer/Panel/VBox/HBox/RightBox/ButtonRight");
			_leftCounter = GetNodeOrNull<Label>("Overlay/CenterContainer/Panel/VBox/HBox/LeftBox/LeftCounter");
			_rightCounter = GetNodeOrNull<Label>("Overlay/CenterContainer/Panel/VBox/HBox/RightBox/RightCounter");

			_leftBaseColor = _leftButton?.Modulate ?? Colors.White;
			_rightBaseColor = _rightButton?.Modulate ?? Colors.White;

			HideSequenceImmediate();
		}

		public override void _Process(double delta)
		{
			if (!_sequenceActive)
			{
				return;
			}

			UpdateFlash(_leftButton, ref _leftFlashTimer, _leftBaseColor, delta);
			UpdateFlash(_rightButton, ref _rightFlashTimer, _rightBaseColor, delta);
		}

		public void ShowSequence(int requiredLeftInputs, int requiredRightInputs, float escapeDuration)
		{
			_requiredLeft = Math.Max(1, requiredLeftInputs);
			_requiredRight = Math.Max(1, requiredRightInputs);
			_sequenceDuration = Mathf.Max(escapeDuration, 0.01f);
			_timeRemaining = _sequenceDuration;
			_currentLeft = 0;
			_currentRight = 0;
			_sequenceActive = true;
			SetProcess(true);
			SetHudVisibility(true);
			UpdateUI();
		}

		public void UpdateSequence(int leftCount, int rightCount, float timeRemaining)
		{
			if (!_sequenceActive)
			{
				return;
			}

			leftCount = Mathf.Clamp(leftCount, 0, _requiredLeft);
			rightCount = Mathf.Clamp(rightCount, 0, _requiredRight);
			_timeRemaining = Mathf.Max(timeRemaining, 0f);

			if (leftCount > _currentLeft)
			{
				TriggerFlash(_leftButton, ref _leftFlashTimer, LeftHighlightColor);
			}

			if (rightCount > _currentRight)
			{
				TriggerFlash(_rightButton, ref _rightFlashTimer, RightHighlightColor);
			}

			_currentLeft = leftCount;
			_currentRight = rightCount;
			UpdateUI();
		}

		public void HideSequence()
		{
			_sequenceActive = false;
			_currentLeft = 0;
			_currentRight = 0;
			_timeRemaining = 0f;
			ResetFlashState();
			SetProcess(false);
			SetHudVisibility(false);
		}

		private void UpdateUI()
		{
			if (_leftCounter != null)
			{
				_leftCounter.Text = $"{_currentLeft}/{_requiredLeft}";
			}

			if (_rightCounter != null)
			{
				_rightCounter.Text = $"{_currentRight}/{_requiredRight}";
			}

			float ratio = _sequenceDuration <= Mathf.Epsilon
				? 0f
				: Mathf.Clamp(_timeRemaining / _sequenceDuration, 0f, 1f);

			if (_timerBar != null)
			{
				_timerBar.Value = ratio;
			}

			if (_timerLabel != null)
			{
				_timerLabel.Text = $"剩余 {Mathf.Round(_timeRemaining * 10f) / 10f:0.0}s";
			}

			if (_leftButton != null)
			{
				_leftButton.Disabled = _currentLeft >= _requiredLeft;
			}

			if (_rightButton != null)
			{
				_rightButton.Disabled = _currentRight >= _requiredRight;
			}
		}

		private void TriggerFlash(Button? button, ref float timer, Color targetColor)
		{
			if (button == null)
			{
				return;
			}

			timer = FlashDuration;
			button.Modulate = targetColor;
		}

		private static void UpdateFlash(Button? button, ref float timer, Color baseColor, double delta)
		{
			if (button == null || timer <= 0f)
			{
				if (button != null)
				{
					button.Modulate = baseColor;
				}
				return;
			}

			timer -= (float)delta;
			if (timer <= 0f)
			{
				button.Modulate = baseColor;
			}
		}

		private void SetHudVisibility(bool show)
		{
			if (_overlay != null)
			{
				_overlay.Visible = show;
			}
			Visible = show;
		}

		private void ResetFlashState()
		{
			_leftFlashTimer = 0f;
			_rightFlashTimer = 0f;
			if (_leftButton != null)
			{
				_leftButton.Modulate = _leftBaseColor;
			}

			if (_rightButton != null)
			{
				_rightButton.Modulate = _rightBaseColor;
			}
		}

		private void HideSequenceImmediate()
		{
			_sequenceActive = false;
			ResetFlashState();
			SetHudVisibility(false);
			SetProcess(false);
		}
	}
}

using Godot;
using Polytoria.Client.Settings;
using Polytoria.Shared.Settings;
using System.Globalization;

namespace Polytoria.Client.UI;

public sealed partial class SliderSettingField : HBoxContainer
{
	public SettingDef Definition = null!;
	private HSlider _slider = null!;
	private Label _valueLabel = null!;
	private float _step = 1f;
	private System.Action<SettingChangedEvent>? _changedHandler;

	public override void _Ready()
	{
		_slider = new HSlider
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter
		};
		AddChild(_slider);

		_valueLabel = new Label
		{
			CustomMinimumSize = new Vector2(64, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center
		};
		AddChild(_valueLabel);

		if (Definition is SettingDef<float> f)
		{
			_slider.MinValue = f.MinValue;
			_slider.MaxValue = f.MaxValue;
			_slider.Step = f.Step;
			_slider.Value = ClientSettingsService.Instance.Get<float>(Definition.Key);
			_step = Mathf.IsZeroApprox(f.Step) ? 1f : f.Step;
			UpdateValueLabel((float)_slider.Value);
		}
		else if (Definition is SettingDef<int> i)
		{
			_slider.MinValue = i.MinValue;
			_slider.MaxValue = i.MaxValue;
			_slider.Step = i.Step;
			_slider.Value = ClientSettingsService.Instance.Get<int>(Definition.Key);
			_step = Mathf.IsZeroApprox(i.Step) ? 1f : i.Step;
			UpdateValueLabel((float)_slider.Value);
		}

		_slider.ValueChanged += (value) =>
		{
			UpdateValueLabel((float)value);
			ClientSettingsService.Instance.Set(Definition.Key, value);
		};

		_changedHandler = e =>
		{
			if (e.Key == Definition.Key && e.NewValue is float f)
			{
				_slider.SetValueNoSignal(f);
				UpdateValueLabel(f);
			}
		};
		ClientSettingsService.Instance.Changed += _changedHandler;

		base._Ready();
	}

	public override void _ExitTree()
	{
		if (_changedHandler != null && ClientSettingsService.Instance != null)
		{
			ClientSettingsService.Instance.Changed -= _changedHandler;
			_changedHandler = null;
		}

		base._ExitTree();
	}

	private void UpdateValueLabel(float value)
	{
		_valueLabel.Text = FormatValue(value);
	}

	private string FormatValue(float value)
	{
		if (Mathf.IsEqualApprox(_step, 1f))
		{
			return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
		}

		return value.ToString("0.###", CultureInfo.InvariantCulture);
	}
}

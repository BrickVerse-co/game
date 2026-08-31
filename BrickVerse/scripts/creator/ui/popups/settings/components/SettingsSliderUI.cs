using Godot;
using BrickVerse.Creator.Properties;
using BrickVerse.Shared.Settings;
using System;

namespace BrickVerse.Creator.UI.Components;

public sealed partial class SettingsSliderUI : HBoxContainer, IProperty
{
	private HSlider _slider = null!;
	private SpinBox _value = null!;
	private bool _syncing;
	private SettingDef? _definition;

	public Type PropertyType { get; set; } = typeof(float);
	public event Action<object?>? ValueChanged;

	public override void _Ready()
	{
		_slider = GetNode<HSlider>("Slider");
		_value = GetNode<SpinBox>("Value");
		_slider.ValueChanged += OnSliderChanged;
		_value.ValueChanged += OnSpinChanged;
		if (_definition != null) ApplyDefinition(_definition);
	}

	public void Configure(SettingDef definition)
	{
		_definition = definition;
		PropertyType = definition.ValueType;
		if (IsNodeReady()) ApplyDefinition(definition);
	}

	private void ApplyDefinition(SettingDef definition)
	{
		double min = Convert.ToDouble(definition.UntypedMinValue ?? 0);
		double max = Convert.ToDouble(definition.UntypedMaxValue ?? 100);
		double step = Convert.ToDouble(definition.UntypedStep ?? (PropertyType == typeof(int) ? 1 : 0.01));
		_slider.MinValue = _value.MinValue = min;
		_slider.MaxValue = _value.MaxValue = max;
		_slider.Step = _value.Step = step;
		_slider.AllowGreater = _value.AllowGreater = false;
		_slider.AllowLesser = _value.AllowLesser = false;
		_value.Suffix = min == 0 && max == 100 ? "%" : "";
	}

	public object? GetValue() => ConvertValue(_slider.Value);

	public void SetValue(object? value)
	{
		if (value == null || !IsNodeReady()) return;
		SetControls(Convert.ToDouble(value));
	}

	private void OnSliderChanged(double value)
	{
		if (_syncing) return;
		SetControls(value);
		ValueChanged?.Invoke(ConvertValue(value));
	}

	private void OnSpinChanged(double value)
	{
		if (_syncing) return;
		SetControls(value);
		ValueChanged?.Invoke(ConvertValue(value));
	}

	private void SetControls(double value)
	{
		_syncing = true;
		_slider.SetValueNoSignal(value);
		_value.SetValueNoSignal(value);
		_syncing = false;
	}

	private object ConvertValue(double value) => PropertyType == typeof(int)
		? Mathf.RoundToInt(value)
		: (float)value;
}

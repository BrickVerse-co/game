// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Data;
using System;

namespace Polytoria.Creator.Properties;

public sealed partial class NumberRangeProperty : Control, IProperty<NumberRange>
{
	private NumberRange _value;
	private SpinBox _minBox = null!;
	private SpinBox _maxBox = null!;

	public NumberRange Value
	{
		get => _value;
		set
		{
			_value = value;
			Refresh();
		}
	}

	public Type PropertyType { get; set; } = null!;

	public event Action<object?>? ValueChanged;

	public object? GetValue()
	{
		return Value;
	}

	public void SetValue(object? value)
	{
		Value = (NumberRange)value!;
	}

	public void Refresh()
	{
		_minBox.SetValueNoSignal(Value.Min);
		_maxBox.SetValueNoSignal(Value.Max);
	}

	public override void _Ready()
	{
		_minBox = GetNode<SpinBox>("Min");
		_maxBox = GetNode<SpinBox>("Max");

		_minBox.ValueChanged += (val) =>
		{
			_value.Min = (float)val;
			ValueChanged?.Invoke(_value);
		};

		_maxBox.ValueChanged += (val) =>
		{
			_value.Max = (float)val;
			ValueChanged?.Invoke(_value);
		};

		Refresh();
	}
}

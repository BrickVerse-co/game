// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;

namespace Polytoria.Creator.Properties;

public sealed partial class BooleanProperty : CheckBox, IProperty<bool>
{
	private bool _value = false;

	public bool Value
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
		if (value == null) return;
		Value = (bool)value;
	}

	public void Refresh()
	{
		SetPressedNoSignal(_value);
	}

	public override void _Ready()
	{
		Refresh();

		Toggled += on =>
		{
			ValueChanged?.Invoke(on);
		};
	}
}

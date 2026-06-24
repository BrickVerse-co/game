// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Text.RegularExpressions;

namespace Polytoria.Creator.Properties;

public sealed partial class StringProperty : LineEdit, IProperty<string>
{
	private string _value = "";

	public string Value
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
		Value = (string)value;
	}

	public void Refresh()
	{
		Text = Value;
	}

	public override void _Ready()
	{
		Refresh();

		TextSubmitted += value =>
		{
			Text = Regex.Unescape(Text);
			ValueChanged?.Invoke(Text);
		};

		FocusExited += () =>
		{
			Text = Regex.Unescape(Text);
			ValueChanged?.Invoke(Text);
		};
	}
}

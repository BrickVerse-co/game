// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using ColorPicker = Polytoria.Creator.UI.ColorPicker;

namespace Polytoria.Creator.Properties;

public sealed partial class ColorProperty : Button, IProperty<Color>
{
	private Color _value;

	public Color Value
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

	public event Action<object?>? PreviewChanged;

	public object? GetValue()
	{
		return Value;
	}

	public void SetValue(object? value)
	{
		if (value == null) return;
		Value = (Color)value;
	}

	private StyleBoxFlat _preview = null!;

	public void Refresh()
	{
		_preview.BgColor = _value;
	}

	public override void _Ready()
	{
		_preview = (StyleBoxFlat)GetNode<Panel>("Panel").GetThemeStylebox("panel");
		Refresh();

		Pressed += () =>
		{
			ColorPicker.Singleton.SwitchTo(this, _value, previewColor =>
			{
				_value = previewColor;
				PreviewChanged?.Invoke(previewColor);
				Refresh();
			}, () =>
			{
				ValueChanged?.Invoke(_value);
			});
		};

		SetNotifyTransform(true);
	}

	public override void _Notification(int what)
	{
		if (what == NotificationTransformChanged)
		{
			ColorPicker.Singleton.CalculatePosition(this);
		}
	}
}

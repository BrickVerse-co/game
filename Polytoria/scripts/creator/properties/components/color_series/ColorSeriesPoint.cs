// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using ColorPicker = Polytoria.Creator.UI.ColorPicker;

namespace Polytoria.Creator.Properties.Components;

public partial class ColorSeriesPoint : Control
{
	[Export] private SpinBox _percentageBox = null!;
	[Export] private Button _colorButton = null!;
	[Export] private Control _colorPreview = null!;
	[Export] private Button _deleteButton = null!;

	public event Action<float>? OffsetChanged;
	public event Action<Color>? ColorChanged;
	public event Action? ColorChangeFinished;
	public event Action? DeleteRequested;

	public float OffsetValue;
	public Color ColorValue;

	public override void _Ready()
	{
		_percentageBox.ValueChanged += val =>
		{
			OffsetChanged?.Invoke(Mathf.Clamp((float)val / 100, 0, 1));
		};

		_colorButton.Pressed += () =>
		{
			ColorPicker.Singleton.SwitchTo(_colorButton, ColorValue, previewColor =>
			{
				_colorPreview.SelfModulate = previewColor;
				ColorChanged?.Invoke(previewColor);
			}, () =>
			{
				ColorChangeFinished?.Invoke();
			});
		};

		_deleteButton.Pressed += () => DeleteRequested?.Invoke();

		_colorPreview.SelfModulate = ColorValue;
		_percentageBox.SetValueNoSignal(OffsetValue * 100);
	}
}

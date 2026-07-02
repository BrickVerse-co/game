// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Creator.Settings;

namespace BrickVerse.Creator.UI;

public partial class SnappingMenu : Control
{
	[Export] private CheckBox _moveCheck = null!;
	[Export] private SpinBox _moveValue = null!;
	[Export] private CheckBox _rotateCheck = null!;
	[Export] private SpinBox _rotateValue = null!;
	private bool _initializing;

	public override void _Ready()
	{
		_moveValue.ValueChanged += MoveValueChanged;
		_rotateValue.ValueChanged += RotateValueChanged;

		_moveCheck.Toggled += MoveCheckToggled;
		_rotateCheck.Toggled += RotateCheckToggled;

		_initializing = true;
		_moveCheck.ButtonPressed = CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Interface.MoveSnapEnabled);
		_moveValue.Value = CreatorSettingsService.Instance.Get<float>(CreatorSettingKeys.Interface.MoveSnapStep);
		_rotateCheck.ButtonPressed = CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Interface.RotateSnapEnabled);
		_rotateValue.Value = CreatorSettingsService.Instance.Get<float>(CreatorSettingKeys.Interface.RotateSnapStep);
		_initializing = false;

		base._Ready();
	}

	private void MoveCheckToggled(bool toggledOn)
	{
		if (_initializing) return;
		CreatorSettingsService.Instance.Set(CreatorSettingKeys.Interface.MoveSnapEnabled, toggledOn);
		CreatorService.Interface.MoveSnapEnabled = toggledOn;
	}

	private void RotateCheckToggled(bool toggledOn)
	{
		if (_initializing) return;
		CreatorSettingsService.Instance.Set(CreatorSettingKeys.Interface.RotateSnapEnabled, toggledOn);
		CreatorService.Interface.RotateSnapEnabled = toggledOn;
	}

	private void MoveValueChanged(double value)
	{
		if (_initializing) return;
		CreatorSettingsService.Instance.Set(CreatorSettingKeys.Interface.MoveSnapStep, (float)value);
		CreatorService.Interface.UserMoveSnapping = (float)value;
	}

	private void RotateValueChanged(double value)
	{
		if (_initializing) return;
		CreatorSettingsService.Instance.Set(CreatorSettingKeys.Interface.RotateSnapStep, (float)value);
		CreatorService.Interface.UserRotateSnapping = (float)value;
	}
}

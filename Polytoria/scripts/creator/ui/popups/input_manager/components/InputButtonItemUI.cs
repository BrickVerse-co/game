// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Data;

namespace Polytoria.Creator.UI.Components;

public partial class InputButtonItemUI : Control
{
	[Export] private Label _keyNameLabel = null!;
	[Export] private TextureRect _iconRect = null!;
	[Export] private Button _removeBtn = null!;
	public InputAction TargetAction = null!;
	public InputButton TargetButton = null!;
	public InputButtonGroupUI GroupParent = null!;

	public override void _Ready()
	{
		_keyNameLabel.Text = TargetButton.KeyCode.ToString();
		_removeBtn.Pressed += OnRemovePressed;
	}

	private void OnRemovePressed()
	{
		GroupParent.RemoveButton(TargetButton);
	}
}

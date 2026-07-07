// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI;

public partial class UISettingsCategoryButton : Button
{
	// [Export]
	// public SettingsViewEnum SwitchTo;

	// public override void _Ready()
	// {
	// 	UIMenuSettings.Singleton.RegisterCategoryButton(this);
	// 	Pressed += OnPressed;
	// 	UIMenuSettings.Singleton.ViewChanged += OnViewChanged;
	// }

	// private void OnViewChanged(SettingsViewEnum @enum)
	// {
	// 	SetPressedNoSignal(SwitchTo == @enum);
	// }

	// private void OnPressed()
	// {
	// 	UIMenuSettings.Singleton.SwitchView(SwitchTo);
	// }
}

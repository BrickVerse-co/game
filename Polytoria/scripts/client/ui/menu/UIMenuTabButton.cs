// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI;

public partial class UIMenuTabButton : Button
{
	[Export]
	public UIGameMenu.GameMenuViewEnum SwitchTo;

	public override void _Ready()
	{
		CoreUIRoot.Singleton.GameMenu.RegisterTabButton(this);
		CoreUIRoot.Singleton.GameMenu.ViewChanged += OnViewChanged;
		base._Ready();
	}

	public override void _Pressed()
	{
		CoreUIRoot.Singleton.GameMenu.SwitchView(SwitchTo);
		base._Pressed();
	}

	private void OnViewChanged(UIGameMenu.GameMenuViewEnum @enum)
	{
		SetPressedNoSignal(@enum == SwitchTo);
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Mobile.UI;

public partial class NavbarButton : Button
{
	[Export]
	public MobileViewEnum SwitchTo;

	public override void _Ready()
	{
		MobileUI.Singleton.ViewPathSwitched += OnViewPathSwitched;
		base._Ready();
	}

	private void OnViewPathSwitched(MobileViewEnum to)
	{
		Modulate = to == SwitchTo ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0.4f);
	}

	public override void _Pressed()
	{
		MobileUI.Singleton.SwitchTo(SwitchTo);
		base._Pressed();
	}
}

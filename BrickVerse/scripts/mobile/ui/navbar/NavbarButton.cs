// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace BrickVerse.Mobile.UI;

public partial class NavbarButton : Button
{
	[Export]
	public MobileViewEnum SwitchTo;

	public override void _Ready()
	{
		MobileUI.Singleton.ViewPathSwitched += OnViewPathSwitched;
		MobileMotion.Bind(this);
		base._Ready();
	}

	private void OnViewPathSwitched(MobileViewEnum to)
	{
		Modulate = to == SwitchTo
			? Color.FromHtml("0097FF")
			: Color.FromHtml("697381");
	}

	public override void _Pressed()
	{
		if (MobileUI.Singleton.CurrentView == SwitchTo) MobileUI.Singleton.RefreshCurrentView();
		else MobileUI.Singleton.SwitchTo(SwitchTo);
		base._Pressed();
	}
}

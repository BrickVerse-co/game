// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI;

public partial class UIMenuButton : Button
{
	[Export] private AnimationPlayer _menuAnim = null!;

	public override void _Ready()
	{
		CoreUIRoot.Singleton.GameMenu.IsShowingChanged += OnIsShowingChanged;
		base._Ready();
	}

	private void OnIsShowingChanged(bool to)
	{
		_menuAnim.Stop();
		if (to)
		{
			_menuAnim.Play("open");
		}
		else
		{
			_menuAnim.Play("close");
		}
		SetPressedNoSignal(to);
	}

	public override void _Toggled(bool toggledOn)
	{
		if (toggledOn)
		{
			CoreUIRoot.Singleton.GameMenu.ShowMenu();
		}
		else
		{
			CoreUIRoot.Singleton.GameMenu.HideMenu();
		}
		base._Toggled(toggledOn);
	}
}

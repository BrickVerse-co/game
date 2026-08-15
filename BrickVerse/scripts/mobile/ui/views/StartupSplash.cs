// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace BrickVerse.Mobile.UI;

public partial class StartupSplash : Control
{
	public override void _Ready()
	{
		Visible = true;
		GetNode<Label>("BuildInfo").Text = $"BrickVerse {ProjectSettings.GetSetting("application/config/version", "1.0.0")}\n© {System.DateTime.UtcNow.Year} Meta Games LLC";
	}

	public void HideSplash()
	{
		GetNode<AnimationPlayer>("AnimPlay").Play("fadeout");
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;

namespace Polytoria.Creator.UI.Popups;

public sealed partial class CreditsPopup : PopupWindowBase
{
	[Export] private Label _versionLabel = null!;

	public override void _Ready()
	{
		_versionLabel.Text = Globals.AppVersion;
		base._Ready();
	}
}

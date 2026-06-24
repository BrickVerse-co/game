// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;

namespace Polytoria.Creator.UI;

public partial class InsertPopupItem : Button
{
	[Export] public TextureRect IconRect = null!;
	[Export] public Label ClassLabel = null!;
	public string Classname = null!;

	public override void _Ready()
	{
		IconRect.Texture = Globals.LoadIcon(Classname);
		ClassLabel.Text = Classname;
	}
}

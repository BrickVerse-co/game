// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;

namespace Polytoria.Creator.Spatial;

public partial class SpatialIcon : Sprite3D, ISpatial
{
	public SpatialIcon(string iconName)
	{
		if (Globals.CurrentAppEntry != Globals.AppEntryEnum.Creator) { Visible = false; return; }
		Texture = GD.Load<Texture2D>("res://assets/textures/creator/spatial/icons/" + iconName + ".svg");
		Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		PixelSize = 0.01f;
	}
}

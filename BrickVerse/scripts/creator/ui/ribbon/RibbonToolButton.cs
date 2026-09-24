// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;

namespace BrickVerse.Creator.UI;

public partial class RibbonToolButton : Button
{
	[Export]
	public ToolModeEnum ToolMode;

	public override void _Ready()
	{
		// Ribbon button visuals are full-size child Controls. Their default Stop
		// filter otherwise consumes clicks over the label before this Button can
		// emit Pressed/Toggled. Keep the complete visible tile clickable.
		SetDescendantsMouseIgnored(this);
		base._Ready();
	}

	public override void _Pressed()
	{
		CreatorService.Interface.ToolMode = ToolMode;
		World.Current?.CreatorContext?.Gizmos?.RefreshVisuals();
		World.Current?.Container?.GrabFocus();
		base._Pressed();
	}

	private static void SetDescendantsMouseIgnored(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			if (child is Control control) control.MouseFilter = MouseFilterEnum.Ignore;
			SetDescendantsMouseIgnored(child);
		}
	}
}

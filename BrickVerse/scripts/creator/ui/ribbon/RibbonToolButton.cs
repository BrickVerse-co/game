// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel.Creator;

namespace BrickVerse.Creator.UI;

public partial class RibbonToolButton : Button
{
	[Export]
	public ToolModeEnum ToolMode;
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI.Touch;

public partial class JumpButton : Control
{
	public override void _Ready()
	{
		Visible = World.Current!.Input.IsTouchscreen;
	}
}

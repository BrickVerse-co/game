// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Creator.UI.Popups;

public partial class PopupWindowBase : Window
{
	public override void _Ready()
	{
		CloseRequested += QueueFree;

		base._Ready();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			QueueFree();
		}
		base._Input(@event);
	}
}

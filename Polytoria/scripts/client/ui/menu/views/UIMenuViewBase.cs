// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI;

public partial class UIMenuViewBase : Control
{
	[Export] public Control? FirstFocus { get; private set; }

	public UIGameMenu Menu { get; set; } = null!;

	public virtual void ShowView()
	{
		Visible = true;
	}

	public virtual void HideView()
	{
		Visible = false;
	}
}

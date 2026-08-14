// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileViewBase : Control
{
	public virtual void ShowView(object? args)
	{

	}

	public virtual void HideView()
	{

	}

	public virtual void RefreshView() { }

	public virtual bool TryNavigateAway(System.Action continuation) => true;
}

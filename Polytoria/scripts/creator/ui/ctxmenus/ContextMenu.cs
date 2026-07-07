// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;

namespace Polytoria.Creator.UI;

public partial class ContextMenu : PopupMenu
{
	public const int ItemIconSize = 16;
	private Control? _dummyFocus;

	public override void _Ready()
	{
		AboutToPopup += OnAboutToPopup;
		CloseRequested += OnCloseRequested;
		TreeExiting += OnCloseRequested;
		base._Ready();
	}

	private void OnCloseRequested()
	{
		_dummyFocus?.QueueFree();
		Close();
	}

	private void OnAboutToPopup()
	{
		_dummyFocus = new();
		GetParent().AddChild(_dummyFocus);
		_dummyFocus.FocusMode = Control.FocusModeEnum.All;
		_dummyFocus.GrabFocus();
	}

	public void PopupAtCursor()
	{
		Popup(new((Vector2I)GetViewport().GetMousePosition(), Vector2I.Zero));
	}

	protected void AddIconItem(string iconName, string label, int id)
	{
		AddIconItem(Globals.LoadUIIcon(iconName), label, id);
		SetItemIconMaxWidth(GetItemIndex(id), ItemIconSize);
	}

	protected static void SetIconItem(PopupMenu menu, string iconName, string label, int id)
	{
		menu.AddIconItem(Globals.LoadUIIcon(iconName), label, id);
		menu.SetItemIconMaxWidth(menu.GetItemIndex(id), ItemIconSize);
	}

	public void Close()
	{
		if (IsInstanceValid(this))
		{
			QueueFree();
		}
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Creator;
using System;

namespace Polytoria.Creator.UI.Popups;

public sealed partial class GiveNamePopup : PopupWindowBase
{
	[Export] private LineEdit _pathEdit = null!;
	[Export] private Button _createBtn = null!;
	[Export] private Button _cancelBtn = null!;

	public string Placeholder = "";
	public string DefaultValue = "";
	public event Action<string>? Submitted;

	public override void _Ready()
	{
		base._Ready();
		_pathEdit.PlaceholderText = Placeholder;
		_pathEdit.Text = DefaultValue;
		_pathEdit.GrabFocus();

		_pathEdit.GuiInput += @event =>
		{
			if (@event.IsActionPressed("ui_accept"))
			{
				SubmitName();
			}
		};

		_createBtn.Pressed += SubmitName;
		_cancelBtn.Pressed += QueueFree;
	}

	private void SubmitName()
	{
		if (CreatorService.CurrentSession == null) return;
		CreatorSession session = CreatorService.CurrentSession;
		string folderName = _pathEdit.Text;

		try
		{
			Submitted?.Invoke(folderName);
			QueueFree();
		}
		catch (Exception ex)
		{
			OS.Alert(ex.Message);
		}
	}
}

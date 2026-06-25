// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Schemas.API;

namespace BrickVerse.Creator.UI;

public partial class ToolboxItemContextMenu : ContextMenu
{
	public APILibraryItem ItemData;
	public LibraryQueryTypeEnum ItemType;
	public ToolboxCard ParentCard = null!;

	public override void _Ready()
	{
		AddIconItem("copy_name", "Copy Name", 0);
		AddIconItem("copy_id", "Copy ID", 1);
		AddIconItem("view", "View on BrickVerse", 2);
		AddIconItem("report", "Report", 3);
		IdPressed += OnIdPressed;
	}

	private async void OnIdPressed(long id)
	{
		switch (id)
		{
			case 0:
				DisplayServer.ClipboardSet(ItemData.Name);
				break;
			case 1:
				DisplayServer.ClipboardSet(ItemData.ID.ToString());
				break;
			case 2:
				OS.ShellOpen("https://brickverse.gg/assets/" + ItemData.ID);
				break;
			case 3:
				OS.ShellOpen("https://brickverse.gg/report?type=asset&id=" + ItemData.ID);
				break;
		}
	}
}

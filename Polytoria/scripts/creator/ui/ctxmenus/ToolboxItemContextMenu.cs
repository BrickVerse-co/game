// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;

namespace Polytoria.Creator.UI;

public partial class ToolboxItemContextMenu : ContextMenu
{
	public APILibraryItem ItemData;
	public LibraryQueryTypeEnum ItemType;
	public ToolboxCard ParentCard = null!;

	public override void _Ready()
	{
		AddIconItem("copy", "Copy ID", 1);
		IdPressed += OnIdPressed;
	}

	private async void OnIdPressed(long id)
	{
		switch (id)
		{
			case 1:
				DisplayServer.ClipboardSet(ItemData.ID.ToString());
				break;
		}
	}
}

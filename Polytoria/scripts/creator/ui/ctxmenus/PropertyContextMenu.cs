// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.UI.Misc;
using Polytoria.Datamodel.Creator;

namespace Polytoria.Creator.UI;

public partial class PropertyContextMenu : ContextMenu
{
	public PropertyLabel Target = null!;

	public override void _Ready()
	{
		base._Ready();
		AddIconItem("copy", "Copy Property", 1);
		AddIconItem("clipboard", "Paste Property", 2);
		AddIconItem("", "Copy Property Name", 3);

		// Disable paste if no clipboard, or is not the same type
		SetItemDisabled(GetItemIndex(2), CreatorService.Clipboard.PropertyClipboard == null || CreatorService.Clipboard.PropertyClipboard.GetType() != Target.Property.PropertyType);

		IdPressed += OnIdPressed;
	}

	private async void OnIdPressed(long id)
	{
		switch (id)
		{
			case 1: // Copy Property
				CreatorService.Clipboard.PropertyClipboard = Target.PropertyPair.GetValue();
				break;
			case 2: // Paste Property
				object? setTo = CreatorService.Clipboard.PropertyClipboard;
				Target.NotifyPaste(setTo);
				break;
			case 3: // Copy Property Name
				DisplayServer.ClipboardSet(Target.Property.Name);
				break;
		}
	}
}

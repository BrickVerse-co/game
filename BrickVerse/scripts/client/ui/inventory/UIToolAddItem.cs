// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI;

public partial class UIToolAddItem : Button
{
	public UIInventory Root = null!;

	public override bool _CanDropData(Vector2 atPosition, Variant data)
	{
		if (data.VariantType == Variant.Type.String)
		{
			string str = data.AsString();

			if (str.StartsWith("tool:"))
			{
				return true;
			}
		}
		return false;
	}

	public override void _DropData(Vector2 atPosition, Variant data)
	{
		if (data.VariantType == Variant.Type.String)
		{
			string str = data.AsString();

			if (str.StartsWith("tool:"))
			{
				string netId = str.Replace("tool:", "");
				Tool? tool = Root.GetToolFromNetworkID(netId);

				if (tool != null)
				{
					Root.AddNewToolInSlot(tool);
				}
			}
		}
	}
}

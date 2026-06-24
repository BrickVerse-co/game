// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI;

public partial class UIInventoryBackpack : Control
{
	[Export] public UIInventory UIInventory { get; private set; } = null!;

	public override void _Ready()
	{
		base._Ready();
	}

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
				Tool? tool = UIInventory.GetToolFromNetworkID(netId);

				if (tool != null)
				{
					UIInventory.AddNewToolInBackpack(tool);
				}
			}
		}
	}
}

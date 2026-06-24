// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Properties;
using System;
using System.Reflection;

namespace Polytoria.Creator.UI.Misc;

public partial class PropertyLabel : Label
{
	public PropertyContextMenu? ItemContextMenu;
	public PropertyInfo Property = null!;
	public IProperty PropertyPair = null!;
	public object Targets = null!;
	public event Action<object?>? Pasted;

	internal void NotifyPaste(object? to)
	{
		Pasted?.Invoke(to);
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton btn && btn.ButtonIndex == MouseButton.Right && btn.Pressed)
		{
			ItemContextMenu?.Close();

			ItemContextMenu = new() { Target = this };
			AddChild(ItemContextMenu);
			ItemContextMenu.PopupAtCursor();
		}
		base._GuiInput(@event);
	}
}

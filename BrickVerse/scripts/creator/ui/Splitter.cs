// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Creator.UI;

public sealed partial class Splitter : Control
{
	public override void _Ready()
	{
		HSplitContainer left = GetNode<HSplitContainer>("Left");
		HSplitContainer right = GetNode<HSplitContainer>("Right");
		VBoxContainer center = GetNode<VBoxContainer>("Center");

		left.Dragged += offset =>
		{
			float left = offset;
			right.OffsetLeft = left + 12;
			center.OffsetLeft = left;
		};

		right.Dragged += offset =>
		{
			float right = offset;
			left.OffsetRight = right - 12;
			center.OffsetRight = right;
		};
	}
}

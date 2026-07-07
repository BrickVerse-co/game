// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;

namespace Polytoria.Creator.UI;

public partial class TagLabel : Control
{
	public string Text { get; set; } = "";

	public event Action? DeleteRequested;

	public override void _Ready()
	{
		GetNode<Label>("Layout/Label").Text = Text;
		GetNode<Button>("Layout/Delete").Pressed += () => { DeleteRequested?.Invoke(); };
	}
}

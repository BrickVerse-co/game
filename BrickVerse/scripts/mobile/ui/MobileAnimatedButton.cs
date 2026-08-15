// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileAnimatedButton : Button
{
	public override void _Ready() => MobileMotion.Bind(this);
}

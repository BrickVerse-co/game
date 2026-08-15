// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileAnimatedToggle : CheckButton
{
	public override void _Ready()
	{
		MobileMotion.Bind(this);
		Toggled += AnimateState;
		AnimateState(ButtonPressed);
	}

	private void AnimateState(bool enabled)
	{
		Color target = enabled ? new Color(0.68f, 0.87f, 1f) : Colors.White;
		CreateTween().TweenProperty(this, "modulate", target, 0.16).SetTrans(Tween.TransitionType.Cubic);
	}
}

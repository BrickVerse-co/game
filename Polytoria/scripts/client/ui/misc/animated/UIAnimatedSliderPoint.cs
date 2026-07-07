// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI.Animated;

public partial class UIAnimatedSliderPoint : Button
{
	public double Progress = 0;
	public bool Active = false;
	private AnimationPlayer _animPlay = null!;

	public override void _Ready()
	{
		_animPlay = GetNode<AnimationPlayer>("AnimPlay");
	}

	public void Jump()
	{
		_animPlay.Stop();
		_animPlay.Play("jump");
	}
}

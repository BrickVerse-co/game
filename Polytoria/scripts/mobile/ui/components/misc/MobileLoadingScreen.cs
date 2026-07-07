// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Mobile.UI;

public partial class MobileLoadingScreen : Node
{
	private AnimationPlayer _animPlay = null!;

	public override void _Ready()
	{
		_animPlay = GetNode<AnimationPlayer>("AnimPlay");
		base._Ready();
	}

	public void ShowScreen()
	{
		_animPlay.Play("appear");
	}

	public void HideScreen()
	{
		_animPlay.Play("disappear");
	}
}

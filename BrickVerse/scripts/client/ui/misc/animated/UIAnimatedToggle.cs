// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI.Animated;

public partial class UIAnimatedToggle : Node
{
	private Button _targetBtn = null!;
	private AnimationPlayer _animPlay = null!;

	public override void _Ready()
	{
		_animPlay = GetNode<AnimationPlayer>("AnimationPlayer");
		_targetBtn = GetParent<Button>();

		_targetBtn.Toggled += OnButtonToggled;

		_targetBtn.Ready += () =>
		{
			if (_targetBtn.ButtonPressed)
			{
				_animPlay.Play("on");
			}
			else
			{
				_animPlay.Play("off");
			}
		};
	}

	private void OnButtonToggled(bool toggledOn)
	{
		_animPlay.Stop();
		if (toggledOn)
		{
			_animPlay.Play("switch_on");
		}
		else
		{
			_animPlay.Play("switch_off");
		}

	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI;

public partial class UIOverviewButton : Button
{
	private Tween? _tween;
	private Control _rect = null!;
	private Control _rectStart = null!;
	private Control _rectFull = null!;

	public override void _Ready()
	{
		_rect = GetNode<Control>("Rect2");
		_rectStart = GetNode<Control>("RectStart");
		_rectFull = GetNode<Control>("RectFull");
		_rect.SetDeferred(Control.PropertyName.Size, _rectStart.Size);
		MouseEntered += OnMouseEntered;
		MouseExited += OnMouseExited;
	}

	private void OnMouseEntered()
	{
		if (IsInstanceValid(_tween))
		{
			_tween.Stop();
		}


		_tween = GetTree().CreateTween();
		PropertyTweener propTweener = _tween.TweenProperty(_rect, "size", _rectFull.Size, 0.3f);
		propTweener.SetEase(Tween.EaseType.Out);
		propTweener.SetTrans(Tween.TransitionType.Back);
	}

	private void OnMouseExited()
	{
		if (IsInstanceValid(_tween))
		{
			_tween.Stop();
		}


		_tween = GetTree().CreateTween();
		PropertyTweener propTweener = _tween.TweenProperty(_rect, "size", _rectStart.Size, 0.3f);
		propTweener.SetEase(Tween.EaseType.Out);
		propTweener.SetTrans(Tween.TransitionType.Back);
	}
}

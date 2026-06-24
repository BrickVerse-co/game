// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;

namespace Polytoria.Client.UI.Animated;

public partial class UIAnimatedSlider : Control
{
	private const string PointPath = "res://scenes/client/ui/misc/animated/animated_slider_point.tscn";

	private Color _baseColor = new(1, 1, 1);
	private PackedScene _pointPacked = null!;
	private Slider _targetSlider = null!;
	private bool _btnDown = false;
	private bool _initialized = false;
	private readonly List<UIAnimatedSliderPoint> btns = [];

	public override void _Ready()
	{
		_pointPacked = GD.Load<PackedScene>(PointPath);

		_targetSlider = GetParent<Slider>();
		_targetSlider.MouseFilter = MouseFilterEnum.Ignore;
		_targetSlider.SelfModulate = new(0, 0, 0, 0);

		_targetSlider.Ready += RefreshSlider;

		int stepCount = (int)Math.Round((_targetSlider.MaxValue - _targetSlider.MinValue) / _targetSlider.Step);

		for (int i = 0; i <= stepCount; i++)
		{
			double myI = _targetSlider.MinValue + (i * _targetSlider.Step);
			UIAnimatedSliderPoint point = _pointPacked.Instantiate<UIAnimatedSliderPoint>();
			point.Progress = myI;
			AddChild(point);

			point.ButtonDown += () =>
			{
				_targetSlider.Value = myI;
				_btnDown = true;
			};

			point.MouseEntered += () =>
			{
				if (_btnDown)
				{
					_targetSlider.Value = myI;
				}
			};
			btns.Add(point);
		}

		_targetSlider.ValueChanged += (_) =>
		{
			RefreshSlider();
		};
	}

	private void RefreshSlider()
	{
		float all = btns.Count;
		float lightness = 0.8f;
		float addBy = 1 / all;

		int currentStep = (int)Math.Round((_targetSlider.Value - _targetSlider.MinValue) / _targetSlider.Step);

		for (int i = 0; i < btns.Count; i++)
		{
			UIAnimatedSliderPoint btn = btns[i];

			if (i > currentStep)
			{
				btn.Modulate = _baseColor.Darkened(0.7f);
				btn.Active = false;
			}
			else
			{
				btn.Modulate = _baseColor * lightness;
				if (!btn.Active)
				{
					btn.Active = true;
					if (_initialized)
					{
						btn.Jump();
					}
				}
			}
			lightness += addBy;
		}
		_initialized = true;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton btn)
		{
			if (btn.ButtonIndex == MouseButton.Left && !btn.Pressed && _btnDown)
			{
				_btnDown = false;
			}
		}
	}
}

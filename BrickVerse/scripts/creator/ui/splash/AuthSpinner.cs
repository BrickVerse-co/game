// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using Godot;

namespace BrickVerse.Creator.UI;

/// <summary>Small, theme-independent activity spinner used by the authentication stage.</summary>
public sealed partial class AuthSpinner : Control
{
	private float _phase;

	public override void _Ready()
	{
		SetProcess(true);
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public override void _Process(double delta)
	{
		_phase = Mathf.PosMod(_phase + (float)delta * 4.2f, Mathf.Tau);
		QueueRedraw();
	}

	public override void _Draw()
	{
		Vector2 center = Size * 0.5f;
		float radius = Mathf.Max(4, Mathf.Min(Size.X, Size.Y) * 0.34f);
		DrawArc(center, radius, 0, Mathf.Tau, 32, new Color(1, 1, 1, 0.12f), 2.5f, true);
		DrawArc(center, radius, _phase, _phase + Mathf.Pi * 1.35f, 22, new Color("32a9ff"), 2.5f, true);
	}
}

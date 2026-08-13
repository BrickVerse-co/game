// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;

namespace BrickVerse.Mobile.UI;

public static class MobileMotion
{
	public static void Enter(Control control, int index = 0)
	{
		control.Modulate = new Color(1, 1, 1, 0);
		control.PivotOffset = control.Size / 2f;
		control.Scale = new Vector2(0.97f, 0.97f);
		Tween tween = control.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		double delay = System.Math.Min(index, 8) * 0.025;
		tween.TweenProperty(control, "scale", Vector2.One, 0.24).SetDelay(delay);
		tween.TweenProperty(control, "modulate:a", 1f, 0.2).SetDelay(delay);
	}

	public static void BindCard(Control card)
	{
		card.MouseEntered += () => Animate(card, new Vector2(1.012f, 1.012f), 0.14);
		card.MouseExited += () => Animate(card, Vector2.One, 0.16);
	}

	public static void Bind(BaseButton button)
	{
		button.PivotOffset = button.Size / 2f;
		button.Resized += () => { if (GodotObject.IsInstanceValid(button)) button.PivotOffset = button.Size / 2f; };
		button.MouseEntered += () => Animate(button, new Vector2(1.025f, 1.025f), 0.11);
		button.MouseExited += () => Animate(button, Vector2.One, 0.13);
		button.ButtonDown += () => Animate(button, new Vector2(0.96f, 0.96f), 0.07);
		button.ButtonUp += () => Animate(button, button.IsHovered() ? new Vector2(1.025f, 1.025f) : Vector2.One, 0.1);
	}

	private static void Animate(Control control, Vector2 scale, double duration)
	{
		if (!GodotObject.IsInstanceValid(control) || !control.IsInsideTree()) return;
		Tween tween = control.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(control, "scale", scale, duration);
	}
}

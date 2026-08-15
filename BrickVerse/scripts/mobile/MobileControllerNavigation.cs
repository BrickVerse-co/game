// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;

namespace BrickVerse.Mobile;

/// <summary>Explicit focus navigation for controllers on the app UI, independent of platform defaults.</summary>
public sealed partial class MobileControllerNavigation : Node
{
	private Vector2 _stickLatch;

	public override void _Process(double delta)
	{
		EnsureFocus();
		Vector2 stick = Input.GetVector("leftward", "rightward", "forward", "backward");
		if (stick.Length() < 0.45f) _stickLatch = Vector2.Zero;
		else if (_stickLatch == Vector2.Zero)
		{
			_stickLatch = stick;
			if (Mathf.Abs(stick.Y) >= Mathf.Abs(stick.X)) Send(stick.Y < 0 ? "ui_up" : "ui_down");
			else Send(stick.X < 0 ? "ui_left" : "ui_right");
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventJoypadButton button || !button.Pressed) return;
		switch (button.ButtonIndex)
		{
			case JoyButton.A: Send("ui_accept"); break;
			case JoyButton.B: Send("ui_cancel"); break;
			case JoyButton.DpadUp: Send("ui_up"); break;
			case JoyButton.DpadDown: Send("ui_down"); break;
			case JoyButton.DpadLeft: Send("ui_left"); break;
			case JoyButton.DpadRight: Send("ui_right"); break;
			case JoyButton.LeftShoulder: Send("ui_left"); break;
			case JoyButton.RightShoulder: Send("ui_right"); break;
		}
	}

	private void EnsureFocus()
	{
		if (GetViewport().GuiGetFocusOwner() != null || GetParent() == null) return;
		foreach (Node node in GetParent().FindChildren("*", "Button", true, false))
			if (node is Button button && button.IsVisibleInTree() && !button.Disabled && button.FocusMode != Control.FocusModeEnum.None)
			{
				button.GrabFocus();
				return;
			}
	}

	private static void Send(string action)
	{
		Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
		Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
	}
}

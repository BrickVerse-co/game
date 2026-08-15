// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Client.XR;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Mobile;

/// <summary>Head-locked OpenXR shell that makes the mobile app usable before a world is launched.</summary>
public sealed partial class XRMobileShell : Node3D
{
	private OpenXRInterface? _interface;
	private XRController3D? _left;
	private XRController3D? _right;
	private Vector2 _navigationLatch;
	private bool _acceptLatch;
	private bool _cancelLatch;

	public bool Initialize()
	{
		if (!XRRuntime.WasRequested()) return false;
		_interface = XRServer.FindInterface("OpenXR") as OpenXRInterface;
		if (_interface == null || !_interface.IsInitialized() && !_interface.Initialize())
		{
			BV.PrintErr("The VR app shell could not initialize OpenXR; leaving the normal app window available.");
			return false;
		}

		XROrigin3D origin = new() { Name = "AppShellOrigin" };
		XRCamera3D camera = new() { Name = "AppShellCamera" };
		_left = new XRController3D { Name = "LeftController", Tracker = "/user/hand/left" };
		_right = new XRController3D { Name = "RightController", Tracker = "/user/hand/right" };
		origin.AddChild(camera);
		origin.AddChild(_left);
		origin.AddChild(_right);
		AddChild(origin);
		GetViewport().UseXR = true;
		camera.MakeCurrent();
		return true;
	}

	public override void _Process(double delta)
	{
		if (_left == null || _right == null) return;
		EnsureFocusedControl();
		Vector2 navigation = _left.GetVector2("primary");
		if (navigation.Length() < 0.45f) _navigationLatch = Vector2.Zero;
		else if (_navigationLatch == Vector2.Zero)
		{
			_navigationLatch = navigation;
			if (Mathf.Abs(navigation.Y) >= Mathf.Abs(navigation.X)) SendAction(navigation.Y < 0 ? "ui_up" : "ui_down");
			else SendAction(navigation.X < 0 ? "ui_left" : "ui_right");
		}

		bool accept = _right.IsButtonPressed("ax_button") || _right.IsButtonPressed("trigger_click");
		if (accept && !_acceptLatch) SendAction("ui_accept");
		_acceptLatch = accept;
		bool cancel = _right.IsButtonPressed("by_button") || _left.IsButtonPressed("menu_button");
		if (cancel && !_cancelLatch) SendAction("ui_cancel");
		_cancelLatch = cancel;
	}

	private void EnsureFocusedControl()
	{
		if (GetViewport().GuiGetFocusOwner() != null || GetParent() == null) return;
		foreach (Node node in GetParent().FindChildren("*", "Button", true, false))
		{
			if (node is Button button && button.IsVisibleInTree() && !button.Disabled && button.FocusMode != Control.FocusModeEnum.None)
			{
				button.GrabFocus();
				return;
			}
		}
	}

	private static void SendAction(string action)
	{
		InputEventAction press = new() { Action = action, Pressed = true };
		Input.ParseInputEvent(press);
		InputEventAction release = new() { Action = action, Pressed = false };
		Input.ParseInputEvent(release);
	}

	public override void _ExitTree()
	{
		if (GetViewport() != null) GetViewport().UseXR = false;
	}
}

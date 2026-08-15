// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Services;
using BrickVerse.Utils;
using BrickVerse.Shared;

namespace BrickVerse.Client.XR;

/// <summary>Cross-platform OpenXR rig used by both desktop runtimes (Link/SteamVR) and Android headsets.</summary>
public sealed partial class XRRuntime : Node3D
{
	private OpenXRInterface? _openXr;
	private XROrigin3D? _origin;
	private XRCamera3D? _head;
	private XRController3D? _left;
	private XRController3D? _right;
	private float _snapTurnLatch;
	private bool _activatePressed;
	private bool _interactPressed;
	private bool _menuPressed;
	private bool _jumpPressed;
	private World _world = null!;

	public bool IsActive { get; private set; }
	public Vector2 MoveVector => IsActive && _left != null ? _left.GetVector2("primary") : Vector2.Zero;
	public bool JumpPressed => IsActive && _right != null && (_right.IsButtonPressed("ax_button") || _right.IsButtonPressed("primary_click"));
	public Transform3D HeadPose => _head?.Transform ?? Transform3D.Identity;
	public Transform3D LeftHandPose => _left?.Transform ?? Transform3D.Identity;
	public Transform3D RightHandPose => _right?.Transform ?? Transform3D.Identity;
	public float HeadYaw => _head?.GlobalRotation.Y ?? 0f;
	public float LeftTrigger => IsActive && _left != null ? _left.GetFloat("trigger") : 0f;
	public float RightTrigger => IsActive && _right != null ? _right.GetFloat("trigger") : 0f;
	public float LeftGrip => IsActive && _left != null ? _left.GetFloat("grip") : 0f;
	public float RightGrip => IsActive && _right != null ? _right.GetFloat("grip") : 0f;
	public bool IsHeadTracked => IsActive && _head != null;
	public bool IsLeftHandTracked => IsActive && _left?.GetIsActive() == true;
	public bool IsRightHandTracked => IsActive && _right?.GetIsActive() == true;
	public string RuntimeName => _openXr?.GetName() ?? "";

	public static bool WasRequested()
	{
		if (OS.HasFeature("xr")) return true;
		string[] args = OS.GetCmdlineArgs();
		for (int index = 0; index < args.Length; index++)
			if (args[index] == "--xr-mode" && index + 1 < args.Length && args[index + 1].Equals("on", System.StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	public void Initialize(World world)
	{
		_world = world;
		_openXr = XRServer.FindInterface("OpenXR") as OpenXRInterface;
		if (_openXr == null || !_openXr.IsInitialized() && !_openXr.Initialize())
		{
			BV.Print("OpenXR runtime unavailable; continuing in flatscreen mode.");
			QueueFree();
			return;
		}

		_origin = new XROrigin3D { Name = "BrickVerseXROrigin" };
		_head = new XRCamera3D { Name = "Head" };
		_left = new XRController3D { Name = "LeftController", Tracker = "/user/hand/left" };
		_right = new XRController3D { Name = "RightController", Tracker = "/user/hand/right" };
		_origin.AddChild(_head);
		_origin.AddChild(_left);
		_origin.AddChild(_right);
		AddChild(_origin);
		XRServer.WorldScale = 1.0;
		GetViewport().UseXR = true;
		IsActive = true;
		BV.Print("OpenXR initialized: ", _openXr.GetSystemInfo());
	}

	public override void _Process(double delta)
	{
		if (!IsActive || _origin == null || _head == null) return;
		Player? player = _world.Players?.LocalPlayer;
		if (player != null)
		{
			_origin.GlobalPosition = player.GDNode3D.GlobalPosition;
			if (player.Character != null)
			{
				player.Character.XRHeadPose = HeadPose;
				player.Character.XRLeftHandPose = LeftHandPose;
				player.Character.XRRightHandPose = RightHandPose;
			}
		}
		_head.MakeCurrent();
		ApplySnapTurn();
		BridgeGameplayActions();
	}

	private void BridgeGameplayActions()
	{
		if (_left == null || _right == null) return;
		SetAction("activate", _right.IsButtonPressed("trigger_click") || RightTrigger > 0.75f, ref _activatePressed);
		SetAction("interact", _left.IsButtonPressed("grip_click") || LeftGrip > 0.75f, ref _interactPressed);
		SetAction("toggle_menu", _right.IsButtonPressed("by_button") || _left.IsButtonPressed("menu_button"), ref _menuPressed);
		SetAction("jump", JumpPressed, ref _jumpPressed);
	}

	private static void SetAction(string action, bool pressed, ref bool previous)
	{
		if (pressed == previous) return;
		previous = pressed;
		Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed, Strength = pressed ? 1f : 0f });
	}

	private void ApplySnapTurn()
	{
		if (_right == null || _origin == null) return;
		float turn = _right.GetVector2("primary").X;
		if (Mathf.Abs(turn) < 0.45f) { _snapTurnLatch = 0; return; }
		if (_snapTurnLatch != 0) return;
		_snapTurnLatch = Mathf.Sign(turn);
		_origin.RotateY(Mathf.DegToRad(-30f * _snapTurnLatch));
	}

	public void Recenter() => XRServer.CenterOnHmd(XRServer.RotationMode.ResetButKeepTilt, true);

	public void NavigateTo(Transform3D destination)
	{
		if (_origin == null) return;
		Player? player = _world.Players?.LocalPlayer;
		if (player != null) player.Position = destination.Origin;
		_origin.GlobalTransform = new Transform3D(destination.Basis, destination.Origin);
	}

	public void Pulse(bool leftHand, float amplitude, float durationSeconds, float frequency)
	{
		XRController3D? controller = leftHand ? _left : _right;
		controller?.TriggerHapticPulse("haptic", frequency, Mathf.Clamp(amplitude, 0f, 1f), Mathf.Max(durationSeconds, 0f), 0f);
	}

	public override void _ExitTree()
	{
		if (IsActive) GetViewport().UseXR = false;
		IsActive = false;
	}
}

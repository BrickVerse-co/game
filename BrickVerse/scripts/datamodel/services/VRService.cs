// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Client.XR;
using BrickVerse.Scripting;
using BrickVerse.Networking;
using Godot;
using BrickVerse.Schemas.Debugger;

namespace BrickVerse.Datamodel.Services;

/// <summary>UGC-facing access to the active OpenXR headset and controllers.</summary>
[Static("VRService"), SaveIgnore]
public sealed partial class VRService : Instance
{
	internal static bool CreatorEmulationEnabled { get; private set; }
	internal static float CreatorLeftTrigger { get; private set; }
	internal static float CreatorRightTrigger { get; private set; }
	internal static float CreatorLeftGrip { get; private set; }
	internal static float CreatorRightGrip { get; private set; }
	private static Transform3D _fakeHead = Transform3D.Identity;
	private static Transform3D _fakeLeft = Transform3D.Identity;
	private static Transform3D _fakeRight = Transform3D.Identity;
	private readonly Transform3D[] _lastPoses = [Transform3D.Identity, Transform3D.Identity, Transform3D.Identity];
	private readonly bool[] _lastEnabled = new bool[3];
	private readonly VRTouchpadModeEnum[] _touchpadModes = [VRTouchpadModeEnum.VirtualThumbstick, VRTouchpadModeEnum.VirtualThumbstick];
	private bool _avatarGestures = true;

	[ScriptEnum] public enum UserCFrameEnum { Head, LeftHand, RightHand }
	[ScriptEnum] public enum VRScalingEnum { Off, World }
	[ScriptEnum] public enum VRControllerModelModeEnum { Off, Transparent, Solid }
	[ScriptEnum] public enum VRLaserPointerModeEnum { Disabled, Pointer, Navigation }
	[ScriptEnum] public enum VRTouchpadEnum { Left, Right }
	[ScriptEnum] public enum VRTouchpadModeEnum { Touch, VirtualThumbstick, ABXY }

	[ScriptProperty] public bool VREnabled => CreatorEmulationEnabled || Runtime?.IsActive == true;
	[ScriptProperty] public bool VRAvailable => CreatorEmulationEnabled || XRRuntime.WasRequested() || XRServer.FindInterface("OpenXR") != null;
	[ScriptProperty] public string DeviceName => CreatorEmulationEnabled ? "Creator VR Emulator" : Runtime?.RuntimeName ?? "";
	[ScriptProperty] public bool ThirdPersonFollowCamEnabled => false;
	[ScriptProperty] public UserCFrameEnum GuiInputUserCFrame { get; set; } = UserCFrameEnum.RightHand;
	[Editable, ScriptProperty] public VRScalingEnum AutomaticScaling { get; set; } = VRScalingEnum.World;
	[Editable, ScriptProperty] public VRControllerModelModeEnum ControllerModels { get; set; } = VRControllerModelModeEnum.Transparent;
	[Editable, ScriptProperty] public VRLaserPointerModeEnum LaserPointer { get; set; } = VRLaserPointerModeEnum.Pointer;
	[Editable, ScriptProperty] public bool FadeOutViewOnCollision { get; set; } = true;
	[Editable, ScriptProperty, SyncVar] public bool AvatarGestures
	{
		get => _avatarGestures;
		set { _avatarGestures = value; OnPropertyChanged(); }
	}

	[ScriptProperty] public BVSignal<UserCFrameEnum, Transform3D> UserCFrameChanged { get; private set; } = new();
	[ScriptProperty] public BVSignal<UserCFrameEnum, bool> UserCFrameEnabled { get; private set; } = new();
	[ScriptProperty] public BVSignal<Transform3D, UserCFrameEnum> NavigationRequested { get; private set; } = new();
	[ScriptProperty] public BVSignal<bool> VREnabledChanged { get; private set; } = new();
	[ScriptProperty] public BVSignal<VRTouchpadEnum, VRTouchpadModeEnum> TouchpadModeChanged { get; private set; } = new();

	private XRRuntime? Runtime => Root?.Input?.XR;
	private bool _lastVrEnabled;

	internal static void ApplyCreatorEmulation(MessageRuntimeDeviceEmulation state)
	{
		CreatorEmulationEnabled = state.Enabled && state.VR;
		CreatorLeftTrigger = state.LeftTrigger ? 1f : 0f;
		CreatorRightTrigger = state.RightTrigger ? 1f : 0f;
		CreatorLeftGrip = state.PrimaryButton ? 1f : 0f;
		CreatorRightGrip = state.SecondaryButton ? 1f : 0f;
		Basis headBasis = Basis.FromEuler(new Vector3(0, state.HeadYaw, 0));
		_fakeHead = new Transform3D(headBasis, new Vector3(0, state.HeadHeight, 0));
		_fakeLeft = new Transform3D(headBasis, new Vector3(-state.HandSpread, state.HeadHeight - 0.35f, -0.35f));
		_fakeRight = new Transform3D(headBasis, new Vector3(state.HandSpread, state.HeadHeight - 0.35f, -0.35f));
	}

	public override void Init()
	{
		SetProcess(true);
		base.Init();
	}

	public override void Process(double delta)
	{
		bool enabled = VREnabled;
		if (enabled != _lastVrEnabled) { _lastVrEnabled = enabled; VREnabledChanged.Invoke(enabled); }
		for (int index = 0; index < 3; index++)
		{
			UserCFrameEnum type = (UserCFrameEnum)index;
			bool tracking = GetUserCFrameEnabled(type);
			if (tracking != _lastEnabled[index]) { _lastEnabled[index] = tracking; UserCFrameEnabled.Invoke(type, tracking); }
			if (!tracking) continue;
			Transform3D pose = GetUserCFrame(type);
			if (!pose.IsEqualApprox(_lastPoses[index])) { _lastPoses[index] = pose; UserCFrameChanged.Invoke(type, pose); }
		}
		base.Process(delta);
	}

	[ScriptMethod]
	public Transform3D GetUserCFrame(UserCFrameEnum type) => type switch
	{
		UserCFrameEnum.LeftHand => CreatorEmulationEnabled ? _fakeLeft : Runtime?.LeftHandPose ?? Transform3D.Identity,
		UserCFrameEnum.RightHand => CreatorEmulationEnabled ? _fakeRight : Runtime?.RightHandPose ?? Transform3D.Identity,
		_ => CreatorEmulationEnabled ? _fakeHead : Runtime?.HeadPose ?? Transform3D.Identity,
	};

	[ScriptMethod]
	public bool GetUserCFrameEnabled(UserCFrameEnum type) => type switch
	{
		UserCFrameEnum.LeftHand => CreatorEmulationEnabled || Runtime?.IsLeftHandTracked == true,
		UserCFrameEnum.RightHand => CreatorEmulationEnabled || Runtime?.IsRightHandTracked == true,
		_ => CreatorEmulationEnabled || Runtime?.IsHeadTracked == true,
	};

	[ScriptMethod] public void RecenterUserHeadCFrame() => Runtime?.Recenter();

	[ScriptMethod]
	public VRTouchpadModeEnum GetTouchpadMode(VRTouchpadEnum pad) => _touchpadModes[(int)pad];

	[ScriptMethod]
	public void SetTouchpadMode(VRTouchpadEnum pad, VRTouchpadModeEnum mode)
	{
		int index = (int)pad;
		if (_touchpadModes[index] == mode) return;
		_touchpadModes[index] = mode;
		TouchpadModeChanged.Invoke(pad, mode);
	}

	[ScriptMethod]
	public void RequestNavigation(Transform3D cframe, UserCFrameEnum inputUserCFrame = UserCFrameEnum.Head)
	{
		NavigationRequested.Invoke(cframe, inputUserCFrame);
		Runtime?.NavigateTo(cframe);
	}

	[ScriptMethod]
	public void PulseController(UserCFrameEnum controller, float amplitude = 0.5f, float durationSeconds = 0.1f, float frequency = 0f)
	{
		if (controller == UserCFrameEnum.Head) return;
		Runtime?.Pulse(controller == UserCFrameEnum.LeftHand, amplitude, durationSeconds, frequency);
	}
}

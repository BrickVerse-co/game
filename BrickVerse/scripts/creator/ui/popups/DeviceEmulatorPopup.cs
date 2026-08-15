using Godot;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Schemas.Debugger;
using BrickVerse.Creator.UI;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class DeviceEmulatorPopup : Window
{
	private const string ScenePath = "res://scenes/creator/popups/device_emulator.tscn";
	[Export] private OptionButton _profile = null!;
	[Export] private OptionButton _screen = null!;
	[Export] private CheckButton _enabled = null!;
	[Export] private CheckButton _preview = null!;
	[Export] private HSlider _leftX = null!;
	[Export] private HSlider _leftY = null!;
	[Export] private HSlider _rightX = null!;
	[Export] private HSlider _rightY = null!;
	[Export] private HSlider _headYaw = null!;
	[Export] private HSlider _headHeight = null!;
	[Export] private HSlider _handSpread = null!;
	[Export] private CheckButton _primary = null!;
	[Export] private CheckButton _secondary = null!;
	[Export] private CheckButton _leftTrigger = null!;
	[Export] private CheckButton _rightTrigger = null!;
	[Export] private Label _status = null!;
	private DevicePreviewGuide? _guide;

	public static void Open()
	{
		DeviceEmulatorPopup popup = GD.Load<PackedScene>(ScenePath).Instantiate<DeviceEmulatorPopup>();
		CreatorGUIRoot.Singleton.AddChild(popup);
		popup.PopupCentered(new Vector2I(590, 720));
	}

	public override void _Ready()
	{
		CloseRequested += QueueFree;
		_enabled.Toggled += _ => Send();
		_profile.ItemSelected += _ => { ApplyProfileDefaults(); Send(); };
		_screen.ItemSelected += _ => UpdateGuide();
		_preview.Toggled += _ => UpdateGuide();
		foreach (Range slider in new Range[] { _leftX, _leftY, _rightX, _rightY, _headYaw, _headHeight, _handSpread }) slider.ValueChanged += _ => Send();
		foreach (CheckButton button in new[] { _primary, _secondary, _leftTrigger, _rightTrigger }) button.Toggled += _ => Send();
		ApplyProfileDefaults();
	}

	private void ApplyProfileDefaults()
	{
		_screen.Select(_profile.Selected switch { 1 => 1, 2 => 2, 3 => 3, _ => 0 });
		UpdateGuide();
	}

	private MessageRuntimeDeviceEmulation State() => new()
	{
		Enabled = _enabled.ButtonPressed,
		DeviceType = _profile.Selected switch { 1 => "Phone", 2 => "Tablet", 3 => "Console", 4 => "VR", _ => "PC" },
		Touchscreen = _profile.Selected is 1 or 2,
		Gamepad = _profile.Selected is 3 or 4,
		VR = _profile.Selected == 4,
		LeftX = (float)_leftX.Value,
		LeftY = (float)_leftY.Value,
		RightX = (float)_rightX.Value,
		RightY = (float)_rightY.Value,
		PrimaryButton = _primary.ButtonPressed,
		SecondaryButton = _secondary.ButtonPressed,
		LeftTrigger = _leftTrigger.ButtonPressed,
		RightTrigger = _rightTrigger.ButtonPressed,
		HeadYaw = Mathf.DegToRad((float)_headYaw.Value),
		HeadHeight = (float)_headHeight.Value,
		HandSpread = (float)_handSpread.Value,
	};

	private void Send()
	{
		bool sent = CreatorService.Singleton.ApplyDeviceEmulation(State());
		_status.Text = sent ? "Applied live to the active play-test client." : "Start Play Test to inject input. Screen preview works in edit mode.";
		_status.Modulate = sent ? new Color(0.4f, 0.9f, 0.62f) : new Color(1f, 0.72f, 0.35f);
	}

	private void UpdateGuide()
	{
		WorldContainer? container = Tabs.Singleton?.CurrentWorldContainer;
		if (container == null) return;
		_guide ??= container.GetNodeOrNull<DevicePreviewGuide>("DevicePreviewGuide");
		if (_guide == null)
		{
			_guide = new DevicePreviewGuide { Name = "DevicePreviewGuide", MouseFilter = Control.MouseFilterEnum.Ignore, LayoutMode = 1 };
			_guide.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			container.AddChild(_guide);
			_guide.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		}
		Vector2I size = _screen.Selected switch { 1 => new(390, 844), 2 => new(1024, 1366), 3 => new(1920, 1080), 4 => new(1280, 720), _ => new(0, 0) };
		_guide.SetTarget(_preview.ButtonPressed ? size : Vector2I.Zero);
	}

	public override void _ExitTree()
	{
		if (_guide != null && IsInstanceValid(_guide)) _guide.SetTarget(Vector2I.Zero);
	}
}

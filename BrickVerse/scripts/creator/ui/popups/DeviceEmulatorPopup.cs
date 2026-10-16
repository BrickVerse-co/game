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
	private VBoxContainer _gamepadPanel = null!;
	private VBoxContainer _vrPanel = null!;
	private GamepadInputSimulator _gamepad = null!;
	private VRInputSimulator _vr = null!;

	public static void Open()
	{
		DeviceEmulatorPopup popup = GD.Load<PackedScene>(ScenePath).Instantiate<DeviceEmulatorPopup>();
		CreatorGUIRoot.Singleton.AddChild(popup);
		popup.PopupCentered(new Vector2I(640, 820));
	}

	public override void _Ready()
	{
		CloseRequested += QueueFree;
		BuildVisualSimulators();
		_enabled.Toggled += _ => Send();
		_profile.ItemSelected += _ => { ApplyProfileDefaults(); Send(); };
		_screen.ItemSelected += _ => UpdateGuide();
		_preview.Toggled += _ => UpdateGuide();
		ApplyProfileDefaults();
	}

	private void BuildVisualSimulators()
	{
		Control content = _status.GetParent<Control>();
		content.GetNode<Control>("GamepadTitle").Visible = false;
		content.GetNode<Control>("GamepadGrid").Visible = false;
		content.GetNode<Control>("Buttons").Visible = false;
		content.GetNode<Control>("VRTitle").Visible = false;
		content.GetNode<Control>("VRGrid").Visible = false;

		_gamepadPanel = SimulatorPanel("GAMEPAD INPUT", "Interactive dual-stick controller");
		_gamepad = new GamepadInputSimulator(); _gamepad.Changed += Send; _gamepadPanel.AddChild(_gamepad);
		Button resetGamepad = new() { Text = "Reset gamepad", FocusMode = Control.FocusModeEnum.None }; resetGamepad.Pressed += _gamepad.Reset; _gamepadPanel.AddChild(resetGamepad);
		content.AddChild(_gamepadPanel); content.MoveChild(_gamepadPanel, _status.GetIndex());

		_vrPanel = SimulatorPanel("VR POSE", "Headset and tracked hand controllers");
		_vr = new VRInputSimulator(); _vr.Changed += Send; _vrPanel.AddChild(_vr);
		Button resetVr = new() { Text = "Reset VR pose", FocusMode = Control.FocusModeEnum.None }; resetVr.Pressed += _vr.Reset; _vrPanel.AddChild(resetVr);
		content.AddChild(_vrPanel); content.MoveChild(_vrPanel, _status.GetIndex());
	}

	private static VBoxContainer SimulatorPanel(string title, string subtitle)
	{
		VBoxContainer panel = new(); panel.AddThemeConstantOverride("separation", 6);
		panel.AddChild(new Label { Text = title, ThemeTypeVariation = "HeaderSmall" });
		panel.AddChild(new Label { Text = subtitle, Modulate = new Color("93a4ba") });
		return panel;
	}

	private void ApplyProfileDefaults()
	{
		_screen.Select(_profile.Selected switch { 1 => 1, 2 => 2, 3 => 3, _ => 0 });
		_gamepadPanel.Visible = _profile.Selected == 3;
		_vrPanel.Visible = _profile.Selected == 4;
		UpdateGuide();
	}

	private MessageRuntimeDeviceEmulation State() => new()
	{
		Enabled = _enabled.ButtonPressed,
		DeviceType = _profile.Selected switch { 1 => "Phone", 2 => "Tablet", 3 => "Console", 4 => "VR", _ => "PC" },
		Touchscreen = _profile.Selected is 1 or 2,
		Gamepad = _profile.Selected is 3 or 4,
		VR = _profile.Selected == 4,
		LeftX = _gamepad.LeftStick.X,
		LeftY = _gamepad.LeftStick.Y,
		RightX = _gamepad.RightStick.X,
		RightY = _gamepad.RightStick.Y,
		PrimaryButton = _gamepad.Primary,
		SecondaryButton = _gamepad.Secondary,
		LeftTrigger = _gamepad.LeftTrigger,
		RightTrigger = _gamepad.RightTrigger,
		HeadYaw = Mathf.DegToRad(_vr.HeadYawDegrees),
		HeadHeight = _vr.HeadHeight,
		HandSpread = _vr.HandSpread,
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

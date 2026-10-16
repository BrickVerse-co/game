// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Creator.UI.Popups;
using System.Linq;

namespace BrickVerse.Creator.UI;

public partial class WorldContainerOverlay : Control
{
	private const string SettingsPath = "user://creator/viewport_ui.cfg";
	private const int MenuToolbar = 10, MenuAxis = 11, MenuStats = 12, MenuFrame = 20, MenuProjection = 21, MenuSceneStatistics = 30;
	public World World = null!;
	public WorldContainer Container = null!;
	private Label _viewportStatus = null!;
	private Button _projectionButton = null!;
	private MenuButton _viewModeButton = null!;
	private SpinBox _cameraSpeed = null!;
	private Label _statsLabel = null!;
	private PanelContainer _toolbarPanel = null!;
	private PanelContainer _transformTipsPanel = null!;
	private Label _transformTips = null!;
	private Control _viewportAxis = null!;
	private MenuButton _optionsButton = null!;
	private bool _showToolbar;
	private bool _showAxis = true;
	private bool _showStats;
	private double _statusRefresh;

	public override void _Ready()
	{
		LoadSettings();
		_viewportAxis = GetNode<Control>("ViewportAxis");
		CreateViewportToolbar();
		CreateTransformTips();
		ApplyVisibility();
		base._Ready();
	}

	private void CreateTransformTips()
	{
		_transformTips = new Label
		{
			MouseFilter = MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Left,
			Modulate = new Color(0.86f, 0.89f, 0.94f),
		};
		_transformTips.AddThemeFontSizeOverride("font_size", 12);
		_transformTipsPanel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
		_transformTipsPanel.SetAnchorsPreset(LayoutPreset.BottomRight);
		_transformTipsPanel.GrowHorizontal = GrowDirection.Begin;
		_transformTipsPanel.GrowVertical = GrowDirection.Begin;
		_transformTipsPanel.OffsetLeft = -310;
		_transformTipsPanel.OffsetTop = -96;
		_transformTipsPanel.OffsetRight = -14;
		_transformTipsPanel.OffsetBottom = -14;
		_transformTipsPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.045f, 0.05f, 0.06f, 0.88f),
			BorderColor = new Color(0.2f, 0.24f, 0.3f, 0.9f),
			BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
			CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
			CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
			ContentMarginLeft = 10, ContentMarginRight = 10,
			ContentMarginTop = 7, ContentMarginBottom = 7,
		});
		_transformTipsPanel.AddChild(_transformTips);
		AddChild(_transformTipsPanel);
	}

	public override void _Process(double delta)
	{
		_statusRefresh -= delta;
		if (_statusRefresh <= 0)
		{
			_statusRefresh = 0.2;
			RefreshStatus();
		}
		base._Process(delta);
	}

	private void CreateViewportToolbar()
	{
		_optionsButton = new MenuButton
		{
			Text = "View  •••",
			TooltipText = "Viewport display, camera, and scene statistics",
			Position = new Vector2(12, 12),
			CustomMinimumSize = new Vector2(82, 32),
			MouseFilter = MouseFilterEnum.Stop,
			Flat = false,
		};
		_optionsButton.AddThemeColorOverride("font_color", new Color("dce6f3"));
		_optionsButton.AddThemeFontSizeOverride("font_size", 12);
		AddChild(_optionsButton);
		PopupMenu options = _optionsButton.GetPopup();
		options.AddCheckItem("Show viewport toolbar", MenuToolbar);
		options.AddCheckItem("Show orientation cube", MenuAxis);
		options.AddCheckItem("Show diagnostics", MenuStats);
		options.AddSeparator();
		options.AddItem("Frame selection", MenuFrame);
		options.AddItem("Toggle perspective / orthographic", MenuProjection);
		options.AddSeparator();
		options.AddItem("Scene statistics…", MenuSceneStatistics);
		options.IdPressed += OnOptionsPressed;

		HBoxContainer toolbar = new()
		{
			Name = "ViewportToolbar",
			CustomMinimumSize = new Vector2(0, 36),
			MouseFilter = MouseFilterEnum.Stop,
		};
		toolbar.AddThemeConstantOverride("separation", 4);

		_toolbarPanel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
		_toolbarPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.055f, 0.06f, 0.07f, 0.94f),
			BorderColor = new Color(0.24f, 0.27f, 0.31f, 1f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
			ContentMarginLeft = 6,
			ContentMarginRight = 6,
			ContentMarginTop = 4,
			ContentMarginBottom = 4,
		});
		_toolbarPanel.AddChild(toolbar);
		AddChild(_toolbarPanel);
		_toolbarPanel.Position = new Vector2(102, 12);

		Button frame = ToolButton("Frame", "Frame selected objects (F)");
		frame.Pressed += () => World.CreatorContext.Freelook.MoveToSelected();
		toolbar.AddChild(frame);

		_projectionButton = ToolButton("Perspective", "Toggle perspective/orthographic projection");
		_projectionButton.Pressed += ToggleProjection;
		toolbar.AddChild(_projectionButton);

		_viewModeButton = new MenuButton { Text = "Lit", TooltipText = "Viewport shading mode", Flat = true };
		_viewModeButton.GetPopup().AddItem("Lit", 0);
		_viewModeButton.GetPopup().AddItem("Unlit", 1);
		_viewModeButton.GetPopup().AddItem("Wireframe", 2);
		_viewModeButton.GetPopup().AddItem("Overdraw", 3);
		_viewModeButton.GetPopup().IdPressed += SetViewMode;
		toolbar.AddChild(_viewModeButton);
		Button stats = ToolButton("Stats", "Toggle viewport diagnostics");
		stats.ToggleMode = true;
		stats.Toggled += enabled => { _showStats = enabled; ApplyVisibility(); SaveSettings(); };
		toolbar.AddChild(stats);

		toolbar.AddChild(new VSeparator());
		Label speedLabel = new() { Text = "Camera", VerticalAlignment = VerticalAlignment.Center, Modulate = new Color(0.75f, 0.78f, 0.82f) };
		toolbar.AddChild(speedLabel);
		_cameraSpeed = new SpinBox { MinValue = 2, MaxValue = 1024, Value = World.CreatorContext.Freelook.MoveSpeed, Step = 2, CustomMinimumSize = new Vector2(76, 0), TooltipText = "Viewport camera movement speed" };
		_cameraSpeed.ValueChanged += value => World.CreatorContext.Freelook.SetCreatorMoveSpeed((float)value);
		World.CreatorContext.Freelook.MoveSpeedChanged += OnCameraSpeedChanged;
		toolbar.AddChild(_cameraSpeed);

		toolbar.AddChild(new VSeparator());
		_viewportStatus = new Label { VerticalAlignment = VerticalAlignment.Center, Modulate = new Color(0.68f, 0.72f, 0.77f), TooltipText = "Selection and snapping status" };
		toolbar.AddChild(_viewportStatus);

		_statsLabel = new Label
		{
			Visible = false,
			Position = new Vector2(16, 60),
			MouseFilter = MouseFilterEnum.Ignore,
			Modulate = new Color(0.88f, 0.9f, 0.94f),
		};
		_statsLabel.AddThemeFontSizeOverride("font_size", 12);
		_statsLabel.AddThemeColorOverride("font_shadow_color", Colors.Black);
		_statsLabel.AddThemeConstantOverride("shadow_offset_x", 1);
		_statsLabel.AddThemeConstantOverride("shadow_offset_y", 1);
		AddChild(_statsLabel);
		RefreshStatus();
	}

	private void OnOptionsPressed(long id)
	{
		switch (id)
		{
			case MenuToolbar: _showToolbar = !_showToolbar; break;
			case MenuAxis: _showAxis = !_showAxis; break;
			case MenuStats: _showStats = !_showStats; break;
			case MenuFrame: World.CreatorContext.Freelook.MoveToSelected(); return;
			case MenuProjection: ToggleProjection(); return;
			case MenuSceneStatistics: SceneStatisticsPopup.Open(); return;
			default: return;
		}
		ApplyVisibility();
		SaveSettings();
	}

	private void ApplyVisibility()
	{
		if (_toolbarPanel != null) _toolbarPanel.Visible = _showToolbar;
		if (_viewportAxis != null) _viewportAxis.Visible = _showAxis;
		if (_statsLabel != null) _statsLabel.Visible = _showStats;
		if (_optionsButton == null) return;
		PopupMenu popup = _optionsButton.GetPopup();
		popup.SetItemChecked(popup.GetItemIndex(MenuToolbar), _showToolbar);
		popup.SetItemChecked(popup.GetItemIndex(MenuAxis), _showAxis);
		popup.SetItemChecked(popup.GetItemIndex(MenuStats), _showStats);
	}

	private void LoadSettings()
	{
		ConfigFile config = new();
		if (config.Load(SettingsPath) != Error.Ok) return;
		_showToolbar = config.GetValue("viewport", "toolbar", false).AsBool();
		_showAxis = config.GetValue("viewport", "orientation_cube", true).AsBool();
		_showStats = config.GetValue("viewport", "diagnostics", false).AsBool();
	}

	private void SaveSettings()
	{
		ConfigFile config = new();
		config.SetValue("viewport", "toolbar", _showToolbar);
		config.SetValue("viewport", "orientation_cube", _showAxis);
		config.SetValue("viewport", "diagnostics", _showStats);
		config.Save(SettingsPath);
	}

	private static Button ToolButton(string text, string tooltip) => new()
	{
		Text = text,
		TooltipText = tooltip,
		Flat = true,
		FocusMode = FocusModeEnum.None,
	};

	private void ToggleProjection()
	{
		BrickVerse.Datamodel.Camera camera = World.CreatorContext.Freelook;
		camera.Orthographic = !camera.Orthographic;
		_projectionButton.Text = camera.Orthographic ? "Orthographic" : "Perspective";
	}

	private void SetViewMode(long id)
	{
		Container.RenderViewport.DebugDraw = id switch
		{
			1 => Viewport.DebugDrawEnum.Unshaded,
			2 => Viewport.DebugDrawEnum.Wireframe,
			3 => Viewport.DebugDrawEnum.Overdraw,
			_ => Viewport.DebugDrawEnum.Disabled,
		};
		_viewModeButton.Text = id switch { 1 => "Unlit", 2 => "Wireframe", 3 => "Overdraw", _ => "Lit" };
	}

	private void OnCameraSpeedChanged(float speed) => _cameraSpeed.SetValueNoSignal(speed);

	private void RefreshStatus()
	{
		if (World == null
			|| World.IsDeleted
			|| World.GDNode == null
			|| !IsInstanceValid(World.GDNode)
			|| World.CreatorContext?.Selections == null
			|| World.CreatorContext.Freelook == null
			|| _viewportStatus == null
			|| _statsLabel == null)
			return;
		int selected = World.CreatorContext.Selections.SelectedInstances.Count;
		UIField? selectedUI = World.CreatorContext.Selections.SelectedInstances.OfType<UIField>().FirstOrDefault();
		string selection = selected == 0 ? "No selection" : $"{selected} selected";
		string moveSnap = CreatorService.Interface.MoveSnapEnabled ? $"Move {CreatorService.Interface.UserMoveSnapping:g}m" : "Move snap off";
		string rotateSnap = CreatorService.Interface.RotateSnapEnabled ? $"Rotate {CreatorService.Interface.UserRotateSnapping:g}°" : "Rotate snap off";
		_viewportStatus.Text = $"{selection}   |   {moveSnap}   |   {rotateSnap}";
		Vector3 cameraPosition = World.CreatorContext.Freelook.Position;
		_statsLabel.Text = $"{Engine.GetFramesPerSecond()} FPS\n{World.GetDescendants().Length:n0} instances\nCamera  {cameraPosition.X:0.0}, {cameraPosition.Y:0.0}, {cameraPosition.Z:0.0}";

		ToolModeEnum tool = CreatorService.Interface.ToolMode;
		_transformTipsPanel.Visible = CreatorService.Interface.ShowTransformTips && selected > 0
			&& (selectedUI != null || tool is ToolModeEnum.Move or ToolModeEnum.Rotate or ToolModeEnum.Scale or ToolModeEnum.Pivot);
		_transformTips.Text = selectedUI != null
			? $"UI LAYOUT  •  {selectedUI.AbsoluteSize.X:0} × {selectedUI.AbsoluteSize.Y:0}px\nPosition  {selectedUI.PositionRelative.X * 100:0.#}%, {selectedUI.PositionRelative.Y * 100:0.#}%  •  Size  {selectedUI.SizeRelative.X * 100:0.#}%, {selectedUI.SizeRelative.Y * 100:0.#}%\nAlt: measure edges  •  Shift: disable snapping  •  Arrow keys: nudge"
			: tool switch
		{
			ToolModeEnum.Move => "MOVE  •  Drag an axis handle\nTab + click places the pivot  •  Ruler lines show distance\n1 Select   2 Move   3 Rotate   4 Scale",
			ToolModeEnum.Rotate => "ROTATE  •  Drag a colored ring\nAlt temporarily reduces snapping\n1 Select   2 Move   3 Rotate   4 Scale",
			ToolModeEnum.Scale => "SCALE  •  Drag an axis handle\nShift scales every axis  •  Alt scales from center\n1 Select   2 Move   3 Rotate   4 Scale",
			ToolModeEnum.Pivot => "EDIT PIVOT  •  Drag arrows to move  •  Drag rings to rotate\nReset Pivot restores local position and rotation to 0, 0, 0",
			_ => ""
		};
	}

	public override void _ExitTree()
	{
		if (World?.CreatorContext?.Freelook != null)
			World.CreatorContext.Freelook.MoveSpeedChanged -= OnCameraSpeedChanged;
		base._ExitTree();
	}
}

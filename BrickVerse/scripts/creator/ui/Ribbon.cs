// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using BrickVerse.Utils;
using BrickVerse.Creator.TeamCreate;
using System;
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Creator.UI.Popups;

namespace BrickVerse.Creator.UI;

public sealed partial class Ribbon : Control
{
	[Export]
	private ButtonGroup _ribbonGroup = null!;

	private TabContainer _taskTabs = null!;

	private Button _selectButton = null!;
	private Button _moveButton = null!;
	private Button _rotateButton = null!;
	private Button _scaleButton = null!;
	private Button _brushButton = null!;
	private HBoxContainer _quickActions = null!;
	private HBoxContainer _codeEditorActions = null!;
	private Button _formatButton = null!;
	private Button _saveButton = null!;
	private Button _findButton = null!;
	private bool _showingCodeActions;
	private TabContainer? _rightTabs;
	private int _teamChatIndex = -1;

	public override void _Ready()
	{
		_taskTabs = GetNode<TabContainer>("Layout/TaskTabs");
		HBoxContainer home = _taskTabs.GetNode<HBoxContainer>("Home/Margin/Buttons");
		HBoxContainer model = _taskTabs.GetNode<HBoxContainer>("Model/Margin/Buttons");
		_quickActions = _taskTabs.GetNode<HBoxContainer>("Tools/Margin/Buttons");
		_codeEditorActions = _taskTabs.GetNode<HBoxContainer>("Script/Margin/Buttons");
		_selectButton = home.GetNode<Button>("Select");
		_moveButton = home.GetNode<Button>("Move");
		_rotateButton = home.GetNode<Button>("Rotate");
		_scaleButton = home.GetNode<Button>("Scale");
		_brushButton = model.GetNode<Button>("Brush");
		_brushButton.GuiInput += OnBrushGuiInput;
		_formatButton = _codeEditorActions.GetNode<Button>("Format");
		_saveButton = _codeEditorActions.GetNode<Button>("Save");
		_findButton = _codeEditorActions.GetNode<Button>("Find");
		_formatButton.Pressed += FormatActiveDocument;
		_saveButton.Pressed += SaveActiveDocument;
		_findButton.Pressed += FindInActiveDocument;

		Button colorButton = model.GetNode<Button>("Color");
		Control paintColorView = model.GetNode<Control>("Paint/Color");
		Button materialButton = model.GetNode<Button>("Material");
		Button insertButton = home.GetNode<Button>("Insert");
		Button forgeButton = _quickActions.GetNode<Button>("Forge");
		Button terrainButton = model.GetNode<Button>("Terrain");
		Button animatorButton = model.GetNode<Button>("Animator");
		Button toolboxButton = _quickActions.GetNode<Button>("Toolbox");
		Button shapesButton = model.GetNode<Button>("Shapes");
		Button inputManagerButton = _taskTabs.GetNode<Button>("UI/Margin/Buttons/InputManager");
		AddTaskAction("Find in Place", "search", FindInFilesPopup.Open);
		AddTaskAction("Data Stores", "database", () => CreatorDataToolsWindow.Open(0));
		AddTaskAction("Localization", "translate", () => CreatorDataToolsWindow.Open(1));
		AddTaskAction("Instance Icons", "image-square", () => CreatorDataToolsWindow.Open(2));
		AddTaskAction("Backups", "history", () => CreatorDataToolsWindow.Open(3));
		AddTaskAction("Collisions", "brick", () => CreatorDataToolsWindow.Open(4));
		AddTaskAction("Analysis", "bug", () => CreatorDataToolsWindow.Open(5));
		AddTaskAction("Scene Stats", "chart-bar", SceneStatisticsPopup.Open);
		AddTaskAction("Particles", "play-filled", ParticleEditorWindow.Open);
		AddTaskAction("Input", "keyboard", CreatorService.Interface.OpenInputManager);
		PopulateShapesMenu(shapesButton);

		StyleBoxFlat colorPreview = (StyleBoxFlat)colorButton.GetNode<Panel>("Preview").GetThemeStylebox("panel");
		colorButton.Pressed += () =>
		{
			ColorPicker.Singleton.SwitchTo(colorButton, colorPreview.BgColor, value =>
			{
				colorPreview.BgColor = value;
				paintColorView.Modulate = value;
				CreatorService.Interface.TargetPartColor = value;
			});
		};

		TextureRect materialPreview = materialButton.GetNode<TextureRect>("Preview/Texture");

		PopupPanel materialPopup = materialButton.GetNode<PopupPanel>("Popup");
		Control materialPopupSpawn = materialButton.GetNode<Control>("PopupSpawn");
		ItemList materialContainer = materialPopup.GetNode<ItemList>("Container");

		foreach (string name in Enum.GetNames<Part.PartMaterialEnum>())
		{
			string previewPath = "res://assets/textures/parts/".PathJoin(name).PathJoin("albedo.jpg");
			Texture2D? previewTex = null;
			if (ResourceLoader.Exists(previewPath))
			{
				previewTex = GD.Load<Texture2D>(previewPath);
			}
			materialContainer.AddItem(name, previewTex);
		}
		materialContainer.ItemSelected += idx =>
		{
			materialPreview.Texture = materialContainer.GetItemIcon((int)idx);
			string materialName = materialContainer.GetItemText((int)idx);
			if (Enum.TryParse(typeof(Part.PartMaterialEnum), materialName, out object? PartMaterialEnum))
			{
				CreatorService.Interface.TargetPartMaterial = (Part.PartMaterialEnum)PartMaterialEnum;
			}
		};

		materialButton.Pressed += () =>
		{
			Vector2 popupPosition = materialPopupSpawn.GlobalPosition;
			Rect2I rect = new()
			{
				Position = (Vector2I)popupPosition,
				Size = materialPopup.Size
			};
			materialPopup.Popup(rect);
		};
		materialContainer.Select(0);

		insertButton.Pressed += () =>
		{
			CreatorService.Interface.OpenInsertMenu();
		};

		TabContainer leftTabs = GetNode<TabContainer>(
			"../Splitter/Left/Split");
		TabContainer bottomTabs = GetNode<TabContainer>(
			"../Splitter/Center/BottomTabs/Tabs");
		TabContainer rightTabs = GetNode<TabContainer>(
			"../Splitter/Right/RightTabs");
		if (rightTabs.GetNodeOrNull<BrickVerse.Creator.TeamCreate.TeamChatDock>("Team Chat") == null)
		{
			Node teamChat = GD.Load<PackedScene>("res://scenes/creator/docks/team_chat_dock.tscn").Instantiate();
			rightTabs.AddChild(teamChat);
			int teamChatIndex = rightTabs.GetTabCount() - 1;
			rightTabs.SetTabTitle(teamChatIndex, "Team Chat");
			rightTabs.SetTabIcon(teamChatIndex, GD.Load<Texture2D>("res://assets/textures/ui-icons/team-create.svg"));
		}
		_rightTabs = rightTabs;
		_teamChatIndex = rightTabs.GetNode<TeamChatDock>("Team Chat").GetIndex();
		if (TeamCreateService.Instance != null) TeamCreateService.Instance.StateChanged += RefreshTeamChatTab;
		RefreshTeamChatTab();

		forgeButton.Pressed += () => rightTabs.CurrentTab = 1;
		terrainButton.Pressed += () =>
		{
			bottomTabs.CurrentTab = 1;
			World.Current?.Container?.GrabFocus();
		};
		animatorButton.Pressed += CreatorService.Interface.OpenAnimationEditor;
		toolboxButton.Pressed += () => leftTabs.CurrentTab = 0;
		inputManagerButton.Pressed += CreatorService.Interface.OpenInputManager;
		Tabs.Singleton.CurrentControlChanged += OnCurrentControlChanged;
		OnCurrentControlChanged(Tabs.Singleton.CurrentControl);

		_ribbonGroup.Pressed += OnRibbonChanged;
	}

	private static TextEditor.TextEditorContainer? ActiveTextEditor() => Tabs.Singleton?.CurrentControl as TextEditor.TextEditorContainer;

	private void FormatActiveDocument() => ActiveTextEditor()?.EditorRoot.FormatDocument();
	private void SaveActiveDocument() => ActiveTextEditor()?.EditorRoot.SaveDocument();
	private void FindInActiveDocument() => ActiveTextEditor()?.EditorRoot.OpenFind();

	private void AddTaskAction(string label, string icon, Action action)
	{
		Button button = new()
		{
			Name = label.Replace(" ", ""),
			CustomMinimumSize = new Vector2(Mathf.Max(74, label.Length * 7), 54),
			TooltipText = label,
			FocusMode = Control.FocusModeEnum.None,
		};
		TextureRect iconView = new()
		{
			Texture = GD.Load<Texture2D>($"res://assets/textures/ui-icons/{icon}.svg"),
			Position = new Vector2(0, 5),
			Size = new Vector2(button.CustomMinimumSize.X, 27),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Modulate = new Color("0097ff"),
		};
		Label caption = new()
		{
			Text = label,
			Position = new Vector2(2, 33),
			Size = new Vector2(button.CustomMinimumSize.X - 4, 18),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		caption.AddThemeFontSizeOverride("font_size", 11);
		button.AddChild(iconView);
		button.AddChild(caption);
		button.Pressed += action;
		_quickActions.AddChild(button);
	}

	private void OnCurrentControlChanged(Control? control)
	{
		_showingCodeActions = control is TextEditor.TextEditorContainer;
		if (_showingCodeActions) _taskTabs.CurrentTab = 3;

	}

	private void PopulateShapesMenu(Button button)
	{
		PopupMenu popup = new() { Name = "ShapesPopup" };
		button.AddChild(popup);
		(string Label, string Icon, Part.ShapeEnum? Shape, bool Icosphere)[] shapes =
		[
			("Block", "block", Part.ShapeEnum.Brick, false),
			("Sphere", "sphere", Part.ShapeEnum.Sphere, false),
			("Cylinder", "cylinder", Part.ShapeEnum.Cylinder, false),
			("Cone", "cone", Part.ShapeEnum.Cone, false),
			("Wedge", "wedge", Part.ShapeEnum.Wedge, false),
			("Corner Wedge", "corner-wedge", Part.ShapeEnum.Corner, false),
			("Torus", "torus", Part.ShapeEnum.Torus, false),
			("Truss", "truss", Part.ShapeEnum.Truss, false),
			("Frame", "frame", Part.ShapeEnum.Frame, false),
			("Icosphere", "icosphere", null, true),
		];
		Dictionary<int, (Part.ShapeEnum? Shape, bool Icosphere, string Name)> entries = [];
		for (int i = 0; i < shapes.Length; i++)
		{
			var item = shapes[i];
			popup.AddIconItem(GD.Load<Texture2D>($"res://assets/textures/creator/ribbon/shapes/{item.Icon}.svg"), item.Label, i);
			entries[i] = (item.Shape, item.Icosphere, item.Label);
		}
		popup.IdPressed += id => { if (entries.TryGetValue((int)id, out var entry)) InsertShape(entry.Shape, entry.Icosphere, entry.Name); };
		button.Pressed += () =>
		{
			Control anchor = button;
			popup.Position = (Vector2I)(anchor.GlobalPosition + new Vector2(0, anchor.Size.Y));
			popup.Popup();
		};
	}

	private static void InsertShape(Part.ShapeEnum? shape, bool isIcosphere, string displayName)
	{
		World? world = World.Current; if (world == null) return;
		Entity entity;
		Part? insertedPart = null;
		if (isIcosphere) entity = Globals.LoadInstance<Icosphere>(world);
		else
		{
			insertedPart = Globals.LoadInstance<Part>(world);
			entity = insertedPart;
		}
		entity.Name = displayName; entity.CreatorInserted();
		world.CreatorContext.History.CreateInstances([entity], world.Environment);
		// Part.Ready() applies its default shape when the instance enters the tree,
		// so apply the requested primitive after parenting has completed.
		if (insertedPart != null) insertedPart.Shape = shape ?? Part.ShapeEnum.Brick;
		Datamodel.Environment.RayResult? hit = world.CreatorContext.Freelook.GetPlacementRay();
		entity.Position = hit != null
			? hit.Value.Position + hit.Value.Normal * (entity.CalculateBounds().Size.Y / 2f)
			: world.CreatorContext.Freelook.GetPlacementPosition();
		if (CreatorService.Interface.MoveSnapEnabled) entity.Position = entity.Position.Snap(CreatorService.Interface.MoveSnapping);
		CreatorBuildEffects.Emit(entity as Dynamic);
		world.CreatorContext.Selections.SelectOnly(entity);
		CreatorSoundEffects.PlayPlace();
		world.Container?.GrabFocus();
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(_brushButton)) _brushButton.GuiInput -= OnBrushGuiInput;

		if (Tabs.Singleton != null) Tabs.Singleton.CurrentControlChanged -= OnCurrentControlChanged;
		if (TeamCreateService.Instance != null) TeamCreateService.Instance.StateChanged -= RefreshTeamChatTab;
		base._ExitTree();
	}

	private void RefreshTeamChatTab()
	{
		if (_rightTabs == null || _teamChatIndex < 0) return;
		bool visible = TeamCreateService.Instance?.TeamCreateEnabled == true;
		_rightTabs.SetTabHidden(_teamChatIndex, !visible);
		if (!visible && _rightTabs.CurrentTab == _teamChatIndex) _rightTabs.CurrentTab = 0;
	}

	private void OnBrushGuiInput(InputEvent inputEvent)
	{
		if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
		if (!_brushButton.ButtonPressed || CreatorService.Interface.ToolMode != ToolModeEnum.Brush) return;

		_selectButton.ButtonPressed = true;
		CreatorService.Interface.ToolMode = ToolModeEnum.Select;
		_brushButton.AcceptEvent();
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		// Input can arrive while the node is entering or leaving the tree. The
		// cached controls are initialized in _Ready(), so ignore input until then.
		if (!IsNodeReady())
		{
			base._UnhandledKeyInput(@event);
			return;
		}

		if (CreatorKeybindResolver.IsPressed(@event, CreatorSettingKeys.Keybinds.ToolSelect, Key.Key1))
		{
			_selectButton.ButtonPressed = true;
		}
		else if (CreatorKeybindResolver.IsPressed(@event, CreatorSettingKeys.Keybinds.ToolMove, Key.Key2))
		{
			_moveButton.ButtonPressed = true;
		}
		else if (CreatorKeybindResolver.IsPressed(@event, CreatorSettingKeys.Keybinds.ToolRotate, Key.Key3))
		{
			_rotateButton.ButtonPressed = true;
		}
		else if (CreatorKeybindResolver.IsPressed(@event, CreatorSettingKeys.Keybinds.ToolScale, Key.Key4))
		{
			_scaleButton.ButtonPressed = true;
		}

		base._UnhandledKeyInput(@event);
	}

	private void OnRibbonChanged(BaseButton rawBtn)
	{
		RibbonToolButton btn = (RibbonToolButton)rawBtn;
		CreatorService.Interface.ToolMode = btn.ToolMode;
		switch (btn.ToolMode)
		{
			case ToolModeEnum.Paint:
			case ToolModeEnum.Brush:
				World.Current?.Container?.GrabFocus();
				break;
		}
	}
}

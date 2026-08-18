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
using System;
using System.Collections.Generic;

namespace BrickVerse.Creator.UI;

public sealed partial class Ribbon : PanelContainer
{
	[Export]
	private ButtonGroup _ribbonGroup = null!;

	private HFlowContainer _container = null!;

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
	private Button _moreActionsButton = null!;
	private PopupMenu _actionsPopup = null!;
	private readonly Dictionary<long, Button> _overflowActions = [];
	private bool _showingCodeActions;
	private bool _updatingOverflow;

	public override void _Ready()
	{
		_container = GetNode<HFlowContainer>("Buttons");

		_selectButton = _container.GetNode<Button>("Select");
		_moveButton = _container.GetNode<Button>("Move");
		_rotateButton = _container.GetNode<Button>("Rotate");
		_scaleButton = _container.GetNode<Button>("Scale");
		_brushButton = _container.GetNode<Button>("Brush");
		_brushButton.GuiInput += OnBrushGuiInput;
		_quickActions = _container.GetNode<HBoxContainer>("QuickActions");
		_codeEditorActions = _container.GetNode<HBoxContainer>("CodeEditorActions");
		_formatButton = _codeEditorActions.GetNode<Button>("Format");
		_saveButton = _codeEditorActions.GetNode<Button>("Save");
		_findButton = _codeEditorActions.GetNode<Button>("Find");
		_moreActionsButton = _container.GetNode<Button>("More");
		_formatButton.Pressed += FormatActiveDocument;
		_saveButton.Pressed += SaveActiveDocument;
		_findButton.Pressed += FindInActiveDocument;

		Button colorButton = _container.GetNode<Button>("Color");
		Control paintColorView = _container.GetNode<Control>("Paint/Color");
		Button materialButton = _container.GetNode<Button>("Material");
		Button insertButton = _container.GetNode<Button>("Insert");
		Button forgeButton = _container.GetNode<Button>("QuickActions/Forge");
		Button terrainButton = _container.GetNode<Button>("QuickActions/Terrain");
		Button animatorButton = _container.GetNode<Button>("QuickActions/Animator");
		Button toolboxButton = _container.GetNode<Button>("QuickActions/Toolbox");
		Button shapesButton = _container.GetNode<Button>("QuickActions/Shapes");
		Button inputManagerButton = _container.GetNode<Button>("QuickActions/InputManager");
		PopulateShapesMenu(shapesButton);

		StyleBoxFlat colorPreview = (StyleBoxFlat)colorButton.GetNode<Panel>("Preview").GetThemeStylebox("panel");
		colorButton.Pressed += () =>
		{
			Button anchor = colorButton.Visible ? colorButton : _moreActionsButton;
			ColorPicker.Singleton.SwitchTo(anchor, colorPreview.BgColor, value =>
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
			Vector2 popupPosition = materialButton.Visible
				? materialPopupSpawn.GlobalPosition
				: _moreActionsButton.GlobalPosition + new Vector2(0, _moreActionsButton.Size.Y);
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

		forgeButton.Pressed += () => rightTabs.CurrentTab = 1;
		terrainButton.Pressed += () =>
		{
			bottomTabs.CurrentTab = 1;
			World.Current?.Container?.GrabFocus();
		};
		animatorButton.Pressed += CreatorService.Interface.OpenAnimationEditor;
		toolboxButton.Pressed += () => leftTabs.CurrentTab = 0;
		inputManagerButton.Pressed += CreatorService.Interface.OpenInputManager;
		SetupRibbonOverflow();
		_container.Resized += QueueOverflowUpdate;
		Tabs.Singleton.CurrentControlChanged += OnCurrentControlChanged;
		OnCurrentControlChanged(Tabs.Singleton.CurrentControl);

		_ribbonGroup.Pressed += OnRibbonChanged;
	}

	private static TextEditor.TextEditorContainer? ActiveTextEditor() => Tabs.Singleton?.CurrentControl as TextEditor.TextEditorContainer;

	private void FormatActiveDocument() => ActiveTextEditor()?.EditorRoot.FormatDocument();
	private void SaveActiveDocument() => ActiveTextEditor()?.EditorRoot.SaveDocument();
	private void FindInActiveDocument() => ActiveTextEditor()?.EditorRoot.OpenFind();

	private void SetupRibbonOverflow()
	{
		_actionsPopup = new PopupMenu { Name = "RibbonActionsPopup" };
		_moreActionsButton.AddChild(_actionsPopup);
		_actionsPopup.IdPressed += id =>
		{
			if (!_overflowActions.TryGetValue(id, out Button? button)) return;
			if (button.ToggleMode) button.ButtonPressed = true;
			button.EmitSignal(BaseButton.SignalName.Pressed);
		};
		_moreActionsButton.Pressed += () =>
		{
			_actionsPopup.Position = (Vector2I)(_moreActionsButton.GlobalPosition + new Vector2(0, _moreActionsButton.Size.Y));
			_actionsPopup.Popup();
		};
	}

	private void QueueOverflowUpdate()
	{
		if (IsInsideTree()) CallDeferred(MethodName.UpdateRibbonOverflow);
	}

	private void UpdateRibbonOverflow()
	{
		if (_updatingOverflow || !IsInstanceValid(_container)) return;
		_updatingOverflow = true;
		_quickActions.Visible = !_showingCodeActions;
		_codeEditorActions.Visible = _showingCodeActions;

		List<Button> candidates = [];
		foreach (Node child in _container.GetChildren())
		{
			if (child is Button button && button != _moreActionsButton) candidates.Add(button);
		}
		HBoxContainer contextual = _showingCodeActions ? _codeEditorActions : _quickActions;
		foreach (Node child in contextual.GetChildren())
		{
			if (child is Button button) candidates.Add(button);
		}
		foreach (Button button in candidates) button.Visible = true;
		_moreActionsButton.Visible = false;

		float used = 4f;
		foreach (Node child in _container.GetChildren())
		{
			if (child is VSeparator separator && separator.Visible)
				used += Mathf.Max(separator.GetCombinedMinimumSize().X, 1f) + 4f;
		}
		foreach (Button button in candidates) used += Mathf.Max(button.CustomMinimumSize.X, button.GetCombinedMinimumSize().X) + 4f;

		float available = Mathf.Max(0f, _container.Size.X - 8f);
		List<Button> hidden = [];
		if (used > available)
		{
			used += Mathf.Max(_moreActionsButton.CustomMinimumSize.X, 48f) + 4f;
			for (int i = candidates.Count - 1; i >= 0 && used > available; i--)
			{
				Button button = candidates[i];
				button.Visible = false;
				hidden.Insert(0, button);
				used -= Mathf.Max(button.CustomMinimumSize.X, button.GetCombinedMinimumSize().X) + 4f;
			}
			_moreActionsButton.Visible = hidden.Count > 0;
		}

		_actionsPopup.Clear();
		_overflowActions.Clear();
		long id = 0;
		foreach (Button button in hidden)
		{
			string label = button.GetNodeOrNull<Label>("Label")?.Text ?? (!string.IsNullOrWhiteSpace(button.Text) ? button.Text : button.Name);
			Texture2D? icon = button.GetNodeOrNull<TextureRect>("Icon")?.Texture ?? button.Icon;
			if (icon != null) _actionsPopup.AddIconItem(icon, label, (int)id);
			else _actionsPopup.AddItem(label, (int)id);
			_overflowActions[id++] = button;
		}
		_updatingOverflow = false;
	}

	private void OnCurrentControlChanged(Control? control)
	{
		_showingCodeActions = control is TextEditor.TextEditorContainer;
		QueueOverflowUpdate();
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
			Control anchor = button.Visible ? button : _moreActionsButton;
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
		if (IsInstanceValid(_container)) _container.Resized -= QueueOverflowUpdate;
		if (Tabs.Singleton != null) Tabs.Singleton.CurrentControlChanged -= OnCurrentControlChanged;
		base._ExitTree();
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

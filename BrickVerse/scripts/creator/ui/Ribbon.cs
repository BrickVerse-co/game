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
using BrickVerse.Creator.UI.Docking;

namespace BrickVerse.Creator.UI;

public sealed partial class Ribbon : Control
{
	private static readonly HashSet<string> PartInsertClasses = new(StringComparer.Ordinal)
	{
		"Part", "Truss", "Mesh", "EditableMesh", "Seat", "VehicleSeat",
	};
	private static readonly HashSet<string> GuiInsertClasses = new(StringComparer.Ordinal)
	{
		"GUI", "GUI3D", "UIView", "UILabel", "UIButton", "UIImage", "UIVideoFrame",
		"UITextInput", "UIHLayout", "UIVLayout", "UIHFlow", "UIVFlow", "UIGradient",
		"UIGridLayout", "UIScrollView", "UIViewport", "UICorner", "UIStroke", "UIShadow",
		"UIAspectRatioRestraint",
	};
	private static readonly HashSet<string> ScriptInsertClasses = new(StringComparer.Ordinal)
	{
		"ClientScript", "ServerScript", "ModuleScript",
	};
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
	private string _lastSelectionSignature = "";

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
		NormalizeRibbonIcons(_taskTabs);
		PopulateShapesMenu(shapesButton);
		AddModelTools(model);

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
		WireHomeInsertShortcuts(home);

		DockHost rightDock = GetNode<DockHost>(
			"../Splitter/Right/Region/Split/Primary");
		if (!rightDock.ContainsPanelId("Team Chat"))
		{
			Control teamChat = GD.Load<PackedScene>("res://scenes/creator/docks/team_chat_dock.tscn").Instantiate<Control>();
			rightDock.AddPanel(new DockPanel(
				"Team Chat",
				"Team Chat",
				teamChat,
				GD.Load<Texture2D>("res://assets/textures/ui-icons/team-create.svg")
			), suppressSave: true);
		}
		if (TeamCreateService.Instance != null) TeamCreateService.Instance.StateChanged += RefreshTeamChatTab;
		RefreshTeamChatTab();

		forgeButton.Pressed += () => DockManager.OpenPanel("Forge");
		terrainButton.Pressed += () =>
		{
			DockManager.OpenPanel("Terrain Editor");
			World.Current?.Container?.GrabFocus();
		};
		animatorButton.Pressed += CreatorService.Interface.OpenAnimationEditor;
		toolboxButton.Pressed += () => DockManager.OpenPanel("Toolbox");
		inputManagerButton.Pressed += CreatorService.Interface.OpenInputManager;
		Tabs.Singleton.CurrentControlChanged += OnCurrentControlChanged;
		OnCurrentControlChanged(Tabs.Singleton.CurrentControl);

		_ribbonGroup.Pressed += OnRibbonChanged;
		_selectButton.Toggled += pressed => { if (pressed) ActivateTool(ToolModeEnum.Select); };
		_moveButton.Toggled += pressed => { if (pressed) ActivateTool(ToolModeEnum.Move); };
		_rotateButton.Toggled += pressed => { if (pressed) ActivateTool(ToolModeEnum.Rotate); };
		_scaleButton.Toggled += pressed => { if (pressed) ActivateTool(ToolModeEnum.Scale); };
	}

	private static TextEditor.TextEditorContainer? ActiveTextEditor() => Tabs.Singleton?.CurrentControl as TextEditor.TextEditorContainer;

	public override void _Process(double delta)
	{
		Instance[] selected = World.Current?.CreatorContext?.Selections?.GetSelected() ?? [];
		string signature = string.Join(',', selected.Select(instance => instance.ObjectID));
		if (signature == _lastSelectionSignature) return;
		_lastSelectionSignature = signature;
		if (selected.Length == 0) return;
		_taskTabs.CurrentTab = selected.Any(instance => instance is UIField) ? 2
			: selected.Any(instance => instance is BrickVerse.Datamodel.Script) ? 3 : 1;
	}

	private void FormatActiveDocument() => ActiveTextEditor()?.EditorRoot.FormatDocument();
	private void SaveActiveDocument() => ActiveTextEditor()?.EditorRoot.SaveDocument();
	private void FindInActiveDocument() => ActiveTextEditor()?.EditorRoot.OpenFind();

	private void AddModelTools(HBoxContainer model)
	{
		AddModelAction(model, "Group", "group", "Group as a Model or Folder", button =>
		{
			PopupMenu menu = new(); button.AddChild(menu);
			menu.AddItem("Group as Model", 0); menu.AddItem("Group as Folder", 1);
			menu.IdPressed += id => World.Current?.CreatorContext.Selections.GroupSelected(id == 1 ? CreatorHistory.GroupAsEnum.Folder : CreatorHistory.GroupAsEnum.Model);
			button.Pressed += () => { menu.Position = (Vector2I)(button.GlobalPosition + new Vector2(0, button.Size.Y)); menu.Popup(); };
		});
		AddModelAction(model, "Pivot", "pivot", "Edit the selected object's local pivot with move and rotate handles", button => button.Pressed += () => ActivateTool(ToolModeEnum.Pivot));
		AddModelAction(model, "Reset Pivot", "reset-pivot", "Restore selected pivots to their object origins", button => button.Pressed += ResetPivots);
		AddModelAction(model, "Anchor", "anchor", "Anchor or unanchor selected physical instances", button =>
		{
			PopupMenu menu = new(); button.AddChild(menu);
			menu.AddItem("Toggle Selected", 0); menu.AddItem("Anchor All Descendants", 1); menu.AddItem("Unanchor All Descendants", 2);
			menu.IdPressed += id => SetAnchored((int)id);
			button.Pressed += () => { menu.Position = (Vector2I)(button.GlobalPosition + new Vector2(0, button.Size.Y)); menu.Popup(); };
		});
		AddModelAction(model, "Lock", "lock", "Lock or unlock selected instances", button => button.Pressed += () => World.Current?.CreatorContext.Selections.ToggleLockSelected());
		AddModelAction(model, "Align", "align", "Set World or Local transform orientation", button =>
		{
			PopupMenu menu = new(); button.AddChild(menu);
			menu.AddItem("World orientation", 0); menu.AddItem("Local orientation", 1); menu.AddItem("Align rotation to world", 2);
			menu.IdPressed += id => SetAlignment((int)id);
			button.Pressed += () => { menu.Position = (Vector2I)(button.GlobalPosition + new Vector2(0, button.Size.Y)); menu.Popup(); };
		});
		AddModelAction(model, "Weld", "weld", "Create a Weld between two selected physical instances", button => button.Pressed += CreateWeld);
		AddModelAction(model, "Effects", "effects", "Insert an effect, attachment, or light", AddEffectsMenu);
	}

	private static void AddModelAction(Container parent, string text, string icon, string tooltip, Action<Button> configure)
	{
		Button button = new() { TooltipText = tooltip, CustomMinimumSize = new Vector2(Mathf.Max(60, text.Length * 7 + 14), 54), FocusMode = FocusModeEnum.None, ClipContents = true };
		TextureRect iconView = new() { Texture = GD.Load<Texture2D>($"res://assets/textures/creator/ribbon/{icon}.svg"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color("0097ff") };
		iconView.SetAnchorsPreset(Control.LayoutPreset.FullRect); iconView.OffsetTop = 5; iconView.OffsetBottom = -22;
		Label label = new() { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
		label.SetAnchorsPreset(Control.LayoutPreset.BottomWide); label.OffsetLeft = 2; label.OffsetTop = -20; label.OffsetRight = -2; label.OffsetBottom = -2;
		button.AddChild(iconView); button.AddChild(label);
		parent.AddChild(button); configure(button);
	}

	private static void ResetPivots()
	{
		World? world = World.Current; if (world == null) return;
		Dynamic[] targets = world.CreatorContext.Selections.GetSelected().OfType<Dynamic>().ToArray(); if (targets.Length == 0) return;
		Vector3[] beforeOffsets = targets.Select(target => target.PivotOffset).ToArray();
		Vector3[] beforeRotations = targets.Select(target => target.PivotRotation).ToArray();
		world.CreatorContext.History.RecordAppliedAction("Reset pivots", new((_) => { foreach (Dynamic target in targets) { target.PivotOffset = Vector3.Zero; target.PivotRotation = Vector3.Zero; } }), new((_) => { for (int i = 0; i < targets.Length; i++) { targets[i].PivotOffset = beforeOffsets[i]; targets[i].PivotRotation = beforeRotations[i]; } }));
		foreach (Dynamic target in targets) { target.PivotOffset = Vector3.Zero; target.PivotRotation = Vector3.Zero; }
		world.CreatorContext.Gizmos.RefreshVisuals();
	}

	private static void SetAnchored(int command)
	{
		World? world = World.Current; if (world == null) return;
		IEnumerable<Physical> source = world.CreatorContext.Selections.GetSelected().OfType<Physical>();
		if (command > 0) source = world.CreatorContext.Selections.GetSelected().SelectMany(item => item.GetDescendants().Prepend(item)).OfType<Physical>();
		Physical[] items = source.Distinct().ToArray(); if (items.Length == 0) return;
		bool[] before = items.Select(item => item.Anchored).ToArray(); bool value = command == 0 ? !items.All(item => item.Anchored) : command == 1;
		world.CreatorContext.History.RecordAppliedAction(value ? "Anchor instances" : "Unanchor instances", new((_) => { foreach (Physical item in items) item.Anchored = value; }), new((_) => { for (int i = 0; i < items.Length; i++) items[i].Anchored = before[i]; }));
		foreach (Physical item in items) item.Anchored = value;
	}

	private static void CreateWeld()
	{
		World? world = World.Current; if (world == null) return;
		Physical[] parts = world.CreatorContext.Selections.GetSelected().OfType<Physical>().Take(2).ToArray();
		if (parts.Length != 2) { CreatorService.Interface.PopupAlert("Select exactly two physical instances to create a Weld.", "Weld"); return; }
		Weld weld = Globals.LoadInstance<Weld>(world); weld.Part0 = parts[0]; weld.Part1 = parts[1];
		world.CreatorContext.History.CreateInstances([weld], parts[0]); world.CreatorContext.Selections.SelectOnly(weld);
	}

	private static void SetAlignment(int command)
	{
		if (command < 2)
		{
			CreatorSettingsService.Instance.Set(CreatorSettingKeys.Interface.TransformOrientation,
				command == 0 ? TransformOrientationEnum.Global : TransformOrientationEnum.Local);
			CreatorService.Interface.StatusBar?.SetStatus(command == 0 ? "Gizmos aligned to World" : "Gizmos aligned to Local");
			return;
		}
		World? world = World.Current; if (world == null) return;
		Dynamic[] items = world.CreatorContext.Selections.GetSelected().OfType<Dynamic>().ToArray(); if (items.Length == 0) return;
		Transform3D[] before = items.Select(item => item.GetGlobalTransform()).ToArray();
		Action apply = () => { foreach (Dynamic item in items) item.Rotation = Vector3.Zero; };
		apply();
		world.CreatorContext.History.RecordAppliedAction("Align rotation to world", new((_) => apply()), new((_) => { for (int i = 0; i < items.Length; i++) items[i].SetGlobalTransform(before[i]); }));
	}

	private static void AddEffectsMenu(Button button)
	{
		(string Label, string ClassName)[] effects = [("Beam", "Beam"), ("Explosion", "Explosion"), ("Fire", "Particles"), ("Particle Emitter", "Particles"), ("Smoke", "Particles"), ("Sparkles", "Particles"), ("Trail", "Trail"), ("Attachment", "Attachment"), ("Point Light", "PointLight"), ("Spot Light", "SpotLight"), ("Surface Light", "SurfaceLight")];
		PopupMenu menu = new(); button.AddChild(menu);
		for (int i = 0; i < effects.Length; i++) menu.AddItem(effects[i].Label, i);
		menu.IdPressed += id =>
		{
			World? world = World.Current; if (world == null) return;
			Instance parent = world.CreatorContext.Selections.GetSelected().FirstOrDefault() ?? world.Environment;
			Instance? effect = Globals.LoadInstance<Instance>(effects[(int)id].ClassName, world);
			if (effect == null) { CreatorService.Interface.StatusBar?.SetStatus($"{effects[(int)id].Label} is not available in this build."); return; }
			if (effects[(int)id].ClassName == "Particles") effect.Name = effects[(int)id].Label;
			world.CreatorContext.History.CreateInstances([effect], parent); world.CreatorContext.Selections.SelectOnly(effect);
		};
		button.Pressed += () => { menu.Position = (Vector2I)(button.GlobalPosition + new Vector2(0, button.Size.Y)); menu.Popup(); };
	}

	private void AddTaskAction(string label, string icon, Action action)
	{
		Button button = new()
		{
			Name = label.Replace(" ", ""),
			CustomMinimumSize = new Vector2(Mathf.Max(62, label.Length * 7 + 12), 54),
			TooltipText = label,
			FocusMode = Control.FocusModeEnum.None,
		};
		TextureRect iconView = new()
		{
			Name = "Icon",
			Texture = GD.Load<Texture2D>($"res://assets/textures/ui-icons/{icon}.svg"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Modulate = new Color("0097ff"),
		};
		Label caption = new()
		{
			Name = "Label",
			Text = label,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
		};
		button.AddChild(iconView);
		button.AddChild(caption);
		caption.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		caption.OffsetLeft = 2;
		caption.OffsetTop = -20;
		caption.OffsetRight = -2;
		caption.OffsetBottom = -2;
		button.Pressed += action;
		_quickActions.AddChild(button);
	}

	private static void NormalizeRibbonIcons(Control root)
	{
		foreach (Node node in root.FindChildren("*", nameof(Button), recursive: true, owned: false))
		{
			if (node is not Button button
				|| button.GetNodeOrNull<TextureRect>("Icon") is not TextureRect icon)
				continue;

			button.ClipContents = true;
			icon.CustomMinimumSize = Vector2.Zero;
			icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			icon.OffsetLeft = 0;
			icon.OffsetTop = 5;
			icon.OffsetRight = 0;
			icon.OffsetBottom = -22;
			icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			icon.MouseFilter = Control.MouseFilterEnum.Ignore;
		}
	}

	private static void WireHomeInsertShortcuts(HBoxContainer home)
	{
		Button part = home.GetNode<Button>("Part");
		Button gui = home.GetNode<Button>("GUI");
		Button script = home.GetNode<Button>("Script");
		Button import = home.GetNode<Button>("Import");

		part.Pressed += () => CreatorService.Interface.OpenInsertMenu(allowedClasses: PartInsertClasses);
		gui.Pressed += () => CreatorService.Interface.OpenInsertMenu(allowedClasses: GuiInsertClasses);
		script.Pressed += () => CreatorService.Interface.OpenInsertMenu(allowedClasses: ScriptInsertClasses);
		PopulateImportMenu(import);
	}

	private static void PopulateImportMenu(Button button)
	{
		PopupMenu popup = new() { Name = "ImportPopup" };
		button.AddChild(popup);
		popup.AddIconItem(GD.Load<Texture2D>("res://assets/textures/datamodel/Mesh.svg"), "Mesh", 0);
		popup.AddIconItem(GD.Load<Texture2D>("res://assets/textures/ui-icons/image-square.svg"), "Texture", 1);
		popup.AddIconItem(GD.Load<Texture2D>("res://assets/textures/datamodel/Sound.svg"), "Sound", 2);
		popup.AddIconItem(GD.Load<Texture2D>("res://assets/textures/ui-icons/video.svg"), "Video", 3);
		popup.IdPressed += id =>
		{
			switch (id)
			{
				case 0: CreatorService.Interface.OpenUploadMeshMenu(); break;
				case 1: CreatorService.Interface.OpenUploadTextureMenu(); break;
				case 2: CreatorService.Interface.OpenUploadSoundMenu(); break;
				case 3: CreatorService.Interface.OpenUploadVideoMenu(); break;
			}
		};
		button.Pressed += () =>
		{
			popup.Position = (Vector2I)(button.GlobalPosition + new Vector2(0, button.Size.Y));
			popup.Popup();
		};
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
		bool visible = TeamCreateService.Instance?.TeamCreateEnabled == true;
		DockManager.SetPanelVisible("Team Chat", visible);
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
			ActivateTool(ToolModeEnum.Select);
		}
		else if (CreatorKeybindResolver.IsPressed(@event, CreatorSettingKeys.Keybinds.ToolMove, Key.Key2))
		{
			_moveButton.ButtonPressed = true;
			ActivateTool(ToolModeEnum.Move);
		}
		else if (CreatorKeybindResolver.IsPressed(@event, CreatorSettingKeys.Keybinds.ToolRotate, Key.Key3))
		{
			_rotateButton.ButtonPressed = true;
			ActivateTool(ToolModeEnum.Rotate);
		}
		else if (CreatorKeybindResolver.IsPressed(@event, CreatorSettingKeys.Keybinds.ToolScale, Key.Key4))
		{
			_scaleButton.ButtonPressed = true;
			ActivateTool(ToolModeEnum.Scale);
		}

		base._UnhandledKeyInput(@event);
	}

	private void OnRibbonChanged(BaseButton rawBtn)
	{
		RibbonToolButton btn = (RibbonToolButton)rawBtn;
		ActivateTool(btn.ToolMode);
	}

	private static void ActivateTool(ToolModeEnum toolMode)
	{
		CreatorService.Interface.ToolMode = toolMode;
		World.Current?.CreatorContext?.Gizmos?.RefreshVisuals();
		switch (toolMode)
		{
			case ToolModeEnum.Paint:
			case ToolModeEnum.Brush:
				World.Current?.Container?.GrabFocus();
				break;
		}
	}
}

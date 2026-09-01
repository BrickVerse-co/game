// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;
using BrickVerse.Creator.UI.Splashes;
using BrickVerse.Creator.UI.Wizards;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using System;
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Creator.Managers;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class CommandPalettePopup : PopupWindowBase
{
	private sealed record Command(string Name, string Category, string Description, string Shortcut, Action Execute, Func<bool>? Available = null);
	private const string ScenePath = "res://scenes/creator/popups/command_palette.tscn";
	[Export] private LineEdit _search = null!;
	[Export] private ItemList _results = null!;
	[Export] private Label _description = null!;
	[Export] private Label _count = null!;
	private readonly List<Command> _commands = [];
	private readonly List<Command> _visibleCommands = [];
	private static readonly List<string> RecentCommandNames = [];
	private static CommandPalettePopup? _openInstance;

	public static void Open()
	{
		if (_openInstance != null && IsInstanceValid(_openInstance))
		{
			_openInstance.GrabFocus();
			return;
		}
		CommandPalettePopup popup = GD.Load<PackedScene>(ScenePath).Instantiate<CommandPalettePopup>();
		_openInstance = popup;
		CreatorService.Interface.PopupWindow(popup);
	}

	public override void _Ready()
	{
		BuildCommands();
		_search.TextChanged += _ => RefreshResults();
		_search.TextSubmitted += _ => RunSelected();
		_results.ItemSelected += _ => UpdateDescription();
		_results.ItemActivated += _ => RunSelected();
		RefreshResults();
		_search.GrabFocus();
		base._Ready();
	}

	public override void _ExitTree()
	{
		if (_openInstance == this) _openInstance = null;
		base._ExitTree();
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false } key)
		{
			if (key.Keycode == Key.Down) { SelectOffset(1); GetViewport().SetInputAsHandled(); }
			else if (key.Keycode == Key.Up) { SelectOffset(-1); GetViewport().SetInputAsHandled(); }
		}
		base._UnhandledKeyInput(@event);
	}

	private void BuildCommands()
	{
		bool HasWorld() => World.Current != null;
		_commands.AddRange([
			new("New Project", "File", "Create a world from a template or blank project.", "Ctrl+N", CreatorInterface.CreateNewWorld),
			new("Open Project", "File", "Open an existing local Creator project.", "Ctrl+O", CreatorService.Interface.PromptOpenWorld),
			new("Save", "File", "Save the active world or document.", "Ctrl+S", CreatorService.SaveCurrentFile, HasWorld),
			new("Save All", "File", "Save every open world and modified script across the project.", "Ctrl+Shift+S", () => Tabs.Singleton.SaveAll(), () => Tabs.Singleton.OpenTabCount > 0),
			new("Save As", "File", "Save the active world or document to another location.", "", CreatorService.SaveCurrentFileAs, HasWorld),
			new("Close Place", "File", "Close the active place with unsaved-work protection.", "F4", () => Tabs.Singleton.CloseCurrentPlace(), HasWorld),
			new("Undo", "Edit", "Undo the last Creator action.", "Ctrl+Z", CreatorService.Undo, HasWorld),
			new("Redo", "Edit", "Redo the last undone action.", "Ctrl+Y", CreatorService.Redo, HasWorld),
			new("Insert Object", "Edit", "Open the searchable object insertion browser.", "", () => CreatorService.Interface.OpenInsertMenu(), HasWorld),
			new("Find in Files", "Edit", "Search project scripts and text files, then jump directly to a matching line.", "Ctrl+Shift+F", FindInFilesPopup.Open, HasWorld),
			new("Align Selection on X", "Transform", "Align selected object pivots to the last-selected object on the X axis.", "Ctrl+Alt+X", () => SelectionTransformTools.AlignToActive(SelectionTransformTools.Axis.X), HasWorld),
			new("Align Selection on Y", "Transform", "Align selected object pivots to the last-selected object on the Y axis.", "Ctrl+Alt+Y", () => SelectionTransformTools.AlignToActive(SelectionTransformTools.Axis.Y), HasWorld),
			new("Align Selection on Z", "Transform", "Align selected object pivots to the last-selected object on the Z axis.", "Ctrl+Alt+Z", () => SelectionTransformTools.AlignToActive(SelectionTransformTools.Axis.Z), HasWorld),
			new("Distribute Selection on X", "Transform", "Evenly space at least three selected objects along X while preserving endpoints.", "Ctrl+Shift+Alt+X", () => SelectionTransformTools.Distribute(SelectionTransformTools.Axis.X), HasWorld),
			new("Distribute Selection on Y", "Transform", "Evenly space at least three selected objects along Y while preserving endpoints.", "Ctrl+Shift+Alt+Y", () => SelectionTransformTools.Distribute(SelectionTransformTools.Axis.Y), HasWorld),
			new("Distribute Selection on Z", "Transform", "Evenly space at least three selected objects along Z while preserving endpoints.", "Ctrl+Shift+Alt+Z", () => SelectionTransformTools.Distribute(SelectionTransformTools.Axis.Z), HasWorld),
			new("Play Test", "Test", "Run the current world from its spawn point.", "F6", () => CreatorService.Singleton.StartLocalTest(), HasWorld),
			new("Play From Camera", "Test", "Run the world starting at the editor camera.", "", () => CreatorService.Singleton.StartLocalTest(true), HasWorld),
			new("Stop Play Test", "Test", "Stop all active local test processes.", "F8", () => CreatorService.Singleton.StopLocalTest(), () => CreatorService.Singleton.LocalTestActive),
			new("Publish", "Build", "Publish saved changes to the linked cloud world.", "", () => CreatorService.Interface.OpenWorldPublish(World.Current), HasWorld),
			new("Publish As", "Build", "Publish as a new world or overwrite another editable target.", "", () => CreatorService.Interface.OpenWorldPublish(World.Current, true), HasWorld),
			new("Cloud Projects", "Window", "Browse personal and editable guild projects.", "", StartupSplash.Singleton.OpenCloud),
			new("Creator Settings", "Window", "Configure Creator, graphics, editor, and workflow preferences.", "", CreatorService.Interface.OpenSettings),
			new("Input Manager", "Window", "Edit the current project's input actions.", "", CreatorService.Interface.OpenInputManager, HasWorld),
			new("Device Emulator", "Window", "Preview the experience at common device sizes.", "", DeviceEmulatorPopup.Open, HasWorld),
			new("Data Store Explorer", "Tools", "Inspect development data stores.", "", () => CreatorDataToolsWindow.Open(0), HasWorld),
			new("Scene History & Diff", "Tools", "Inspect scene revisions and compare changes.", "", () => CreatorDataToolsWindow.Open(2), HasWorld),
			new("What’s New", "Help", "Review the latest Creator features and improvements.", "", WhatsNewPopup.ShowLatest),
		]);
		for (int slot = 1; slot <= 9; slot++)
		{
			int bookmarkSlot = slot;
			_commands.Add(new($"Save Viewport Bookmark {slot}", "Viewport", "Store the current scene camera position for this world.", $"Ctrl+Alt+{slot}", () => ViewportBookmarkManager.Save(bookmarkSlot), HasWorld));
			_commands.Add(new($"Recall Viewport Bookmark {slot}", "Viewport", "Return the scene camera to this saved position.", $"Alt+{slot}", () => ViewportBookmarkManager.Recall(bookmarkSlot), () => HasWorld() && ViewportBookmarkManager.Exists(bookmarkSlot)));
		}
	}

	private void RefreshResults()
	{
		string query = _search.Text.Trim();
		IEnumerable<Command> matches = _commands.Where(command => command.Available?.Invoke() != false);
		if (query.Length > 0)
		{
			string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			matches = matches.Where(command => terms.All(term =>
				command.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| command.Category.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| command.Description.Contains(term, StringComparison.OrdinalIgnoreCase)));
		}
		_visibleCommands.Clear();
		_visibleCommands.AddRange(matches
			.OrderBy(command => RecentRank(command.Name))
			.ThenBy(command => command.Category)
			.ThenBy(command => command.Name));
		_results.Clear();
		foreach (Command command in _visibleCommands)
		{
			string suffix = string.IsNullOrWhiteSpace(command.Shortcut) ? "" : $"    {command.Shortcut}";
			_results.AddItem($"{command.Category}  ·  {command.Name}{suffix}");
		}
		_count.Text = $"{_visibleCommands.Count} command{(_visibleCommands.Count == 1 ? "" : "s")}";
		if (_visibleCommands.Count > 0) _results.Select(0);
		UpdateDescription();
	}

	private void UpdateDescription()
	{
		int[] selected = _results.GetSelectedItems();
		_description.Text = selected.Length > 0 && selected[0] < _visibleCommands.Count
			? _visibleCommands[selected[0]].Description : "No matching commands.";
	}

	private void SelectOffset(int offset)
	{
		if (_visibleCommands.Count == 0) return;
		int[] selected = _results.GetSelectedItems();
		int next = Mathf.Clamp((selected.Length == 0 ? 0 : selected[0]) + offset, 0, _visibleCommands.Count - 1);
		_results.Select(next);
		_results.EnsureCurrentIsVisible();
		UpdateDescription();
	}

	private void RunSelected()
	{
		int[] selected = _results.GetSelectedItems();
		if (selected.Length == 0 || selected[0] >= _visibleCommands.Count) return;
		Command command = _visibleCommands[selected[0]];
		Action action = command.Execute;
		RecentCommandNames.Remove(command.Name);
		RecentCommandNames.Insert(0, command.Name);
		if (RecentCommandNames.Count > 8) RecentCommandNames.RemoveRange(8, RecentCommandNames.Count - 8);
		QueueFree();
		Callable.From(action).CallDeferred();
	}

	private static int RecentRank(string name)
	{
		int index = RecentCommandNames.IndexOf(name);
		return index < 0 ? int.MaxValue : index;
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0.

using Godot;
using BrickVerse.Schemas.Debugger;
using System.Collections.Generic;

namespace BrickVerse.Creator.Debugger;

/// <summary>
/// Live view of a play-test process. Runtime changes are intentionally sent
/// through the debugger instead of mutating the saved Creator world.
/// </summary>
public sealed partial class RuntimeDebugWindow : MarginContainer
{
	private readonly DebugServer _server;
	private readonly int _processId;
	private readonly Tree _explorer = new() { HideRoot = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
	private readonly VBoxContainer _properties = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
	private readonly RichTextLabel _console = new() { FitContent = false, ScrollFollowing = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
	private readonly LineEdit _executor = new() { PlaceholderText = "Run Luau in this runtime…" };
	private readonly Dictionary<TreeItem, RuntimeObjectInfo> _items = [];

	public RuntimeDebugWindow(DebugServer server, int processId, bool isServer)
	{
		_server = server;
		_processId = processId;
		Name = isServer ? "Server Runtime" : "Client Runtime";
		CustomMinimumSize = new Vector2(0, 260);
		SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		AddThemeConstantOverride("margin_left", 7);
		AddThemeConstantOverride("margin_top", 7);
		AddThemeConstantOverride("margin_right", 7);
		AddThemeConstantOverride("margin_bottom", 7);
		SetMeta("_tab_name", Name);

		TabContainer tabs = new();
		AddChild(tabs);
		tabs.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabs.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

		HSplitContainer inspector = new() { Name = "Explorer & Properties" };
		inspector.AddChild(_explorer);
		ScrollContainer propertyScroll = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		propertyScroll.AddChild(_properties);
		inspector.AddChild(propertyScroll);
		tabs.AddChild(inspector);

		VBoxContainer consolePanel = new() { Name = "Console" };
		consolePanel.AddChild(_console);
		HBoxContainer executeRow = new();
		executeRow.AddChild(_executor);
		_executor.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		Button execute = new() { Text = "Execute" };
		executeRow.AddChild(execute);
		consolePanel.AddChild(executeRow);
		tabs.AddChild(consolePanel);

		_explorer.ItemSelected += ShowSelectedProperties;
		_explorer.ItemEdited += OnExplorerItemEdited;
		execute.Pressed += Execute;
		_executor.TextSubmitted += _ => Execute();
	}

	public void Activate()
	{
		Show();
		if (GetParent() is TabContainer tabs)
			tabs.CurrentTab = tabs.GetTabIdxFromControl(this);
	}

	public override async void _Ready()
	{
		_server.RequestRuntimeSnapshot(_processId);
		await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
		if (IsInstanceValid(this)) _server.RequestRuntimeSnapshot(_processId);
		base._Ready();
	}

	public void ApplySnapshot(MessageRuntimeSnapshot snapshot)
	{
		_items.Clear();
		_explorer.Clear();
		TreeItem hiddenRoot = _explorer.CreateItem();
		Dictionary<string, TreeItem> idToItem = [];

		foreach (RuntimeObjectInfo obj in snapshot.Objects)
		{
			TreeItem parent = string.IsNullOrEmpty(obj.ParentObjectID) || !idToItem.TryGetValue(obj.ParentObjectID, out TreeItem? parentItem)
				? hiddenRoot
				: parentItem;
			TreeItem item = parent.CreateChild();
			item.SetText(0, $"{obj.Name}  [{obj.ClassName}]");
			item.SetEditable(0, true);
			item.Collapsed = obj.ParentObjectID.Length > 0;
			idToItem[obj.ObjectID] = item;
			_items[item] = obj;
		}
	}

	private void OnExplorerItemEdited()
	{
		TreeItem? selected = _explorer.GetEdited();
		if (selected == null || !_items.TryGetValue(selected, out RuntimeObjectInfo? obj)) return;
		string text = selected.GetText(0).Trim();
		int classSuffix = text.LastIndexOf("  [", System.StringComparison.Ordinal);
		string newName = classSuffix > 0 ? text[..classSuffix].Trim() : text;
		if (newName.Length == 0) return;
		_server.RenameRuntimeObject(_processId, obj.ObjectID, newName);
		_server.RequestRuntimeSnapshot(_processId);
	}

	public void AppendLog(MessageLogDispatch log)
	{
		string source = string.IsNullOrWhiteSpace(log.Source) ? "" : $" [{log.Source}]";
		_console.AppendText($"[{log.LogFrom}]{source} {log.Content}\n");
	}

	private void ShowSelectedProperties()
	{
		foreach (Node child in _properties.GetChildren()) child.QueueFree();
		TreeItem? selected = _explorer.GetSelected();
		if (selected == null || !_items.TryGetValue(selected, out RuntimeObjectInfo? obj)) return;

		Label title = new() { Text = $"{obj.Name} ({obj.ClassName})" };
		title.AddThemeFontSizeOverride("font_size", 18);
		_properties.AddChild(title);

		foreach (RuntimePropertyInfo property in obj.Properties)
		{
			HBoxContainer row = new();
			Label label = new() { Text = property.Name, CustomMinimumSize = new Vector2(180, 0), TooltipText = property.TypeName };
			LineEdit value = new() { Text = property.Value, Editable = property.CanWrite, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			if (property.CanWrite)
			{
				value.TextSubmitted += text =>
				{
					_server.SetRuntimeProperty(_processId, obj.ObjectID, property.Name, text);
					_server.RequestRuntimeSnapshot(_processId);
				};
			}
			row.AddChild(label);
			row.AddChild(value);
			_properties.AddChild(row);
		}
	}

	private void Execute()
	{
		string source = _executor.Text.Trim();
		if (source.Length == 0) return;
		_server.ExecuteRuntimeLuau(_processId, source);
		_executor.Clear();
	}
}

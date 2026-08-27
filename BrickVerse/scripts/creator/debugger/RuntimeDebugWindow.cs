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
	private readonly RichTextLabel _performance;
	private readonly RichTextLabel _scripts;
	private readonly RichTextLabel _memory;
	private readonly RichTextLabel _network;
	private Timer _diagnosticsTimer = null!;

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
		tabs.AddThemeConstantOverride("side_margin", 8);

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
		Button execute = new() { Text = "Run", CustomMinimumSize = new Vector2(74, 34), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
		execute.AddThemeStyleboxOverride("normal", Surface("1769AA"));
		execute.AddThemeStyleboxOverride("hover", Surface("2384CE"));
		execute.AddThemeStyleboxOverride("pressed", Surface("10558B"));
		executeRow.AddChild(execute);
		consolePanel.AddChild(executeRow);
		tabs.AddChild(consolePanel);
		_performance = AddDiagnosticTab(tabs, "Performance");
		_scripts = AddDiagnosticTab(tabs, "Scripts");
		_memory = AddDiagnosticTab(tabs, "Memory");
		_network = AddDiagnosticTab(tabs, "Network");

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
		_server.RuntimeDiagnosticsReceived += OnDiagnostics;
		_diagnosticsTimer = new Timer { WaitTime = 1, Autostart = true };
		_diagnosticsTimer.Timeout += RequestDiagnostics;
		AddChild(_diagnosticsTimer);
		RequestDiagnostics();
		_server.RequestRuntimeSnapshot(_processId);
		await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
		if (IsInstanceValid(this)) _server.RequestRuntimeSnapshot(_processId);
		base._Ready();
	}

	public override void _ExitTree()
	{
		_server.RuntimeDiagnosticsReceived -= OnDiagnostics;
		base._ExitTree();
	}

	private static RichTextLabel AddDiagnosticTab(TabContainer tabs, string name)
	{
		MarginContainer panel = GD.Load<PackedScene>("res://scenes/creator/components/runtime_diagnostic_panel.tscn").Instantiate<MarginContainer>();
		panel.Name = name;
		tabs.AddChild(panel);
		return panel.GetNode<RichTextLabel>("Content");
	}

	private void RequestDiagnostics() => _server.RequestRuntimeDiagnostics(_processId);

	private void OnDiagnostics(int processId, MessageRuntimeDiagnostics sample)
	{
		if (processId != _processId) return;
		string fpsColor = sample.Fps >= 55 ? "#35C978" : sample.Fps >= 30 ? "#F0B84B" : "#ED5C5C";
		_performance.Text = $"[font_size=21][b]Live performance[/b][/font_size]\n[color=#9BA7B8]Updates every second from this play-test process.[/color]\n\n[font_size=30][color={fpsColor}]{sample.Fps:0} FPS[/color][/font_size]\n\n[b]Timing[/b]\nFrame time                 {sample.FrameTimeMs:0.00} ms\nPhysics time              {sample.PhysicsTimeMs:0.00} ms\n\n[b]Rendering[/b]\nDraw calls                 {sample.DrawCalls:N0}\nRendered objects      {sample.Active3DObjects:N0}\n\n[b]Scene[/b]\nNodes                         {sample.NodeCount:N0}\nObjects                      {sample.ObjectCount:N0}";
		_scripts.Text = $"[font_size=21][b]Running scripts[/b][/font_size]  [color=#62B5F6]{sample.Scripts.Length} active[/color]\n[color=#9BA7B8]Script instances currently present in the runtime tree.[/color]\n\n" + (sample.Scripts.Length == 0 ? "[color=#9BA7B8]No running script instances.[/color]" : "• " + string.Join("\n• ", sample.Scripts));
		_memory.Text = $"[font_size=21][b]Memory[/b][/font_size]\n[color=#9BA7B8]Live process allocation and renderer usage.[/color]\n\n[font_size=26][color=#62B5F6]{FormatBytes(sample.StaticMemoryBytes)}[/color][/font_size]\nManaged / static memory\n\n[font_size=26]{FormatBytes(sample.VideoMemoryBytes)}[/font_size]\nVideo memory";
		string pingColor = sample.PingMs <= 80 ? "#35C978" : sample.PingMs <= 180 ? "#F0B84B" : "#ED5C5C";
		_network.Text = $"[font_size=21][b]Network[/b][/font_size]\n[color=#9BA7B8]Current multiplayer session state.[/color]\n\n[b]Runtime role[/b]          {(sample.IsServer ? "Server" : "Client")}\n[b]Network mode[/b]       {sample.NetworkMode}\n[b]Players[/b]                    {sample.Players:N0}\n[b]Latency[/b]                    [color={pingColor}]{sample.PingMs:N0} ms[/color]";
	}

	private static StyleBoxFlat Surface(string hex)
	{
		StyleBoxFlat style = new() { BgColor = Color.FromHtml(hex), ContentMarginLeft = 14, ContentMarginTop = 12, ContentMarginRight = 14, ContentMarginBottom = 12 };
		style.SetCornerRadiusAll(8);
		return style;
	}

	private static string FormatBytes(long bytes)
	{
		string[] units = ["B", "KB", "MB", "GB"];
		double value = bytes;
		int unit = 0;
		while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
		return $"{value:0.##} {units[unit]}";
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

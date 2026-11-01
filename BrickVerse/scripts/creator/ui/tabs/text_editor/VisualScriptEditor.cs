// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BrickVerse.Scripting.Visual;
using VisualNode = BrickVerse.Scripting.Visual.GraphNode;

namespace BrickVerse.Creator.UI.TextEditor;

public partial class TextEditorRoot
{
	private GraphEdit? _visualGraph;
	private PopupMenu? _wireBlockMenu;
	private readonly List<BlockDefinition> _wireCandidates = [];
	private StringName _pendingWireNode = default!;
	private int _pendingWirePort;
	private bool _pendingWireFromOutput;
	private Vector2 _pendingBlockPosition;
	private readonly List<GraphConnection> _visualConnections = [];
	private static readonly Color FlowColor = Color.FromHtml("#F4F4F4");
	private static readonly Color ValueColor = Color.FromHtml("#4EB3E8");
	private sealed record BlockDefinition(string Category, string Title, string Type, string Hint);
	private static readonly BlockDefinition[] BlockDefinitions =
	[
		new("Events", "Event Begin Play", "event_start", "Runs when this script starts"),
		new("Flow", "Branch", "branch", "Choose True or False using a condition"),
		new("Output", "Print", "print", "Write information to Output"), new("Output", "Warn", "warn", "Write a warning"),
		new("Output", "Error", "error", "Write an error"),
		new("DataModel", "Get Service", "get_service", "Get a service such as Players"),
		new("DataModel", "Game", "game", "The current DataModel"), new("DataModel", "Script", "script", "This script instance"),
		new("DataModel", "Find First Child", "find_child", "Find a named child"),
		new("DataModel", "Get Property", "get_property", "Read an API property"),
		new("DataModel", "Set Property", "set_property", "Change an API property"),
		new("DataModel", "Call Method", "call_method", "Call an API method"), new("DataModel", "Destroy", "destroy", "Destroy an instance"),
		new("Values", "Text", "literal", "A text value"), new("Values", "Number", "number", "A numeric value"),
		new("Values", "Boolean", "boolean", "True or false"), new("Values", "To String", "to_string", "Convert a value to text"),
		new("Math", "Add", "add", "A + B"), new("Math", "Subtract", "subtract", "A - B"),
		new("Math", "Multiply", "multiply", "A × B"), new("Math", "Divide", "divide", "A ÷ B"),
		new("Compare", "Equal", "equals", "A equals B"), new("Compare", "Less Than", "less", "A < B"),
		new("Compare", "Greater Than", "greater", "A > B"), new("Logic", "And", "and", "A and B"),
		new("Logic", "Or", "or", "A or B"), new("Logic", "Not", "not", "Invert a boolean"),
	];

	private void InitializeVisualEditor()
	{
		CodeEditor.Visible = false;
		_finder.Visible = false;
		Control host = CodeEditor.GetParent<Control>();
		VBoxContainer layout = new() { LayoutMode = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		host.AddChild(layout);
		HBoxContainer toolbar = new() { CustomMinimumSize = new Vector2(0, 42) };
		Label heading = new() { Text = "  VISUAL SCRIPT  ", TooltipText = "Experimental graphs compile to sandboxed Luau." };
		toolbar.AddChild(heading);
		MenuButton addBlock = new() { Text = "+ Add Block", TooltipText = "Insert a visual scripting block" };
		PopupMenu menu = addBlock.GetPopup();
		int menuId = 0;
		string? category = null;
		foreach (BlockDefinition definition in BlockDefinitions)
		{
			if (category != null && category != definition.Category) menu.AddSeparator(definition.Category);
			else if (category == null) menu.AddSeparator(definition.Category);
			category = definition.Category;
			menu.AddItem(definition.Title, menuId++);
			menu.SetItemTooltip(menu.ItemCount - 1, definition.Hint);
		}
		menu.IdPressed += id => AddBlock(BlockDefinitions[(int)id].Type);
		toolbar.AddChild(addBlock);
		toolbar.AddChild(new Label { Text = "  Drag pins to connect  •  Right-click a wire to disconnect  •  Ctrl+S to save", Modulate = new Color(0.68f, 0.7f, 0.75f) });
		layout.AddChild(toolbar);

		_visualGraph = new GraphEdit { MinimapEnabled = true, ShowGrid = true, RightDisconnects = true,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		_visualGraph.AddValidConnectionType(0, 0);
		_visualGraph.AddValidConnectionType(1, 1);
		_visualGraph.ConnectionRequest += ConnectVisualNodes;
		_visualGraph.DisconnectionRequest += DisconnectVisualNodes;
		_visualGraph.ConnectionToEmpty += (node, port, position) => ShowWireBlockMenu(node, (int)port, position, true);
		_visualGraph.ConnectionFromEmpty += (node, port, position) => ShowWireBlockMenu(node, (int)port, position, false);
		_visualGraph.GuiInput += OnVisualGraphInput;
		layout.AddChild(_visualGraph);
		_wireBlockMenu = new PopupMenu();
		_wireBlockMenu.IdPressed += CreateBlockFromWireMenu;
		_visualGraph.AddChild(_wireBlockMenu);
		LoadVisualDocument();
	}

	private Godot.GraphNode? AddBlock(string type, Vector2? position = null)
	{
		Vector2 location = position ?? new Vector2(260 + _visualConnections.Count * 12, 160 + _visualConnections.Count * 12);
		VisualNode data = new() { Id = Guid.NewGuid().ToString("N"), Type = type, X = location.X, Y = location.Y };
		if (type is "print" or "warn" or "error" or "literal") data.Text = type == "literal" ? "Text" : "Hello BrickVerse!";
		if (type == "number") data.Text = "0";
		if (type == "boolean") data.Text = "true";
		if (type is "get_service") data.Text = "Players";
		if (type == "find_child") data.Text = "Part";
		if (type is "get_property" or "set_property") data.Member = "Name";
		if (type == "call_method") data.Member = "Destroy";
		Godot.GraphNode? node = CreateVisualNode(data);
		SerializeVisualDocument();
		return node;
	}

	private void LoadVisualDocument()
	{
		GraphDocument document;
		try { document = JsonSerializer.Deserialize<GraphDocument>(CodeEditor.Text, VisualJsonOptions) ?? new(); }
		catch { document = new(); }
		_visualConnections.Clear();
		_visualConnections.AddRange(document.Connections);
		foreach (VisualNode node in document.Nodes) CreateVisualNode(node);
		foreach (GraphConnection link in _visualConnections)
			if (_visualGraph!.HasNode(new NodePath(link.From)) && _visualGraph.HasNode(new NodePath(link.To)))
				_visualGraph.ConnectNode(link.From, link.FromPort, link.To, link.ToPort);
		foreach (GraphConnection link in _visualConnections) RefreshInputPresentation(link.To, link.ToPort);
	}

	private Godot.GraphNode? CreateVisualNode(VisualNode data)
	{
		if (_visualGraph == null || _visualGraph.HasNode(new NodePath(data.Id))) return null;
		Godot.GraphNode node = new()
		{
			Name = data.Id,
			Title = BlockTitle(data.Type),
			PositionOffset = new Vector2(data.X, data.Y),
		};
		node.SetMeta("type", data.Type);
		_visualGraph.AddChild(node);
		bool action = data.Type is "print" or "warn" or "error" or "set_property" or "call_method" or "destroy";
		bool value = data.Type is "get_service" or "get_property" or "find_child" or "literal" or "number" or "boolean"
			or "game" or "script" or "to_string" or "not" or "add" or "subtract" or "multiply" or "divide"
			or "equals" or "less" or "greater" or "and" or "or";
		if (data.Type == "event_start")
		{
			node.AddChild(new Label { Text = "Begin Play" });
			node.SetSlot(0, false, 0, FlowColor, true, 0, FlowColor);
		}
		else if (data.Type == "branch")
		{
			node.AddChild(new Label { Text = "Condition                                      True" });
			node.SetSlot(0, true, 0, FlowColor, true, 0, FlowColor);
			node.AddChild(new Label { Text = "                                                   False" });
			node.SetSlot(1, true, 1, ValueColor, true, 0, FlowColor);
		}
		else if (action)
		{
			node.AddChild(new Label { Text = "Execution" });
			node.SetSlot(0, true, 0, FlowColor, true, 0, FlowColor);
			AddEditableRow(node, data.Type is "call_method" or "set_property" ? "Member" : data.Type == "destroy" ? "Instance" : "Value", data.Type is "call_method" or "set_property" ? data.Member : data.Text,
				text => { if (data.Type is "call_method" or "set_property") node.SetMeta("member", text); else node.SetMeta("text", text); });
			node.SetSlot(1, true, 1, ValueColor, false, 1, ValueColor);
			if (data.Type is "call_method" or "set_property" or "destroy")
			{
				node.AddChild(new Label { Text = "Target" });
				node.SetSlot(2, true, 1, ValueColor, false, 1, ValueColor);
			}
		}
		else if (value)
		{
			if (data.Type is "game" or "script")
			{
				node.AddChild(new Label { Text = "Value" });
				node.SetSlot(0, false, 1, ValueColor, true, 1, ValueColor);
			}
			else if (data.Type is "to_string" or "not")
			{
				node.AddChild(new Label { Text = "Value" });
				node.SetSlot(0, true, 1, ValueColor, true, 1, ValueColor);
			}
			else if (data.Type is "add" or "subtract" or "multiply" or "divide" or "equals" or "less" or "greater" or "and" or "or")
			{
				node.AddChild(new Label { Text = "A" });
				node.SetSlot(0, true, 1, ValueColor, true, 1, ValueColor);
				node.AddChild(new Label { Text = "B" });
				node.SetSlot(1, true, 1, ValueColor, false, 1, ValueColor);
			}
			else
			{
				AddEditableRow(node, data.Type == "get_service" ? "Service" : data.Type == "get_property" ? "Property" : data.Type == "find_child" ? "Child" : data.Type == "number" ? "Number" : data.Type == "boolean" ? "Boolean" : "Text",
					data.Type == "get_property" ? data.Member : data.Text,
					text => { if (data.Type == "get_property") node.SetMeta("member", text); else node.SetMeta("text", text); });
				node.SetSlot(0, false, 1, ValueColor, true, 1, ValueColor);
			}
			if (data.Type is "get_property" or "find_child")
			{
				node.AddChild(new Label { Text = "Target" });
				node.SetSlot(1, true, 1, ValueColor, false, 1, ValueColor);
			}
		}
		if (data.Text != null) node.SetMeta("text", data.Text);
		if (data.Member != null) node.SetMeta("member", data.Member);
		if (data.Type != "event_start")
		{
			Button delete = new() { Text = "Delete Block", Flat = true };
			delete.Pressed += () => RemoveVisualNode(node);
			node.AddChild(delete);
		}
		return node;
	}

	private void AddEditableRow(Godot.GraphNode node, string label, string? value, Action<string> changed)
	{
		HBoxContainer row = new();
		row.AddChild(new Label { Text = label });
		LineEdit edit = new() { Text = value ?? "", CustomMinimumSize = new Vector2(120, 0) };
		edit.TextChanged += text => { changed(text); SerializeVisualDocument(); };
		row.AddChild(edit);
		node.AddChild(row);
	}

	private void ConnectVisualNodes(StringName from, long fromPort, StringName to, long toPort)
	{
		if (_visualConnections.Any(link => link.From == from.ToString() && link.FromPort == fromPort && link.To == to.ToString() && link.ToPort == toPort)) return;
		foreach (GraphConnection existing in _visualConnections.Where(link => link.To == to.ToString() && link.ToPort == toPort).ToArray())
			DisconnectVisualNodes(existing.From, existing.FromPort, existing.To, existing.ToPort);
		_visualGraph!.ConnectNode(from, (int)fromPort, to, (int)toPort);
		_visualConnections.Add(new() { From = from, FromPort = (int)fromPort, To = to, ToPort = (int)toPort });
		RefreshInputPresentation(to, (int)toPort);
		SerializeVisualDocument();
	}

	private void DisconnectVisualNodes(StringName from, long fromPort, StringName to, long toPort)
	{
		_visualGraph!.DisconnectNode(from, (int)fromPort, to, (int)toPort);
		_visualConnections.RemoveAll(link => link.From == from.ToString() && link.FromPort == fromPort && link.To == to.ToString() && link.ToPort == toPort);
		RefreshInputPresentation(to, (int)toPort);
		SerializeVisualDocument();
	}

	private void ShowWireBlockMenu(StringName nodeName, int port, Vector2 releasePosition, bool fromOutput)
	{
		if (_visualGraph == null || _wireBlockMenu == null || _visualGraph.GetNodeOrNull<Godot.GraphNode>(new NodePath(nodeName.ToString())) is not Godot.GraphNode endpoint) return;
		int requiredType = fromOutput ? endpoint.GetOutputPortType(port) : endpoint.GetInputPortType(port);
		_wireCandidates.Clear();
		_wireCandidates.AddRange(BlockDefinitions.Where(definition => fromOutput
			? AcceptsType(definition.Type, requiredType)
			: ProducesType(definition.Type, requiredType)));
		_wireBlockMenu.Clear();
		string? category = null;
		for (int index = 0; index < _wireCandidates.Count; index++)
		{
			BlockDefinition candidate = _wireCandidates[index];
			if (category != candidate.Category) { _wireBlockMenu.AddSeparator(candidate.Category); category = candidate.Category; }
			_wireBlockMenu.AddItem(candidate.Title, index);
			_wireBlockMenu.SetItemTooltip(_wireBlockMenu.ItemCount - 1, candidate.Hint);
		}
		_pendingWireNode = nodeName;
		_pendingWirePort = port;
		_pendingWireFromOutput = fromOutput;
		_pendingBlockPosition = (releasePosition + _visualGraph.ScrollOffset) / _visualGraph.Zoom;
		_wireBlockMenu.Position = DisplayServer.MouseGetPosition();
		_wireBlockMenu.Popup();
	}

	private void CreateBlockFromWireMenu(long id)
	{
		if (id < 0 || id >= _wireCandidates.Count) return;
		Godot.GraphNode? created = AddBlock(_wireCandidates[(int)id].Type, _pendingBlockPosition);
		if (created == null) return;
		if (_pendingWireFromOutput)
		{
			for (int port = 0; port < created.GetInputPortCount(); port++)
				if (created.GetInputPortType(port) == GetOutputType(_pendingWireNode, _pendingWirePort))
				{ ConnectVisualNodes(_pendingWireNode, _pendingWirePort, created.Name, port); break; }
		}
		else
		{
			for (int port = 0; port < created.GetOutputPortCount(); port++)
				if (created.GetOutputPortType(port) == GetInputType(_pendingWireNode, _pendingWirePort))
				{ ConnectVisualNodes(created.Name, port, _pendingWireNode, _pendingWirePort); break; }
		}
	}

	private int GetOutputType(StringName node, int port) => _visualGraph!.GetNode<Godot.GraphNode>(new NodePath(node.ToString())).GetOutputPortType(port);
	private int GetInputType(StringName node, int port) => _visualGraph!.GetNode<Godot.GraphNode>(new NodePath(node.ToString())).GetInputPortType(port);
	private static bool ProducesType(string type, int pinType) => pinType == 0
		? type is "event_start" or "branch" or "print" or "warn" or "error" or "set_property" or "call_method" or "destroy"
		: type is "get_service" or "get_property" or "find_child" or "literal" or "number" or "boolean" or "game" or "script"
			or "to_string" or "not" or "add" or "subtract" or "multiply" or "divide" or "equals" or "less" or "greater" or "and" or "or";
	private static bool AcceptsType(string type, int pinType) => pinType == 0
		? type is "branch" or "print" or "warn" or "error" or "set_property" or "call_method" or "destroy"
		: type is "branch" or "print" or "warn" or "error" or "set_property" or "call_method" or "destroy" or "get_property"
			or "find_child" or "to_string" or "not" or "add" or "subtract" or "multiply" or "divide" or "equals" or "less" or "greater" or "and" or "or";

	private void RefreshInputPresentation(StringName nodeName, int inputPort)
	{
		if (_visualGraph?.GetNodeOrNull<Godot.GraphNode>(new NodePath(nodeName.ToString())) is not Godot.GraphNode target
			|| inputPort < 0 || inputPort >= target.GetInputPortCount()) return;
		int slot = target.GetInputPortSlot(inputPort);
		if (slot < 0 || slot >= target.GetChildCount() || target.GetChild(slot) is not Control row) return;
		LineEdit? editor = row.GetChildren().OfType<LineEdit>().FirstOrDefault();
		Button? chip = row.GetChildren().OfType<Button>().FirstOrDefault(button => button.Name == "LinkedValue");
		GraphConnection? connection = _visualConnections.FirstOrDefault(link => link.To == nodeName.ToString() && link.ToPort == inputPort);
		if (connection == null)
		{
			if (editor != null) editor.Visible = true;
			if (chip != null) { row.RemoveChild(chip); chip.QueueFree(); }
			return;
		}
		if (_visualGraph.GetNodeOrNull<Godot.GraphNode>(new NodePath(connection.From)) is not Godot.GraphNode source) return;
		if (editor != null) editor.Visible = false;
		chip ??= new Button { Name = "LinkedValue", Flat = true, TooltipText = "Jump to the connected value block" };
		string preview = source.HasMeta("text") ? source.GetMeta("text").AsString() : source.Title;
		if (preview.Length > 24) preview = preview[..21] + "…";
		chip.Text = $"↗ {source.Title}: {preview}";
		if (chip.GetParent() == null)
		{
			chip.Pressed += () => FocusVisualNode(connection.From);
			row.AddChild(chip);
		}
	}

	private void FocusVisualNode(string id)
	{
		if (_visualGraph?.GetNodeOrNull<Godot.GraphNode>(new NodePath(id)) is not Godot.GraphNode source) return;
		foreach (Godot.GraphNode node in _visualGraph.GetChildren().OfType<Godot.GraphNode>()) node.Selected = false;
		source.Selected = true;
		_visualGraph.ScrollOffset = source.PositionOffset * _visualGraph.Zoom - _visualGraph.Size / 2 + source.Size / 2;
	}

	private void RemoveVisualNode(Godot.GraphNode node)
	{
		string id = node.Name;
		foreach (GraphConnection link in _visualConnections.Where(link => link.From == id || link.To == id).ToArray())
		{
			if (_visualGraph!.IsNodeConnected(link.From, link.FromPort, link.To, link.ToPort))
				_visualGraph.DisconnectNode(link.From, link.FromPort, link.To, link.ToPort);
			_visualConnections.Remove(link);
		}
		node.QueueFree();
		SerializeVisualDocument();
	}

	private void OnVisualGraphInput(InputEvent input)
	{
		if (input.IsActionPressed("save")) { SaveDocument(); _visualGraph!.AcceptEvent(); }
		else if (input is InputEventMouseButton { Pressed: false }) SerializeVisualDocument();
	}

	private void SerializeVisualDocument()
	{
		if (_visualGraph == null) return;
		GraphDocument document = new() { Connections = _visualConnections.ToList() };
		foreach (Godot.GraphNode node in _visualGraph.GetChildren().OfType<Godot.GraphNode>())
		{
			document.Nodes.Add(new VisualNode
			{
				Id = node.Name,
				Type = node.GetMeta("type").AsString(),
				X = node.PositionOffset.X,
				Y = node.PositionOffset.Y,
				Text = node.HasMeta("text") ? node.GetMeta("text").AsString() : null,
				Member = node.HasMeta("member") ? node.GetMeta("member").AsString() : null,
			});
		}
		CodeEditor.Text = JsonSerializer.Serialize(document, VisualJsonOptions);
	}

	private static string BlockTitle(string type) => type switch
	{
		"event_start" => "Event Begin Play", "print" => "Print", "warn" => "Warn", "error" => "Error",
		"get_service" => "Get Service", "get_property" => "Get Property", "set_property" => "Set Property",
		"call_method" => "Call Method", "literal" => "Text", "number" => "Number", "boolean" => "Boolean",
		"game" => "Game", "script" => "Script", "find_child" => "Find First Child", "destroy" => "Destroy",
		"branch" => "Branch", "to_string" => "To String", "not" => "Not", "add" => "Add", "subtract" => "Subtract",
		"multiply" => "Multiply", "divide" => "Divide", "equals" => "Equal", "less" => "Less Than",
		"greater" => "Greater Than", "and" => "And", "or" => "Or", _ => type,
	};
	private static readonly JsonSerializerOptions VisualJsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BrickVerse.Scripting.Luau;
using Script = BrickVerse.Datamodel.Script;

namespace BrickVerse.Scripting.Visual;

/// <summary>Experimental visual scripts are serialized graphs compiled to ordinary sandboxed Luau.</summary>
public sealed class VisualScriptProvider : IScriptLanguageProvider
{
	private readonly LuauProvider _luau = new();
	public byte[] CompileSource(string source) => _luau.CompileSource(VisualScriptCompiler.Compile(source));
	public void Run(Script script) => _luau.Run(script);
	public void Close(Script script) => _luau.Close(script);
	public Task CallAsync(Script script, string funcName, object?[]? args) => _luau.CallAsync(script, funcName, args);
	public void CallUpdate(Script script, double delta) => _luau.CallUpdate(script, delta);
	public void CallFixedUpdate(Script script, double delta) => _luau.CallFixedUpdate(script, delta);
	public void FreeBVCallback(BVCallback callback) => _luau.FreeBVCallback(callback);
	public void Dispose() => _luau.Dispose();
}

public static class VisualScriptCompiler
{
	public static string Compile(string json)
	{
		GraphDocument document;
		try { document = JsonSerializer.Deserialize<GraphDocument>(json, JsonOptions) ?? throw new JsonException("Empty graph"); }
		catch (JsonException exception) { throw new InvalidOperationException("Visual script graph is invalid: " + exception.Message, exception); }
		if (document.Version != 1) throw new InvalidOperationException($"Unsupported visual script version {document.Version}");
		Dictionary<string, GraphNode> nodes = document.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
		GraphNode? start = document.Nodes.FirstOrDefault(node => node.Type == "event_start");
		if (start == null) return "-- Empty BrickVerse visual script\n";
		StringBuilder luau = new("-- Generated from BrickVerse Visual Script. Do not edit.\n");
		AppendFlow(start.Id, luau, document, nodes, [], 0, skipCurrent: true);
		return luau.ToString();
	}

	private static void AppendFlow(string nodeId, StringBuilder output, GraphDocument document,
		Dictionary<string, GraphNode> nodes, HashSet<string> path, int indent, bool skipCurrent = false)
	{
		if (!nodes.TryGetValue(nodeId, out GraphNode? node) || !path.Add(nodeId)) return;
		if (!skipCurrent)
		{
			if (node.Type == "branch")
			{
				output.Append('\t', indent).Append("if ").Append(InputExpression(node.Id, 1, document, nodes) ?? "false").Append(" then\n");
				AppendNext(node, 0, output, document, nodes, new(path), indent + 1);
				output.Append('\t', indent).Append("else\n");
				AppendNext(node, 1, output, document, nodes, new(path), indent + 1);
				output.Append('\t', indent).Append("end\n");
				return;
			}
			output.Append('\t', indent);
			AppendAction(output, node, document, nodes);
		}
		AppendNext(node, 0, output, document, nodes, path, indent);
	}

	private static void AppendNext(GraphNode node, int outputPort, StringBuilder output, GraphDocument document,
		Dictionary<string, GraphNode> nodes, HashSet<string> path, int indent)
	{
		GraphConnection? next = document.Connections.FirstOrDefault(link => link.From == node.Id && link.FromPort == outputPort && link.ToPort == 0);
		if (next != null) AppendFlow(next.To, output, document, nodes, path, indent);
	}

	private static void AppendAction(StringBuilder output, GraphNode node, GraphDocument document, Dictionary<string, GraphNode> nodes)
	{
		string value = InputExpression(node.Id, 1, document, nodes) ?? Quote(node.Text ?? "");
		switch (node.Type)
		{
			case "print": output.Append("print(").Append(value).Append(")\n"); break;
			case "warn": output.Append("warn(").Append(value).Append(")\n"); break;
			case "error": output.Append("error(").Append(value).Append(")\n"); break;
			case "set_property":
				output.Append(Target(node, document, nodes)).Append('.').Append(SafeMember(node.Member)).Append(" = ").Append(value).Append("\n");
				break;
			case "call_method":
				output.Append(Target(node, document, nodes)).Append(':').Append(SafeMember(node.Member)).Append('(')
					.Append(InputExpression(node.Id, 1, document, nodes) ?? "").Append(")\n");
				break;
			case "destroy": output.Append(Target(node, document, nodes)).Append(":Destroy()\n"); break;
			default: throw new InvalidOperationException($"Unknown visual action block '{node.Type}'");
		}
	}

	private static string Target(GraphNode node, GraphDocument document, Dictionary<string, GraphNode> nodes) =>
		InputExpression(node.Id, node.Type is "get_property" or "find_child" ? 0 : 2, document, nodes) ?? "game";

	private static string? InputExpression(string nodeId, int port, GraphDocument document, Dictionary<string, GraphNode> nodes)
	{
		GraphConnection? link = document.Connections.FirstOrDefault(item => item.To == nodeId && item.ToPort == port);
		if (link == null || !nodes.TryGetValue(link.From, out GraphNode? source)) return null;
		return source.Type switch
		{
			"get_service" => $"game:GetService({Quote(source.Text ?? "Players")})",
			"get_property" => $"{Target(source, document, nodes)}.{SafeMember(source.Member)}",
			"literal" => Quote(source.Text ?? ""),
			"number" => ParseNumber(source.Text),
			"boolean" => string.Equals(source.Text, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
			"game" => "game",
			"script" => "script",
			"find_child" => $"{Target(source, document, nodes)}:FindFirstChild({Quote(source.Text ?? "Part")})",
			"to_string" => $"tostring({InputExpression(source.Id, 0, document, nodes) ?? "nil"})",
			"not" => $"not ({InputExpression(source.Id, 0, document, nodes) ?? "false"})",
			"add" or "subtract" or "multiply" or "divide" or "equals" or "less" or "greater" or "and" or "or" =>
				Binary(source, document, nodes),
			_ => throw new InvalidOperationException($"Block '{source.Type}' cannot provide a value"),
		};
	}

	private static string Binary(GraphNode node, GraphDocument document, Dictionary<string, GraphNode> nodes)
	{
		string op = node.Type switch { "add" => "+", "subtract" => "-", "multiply" => "*", "divide" => "/",
			"equals" => "==", "less" => "<", "greater" => ">", "and" => "and", _ => "or" };
		return $"({InputExpression(node.Id, 0, document, nodes) ?? "0"} {op} {InputExpression(node.Id, 1, document, nodes) ?? "0"})";
	}

	private static string ParseNumber(string? value) => double.TryParse(value, System.Globalization.NumberStyles.Float,
		System.Globalization.CultureInfo.InvariantCulture, out double parsed)
		? parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";

	private static string SafeMember(string? member)
	{
		if (string.IsNullOrWhiteSpace(member) || !member.All(character => char.IsLetterOrDigit(character) || character == '_') || !char.IsLetter(member[0]))
			throw new InvalidOperationException($"'{member}' is not a safe API member name");
		return member;
	}
	private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

public sealed class GraphDocument
{
	public int Version { get; set; } = 1;
	public List<GraphNode> Nodes { get; set; } = [];
	public List<GraphConnection> Connections { get; set; } = [];
}
public sealed class GraphNode
{
	public string Id { get; set; } = "";
	public string Type { get; set; } = "";
	public float X { get; set; }
	public float Y { get; set; }
	public string? Text { get; set; }
	public string? Member { get; set; }
}
public sealed class GraphConnection
{
	public string From { get; set; } = "";
	public int FromPort { get; set; }
	public string To { get; set; } = "";
	public int ToPort { get; set; }
}

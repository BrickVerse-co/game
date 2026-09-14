// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Linq;
using System.Text.Json;
using BrickVerse.Datamodel;

namespace BrickVerse.Creator.UI;

/// <summary>Transport-independent Creator MCP protocol core shared by Forge CEF and external HTTP clients. Only call on the Godot main thread.</summary>
internal sealed class ForgeMcpServer
{
	private World? _world;
	private ForgeToolExecutor? _executor;
	private bool _initialized;
	private readonly System.Collections.Generic.HashSet<string> _shellRequests = new();
	private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.CancellationTokenSource> _runningCommands = new();

	public async System.Threading.Tasks.Task<string?> HandleAsync(string message, Godot.Control owner, System.Threading.CancellationToken lifetime)
	{
		object? id = null;
		try
		{
			if (message.Length > 262144) return Error(null, -32600, "Request too large.");
			using var document = JsonDocument.Parse(message);
			var request = document.RootElement;
			if (request.TryGetProperty("method", out var notification) && notification.GetString() == "notifications/cancelled")
			{
				var cancelledId = request.GetProperty("params").GetProperty("requestId").GetRawText();
				if (_runningCommands.TryGetValue(cancelledId, out var running)) running.Cancel();
				return null;
			}
			if (request.TryGetProperty("id", out var requestId)) id = requestId.Clone();
			if (request.TryGetProperty("method", out var method) && method.GetString() == "tools/call"
				&& request.TryGetProperty("params", out var parameters)
				&& parameters.TryGetProperty("name", out var name) && name.GetString() == "run_shell")
			{
				if (id == null || request.GetProperty("jsonrpc").GetString() != "2.0") return Error(id, -32600, "Invalid request.");
				if (!_initialized) return Error(id, -32002, "Initialize first.");
				if (_shellRequests.Count >= 1024 || !_shellRequests.Add(requestId.GetRawText()))
					return Error(id, -32600, "Duplicate command request or session limit reached. Reconnect Creator.");
				using var cancellation = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(lifetime);
				var key = requestId.GetRawText();
				if (!_runningCommands.TryAdd(key, cancellation))
					return Error(id, -32600, "Duplicate running command request.");
				try
				{
					var result = await ForgeShellTool.RunAsync(parameters.GetProperty("arguments").Clone(), owner, cancellation.Token);
					return ToolResult(id, result.Text, result.IsError);
				}
				finally { _runningCommands.TryRemove(key, out _); }
			}
			return Handle(message);
		}
		catch (Exception ex) { return ToolResult(id, ex.Message, true); }
	}

	public string? Handle(string message)
	{
		object? id = null;
		try
		{
			if (message.Length > 262144) return Error(null, -32600, "Request too large.");
			using var document = JsonDocument.Parse(message);
			var request = document.RootElement;
			if (request.ValueKind != JsonValueKind.Object) return Error(null, -32600, "Expected a request object.");
			if (request.TryGetProperty("id", out var requestId)) id = requestId.Clone();
			if (!request.TryGetProperty("jsonrpc", out var version) || version.GetString() != "2.0"
				|| !request.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
				return Error(id, -32600, "Invalid JSON-RPC request.");
			if (id == null) return null; // MCP notifications have no response.
			object result;
			switch (method.GetString())
			{
				case "initialize":
					_initialized = true;
					result = new
					{
						protocolVersion = "2025-06-18",
						capabilities = new { tools = new { listChanged = false } },
						serverInfo = new { name = "brickverse-creator", version = "1.0.0" }
					};
					break;
				case "ping": result = new { }; break;
				case "tools/list":
					if (!_initialized) return Error(id, -32002, "Initialize first.");
					result = new
					{
						tools = ForgeToolCatalog.Definitions.Where(t => t.Function.Name != "run_luau")
						.Select(t => (object)new { name = t.Function.Name, description = t.Function.Description, inputSchema = t.Function.Parameters })
						.Concat(OperatingSystem.IsWindows() ? new[] { ForgeShellTool.Definition } : Array.Empty<object>())
					};
					break;
				case "tools/call":
					if (!_initialized) return Error(id, -32002, "Initialize first.");
					var args = request.GetProperty("params");
					var name = args.GetProperty("name").GetString();
					if (name == "run_luau" || !ForgeToolCatalog.Definitions.Any(t => t.Function.Name == name))
						return Error(id, -32602, "Unknown or unavailable tool.");
					var world = World.Current;
					if (world == null) return ToolResult(id, "Open a world in Creator first.", true);
					if (!ReferenceEquals(world, _world))
					{
						if (name is not ("get_creator_state" or "get_world_tree" or "list_instantiable_classes" or "search_instances" or "inspect_instance" or "get_script_diff"))
							return ToolResult(id, "The active world changed. Inspect the current world before editing.", true);
						_world = world;
						_executor = new ForgeToolExecutor(world);
					}
					try
					{
						var output = _executor!.Execute(name!, args.TryGetProperty("arguments", out var arguments) ? arguments.GetRawText() : "{}");
						return ToolResult(id, output, false, _executor.LastEvent);
					}
					catch (Exception ex) { return ToolResult(id, ex.Message, true); }
				default: return Error(id, -32601, "Method not found.");
			}
			return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
		}
		catch (JsonException) { return Error(id, -32700, "Invalid JSON."); }
		catch (Exception) { return Error(id, -32602, "Invalid request parameters."); }
	}

	private static string ToolResult(object? id, string text, bool isError, ForgeToolEvent? change = null) =>
		JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result = new { content = new[] { new { type = "text", text } }, isError, structuredContent = change } });
	private static string Error(object? id, int code, string message) =>
		JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } });
}

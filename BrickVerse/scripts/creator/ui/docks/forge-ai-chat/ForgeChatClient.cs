// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BrickVerse.Creator.UI;

internal sealed class ForgeChatClient
{
	private const int MaxToolRounds = 6;
	private readonly BVHttpClient _httpClient = new();

	private const string SystemPrompt = """
    You are Forge, an AI coding assistant inside BrickVerse Creator.

    TOOL AND SAFETY RULES:
    - Use tools for project state, paths, scripts, and world edits. Never invent paths or classes.
    - Only read or modify user-visible Creator hierarchy. Never target Temporary, Hidden, Internal, Runtime, Cache, Preview, or inaccessible engine staging areas.
    - Visible Explorer services such as world.Environment, world.ScriptService, world.PlayerDefaults, and their visible descendants are valid targets even if their engine metadata uses hidden flags.
    - For ServerScript/Script-like classes, prefer world.ScriptService when no parent is specified. For world objects, prefer the selected visible instance or world.Environment.
    - Inspect or search before ambiguous mutations.
    - When creating a script, create it with its final Source through create_instance. Do not paste the full source into chat afterward.
    - When changing an existing script, use edit_script_source. Do not repeat the full source in the final response.
    - Keep the final response concise: summarize the change, mention the affected path, and note unresolved issues.
    - The Creator UI exposes Open/Reveal, View diff, and Rollback actions for tool changes.
    - Tool results are authoritative. If create_instance reports a visible created path, the object was created; do not contradict it because a temporary staging path appeared during creation.
    - Never claim a tool succeeded unless its result says it succeeded.
    - Avoid run_luau unless execution is necessary for validation and the user can review it first.
    """;

	public async Task<string> TestConnectionAsync(ForgeProviderSettings settings)
	{
		List<ForgeChatMessage> messages =
		[
			new ForgeChatMessage { Role = "system", Content = "Reply with exactly OK." },
			new ForgeChatMessage { Role = "user", Content = "Respond now." },
		];

		string rawResponse = await SendRequestAsync(settings, messages, null);
		ForgeChatMessage assistant = ParseAssistantMessage(rawResponse);
		return string.IsNullOrWhiteSpace(assistant.Content) ? "No text content returned." : assistant.Content.Trim();
	}

	public async Task<ForgeCompletionResult> CompleteTurnAsync(
		ForgeProviderSettings settings,
		List<ForgeChatMessage> history,
		string userPrompt,
		string? contextSummary,
		ForgeToolExecutor toolExecutor,
		Func<string, Task>? activityCallback = null,
		Func<ForgeRunLuauArgs, Task<bool>>? confirmLuauCallback = null)
	{
		List<ForgeChatMessage> working =
		[
			new ForgeChatMessage { Role = "system", Content = SystemPrompt },
		];

		working.AddRange(NormalizeTranscript(history));

		if (!string.IsNullOrWhiteSpace(contextSummary))
		{
			working.Add(new ForgeChatMessage
			{
				Role = "system",
				Content = "Current Creator context:\n" + contextSummary.Trim(),
			});
		}

		ForgeChatMessage userMessage = new() { Role = "user", Content = userPrompt.Trim() };
		working.Add(userMessage);

		ForgeCompletionResult result = new();
		result.TranscriptDelta.Add(userMessage.Clone());

		for (int round = 0; round < MaxToolRounds; round++)
		{
			RepairToolCallSequence(working);
			if (activityCallback != null) await activityCallback(round == 0 ? "Analyzing your request…" : "Continuing with project context…");
			string rawResponse = await SendRequestAsync(settings, working, ForgeToolCatalog.Definitions);
			ForgeChatMessage assistantMessage = ParseAssistantMessage(rawResponse);
			working.Add(assistantMessage);
			result.TranscriptDelta.Add(assistantMessage.Clone());

			if (assistantMessage.ToolCalls == null || assistantMessage.ToolCalls.Count == 0)
			{
				result.AssistantText = assistantMessage.Content?.Trim() ?? string.Empty;
				return result;
			}

			foreach (ForgeChatToolCall toolCall in assistantMessage.ToolCalls)
			{
				string toolResult;
				toolExecutor.ResetLastEvent();
				try
				{
					if (activityCallback != null) await activityCallback(GetToolActivityText(toolCall.Function.Name));
					if (toolCall.Function.Name == "run_luau")
					{
						ForgeRunLuauArgs runArgs = toolExecutor.ParseRunLuauArguments(toolCall.Function.Arguments);
						bool approved = confirmLuauCallback != null && await confirmLuauCallback(runArgs);
						toolResult = approved
							? toolExecutor.RunConfirmedLuau(runArgs)
							: "The user declined Luau execution. Do not claim the code ran; provide the code and manual steps instead.";
					}
					else
					{
						toolResult = toolExecutor.Execute(toolCall.Function.Name, toolCall.Function.Arguments);
					}
					ForgeToolEvent toolEvent = toolExecutor.LastEvent ?? new ForgeToolEvent
					{
						ToolName = toolCall.Function.Name,
						Title = GetToolActivityText(toolCall.Function.Name).TrimEnd('…'),
						Detail = toolResult.Length > 220 ? toolResult[..220] + "…" : toolResult,
					};
					result.ToolEvents.Add(toolEvent);
				}
				catch (Exception ex)
				{
					toolResult = $"Tool '{toolCall.Function.Name}' failed: {ex.Message}";
				}

				ForgeChatMessage toolMessage = new()
				{
					Role = "tool",
					ToolCallId = toolCall.Id,
					Name = toolCall.Function.Name,
					Content = toolResult,
				};

				working.Add(toolMessage);
				result.TranscriptDelta.Add(toolMessage.Clone());
			}
		}

		RepairToolCallSequence(working);
		if (activityCallback != null) await activityCallback("Writing a final response…");
		working.Add(new ForgeChatMessage
		{
			Role = "system",
			Content = "Tool use is now disabled. Give the user a complete final answer using the gathered results. If an action could not be completed, provide exact code and manual steps. Do not request another tool call.",
		});
		string finalRaw = await SendRequestAsync(settings, working, null);
		ForgeChatMessage finalMessage = ParseAssistantMessage(finalRaw);
		result.AssistantText = string.IsNullOrWhiteSpace(finalMessage.Content)
			? "Forge completed its inspection but the model returned no final text. Review the tool activity above and try a more capable model."
			: finalMessage.Content.Trim();
		result.TranscriptDelta.Add(new ForgeChatMessage { Role = "assistant", Content = result.AssistantText });
		return result;
	}

	private static IEnumerable<ForgeChatMessage> NormalizeTranscript(IEnumerable<ForgeChatMessage> source)
	{
		List<ForgeChatMessage> normalized = source.Select(static message => message.Clone()).ToList();
		RepairToolCallSequence(normalized);
		return normalized;
	}

	private static void RepairToolCallSequence(List<ForgeChatMessage> messages)
	{
		for (int i = 0; i < messages.Count; i++)
		{
			ForgeChatMessage assistant = messages[i];
			if (assistant.Role != "assistant" || assistant.ToolCalls == null || assistant.ToolCalls.Count == 0)
				continue;

			HashSet<string> answered = [];
			int cursor = i + 1;
			while (cursor < messages.Count && messages[cursor].Role == "tool")
			{
				if (!string.IsNullOrWhiteSpace(messages[cursor].ToolCallId))
					answered.Add(messages[cursor].ToolCallId!);
				cursor++;
			}

			foreach (ForgeChatToolCall call in assistant.ToolCalls)
			{
				if (answered.Contains(call.Id)) continue;
				messages.Insert(cursor++, new ForgeChatMessage
				{
					Role = "tool",
					ToolCallId = call.Id,
					Name = call.Function.Name,
					Content = "This tool call was interrupted before execution. Continue without assuming it succeeded.",
				});
			}
		}
	}

	private static bool SupportsCustomTemperature(string model)
	{
		string id = model.Trim().ToLowerInvariant();
		int slash = id.LastIndexOf('/');
		if (slash >= 0) id = id[(slash + 1)..];
		return !(id.StartsWith("gpt-5", StringComparison.Ordinal)
			|| id.StartsWith("o1", StringComparison.Ordinal)
			|| id.StartsWith("o3", StringComparison.Ordinal)
			|| id.StartsWith("o4", StringComparison.Ordinal)
			|| id.StartsWith("grok-4", StringComparison.Ordinal)
			|| id == "forge-free"
			|| id == "auto");
	}

	private static string GetToolActivityText(string toolName) => toolName switch
	{
		"get_creator_state" => "Reading Creator state…",
		"list_instantiable_classes" => "Checking available classes…",
		"search_instances" => "Searching Inspector…",
		"inspect_instance" => "Inspecting object properties and source…",
		"select_instances" => "Updating selection…",
		"create_instance" => "Creating a visible object…",
		"set_instance_properties" => "Applying property changes…",
		"delete_instance" => "Deleting an object…",
		"edit_script_source" => "Editing script…",
		"get_script_diff" => "Preparing script diff…",
		"rollback_last_change" => "Rolling back change…",
		"run_luau" => "Preparing Luau execution preview…",
		_ => $"Running {toolName}…",
	};

	private async Task<string> SendRequestAsync(ForgeProviderSettings settings, List<ForgeChatMessage> messages, List<ForgeChatToolDefinition>? tools)
	{
		ForgeChatRequest request = new()
		{
			Model = settings.Model.Trim(),
			Messages = messages,
			Tools = tools,
			ToolChoice = tools == null ? null : "auto",
		};

		request.Temperature = SupportsCustomTemperature(settings.Model) ? settings.Temperature : null;
		string json = settings.Provider == ForgeProviderKind.Anthropic
			? BuildAnthropicRequest(request)
			: JsonSerializer.Serialize(request, ForgeJsonContext.Default.ForgeChatRequest);
		using HttpRequestMessage httpRequest = new(HttpMethod.Post, settings.GetChatCompletionsEndpoint())
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};

		if (!string.IsNullOrWhiteSpace(settings.ApiKey))
		{
			if (settings.Provider == ForgeProviderKind.Anthropic)
			{
				httpRequest.Headers.TryAddWithoutValidation("x-api-key", settings.ApiKey.Trim());
				httpRequest.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
			}
			else
			{
				httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {settings.ApiKey.Trim()}");
			}
		}

		using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest);
		string body = await response.Content.ReadAsStringAsync();

		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException(BuildErrorMessage(response, body));
		}

		return settings.Provider == ForgeProviderKind.Anthropic ? ConvertAnthropicResponse(body) : body;
	}

	[RequiresUnreferencedCode("Calls System.Text.Json.Nodes.JsonArray.Add<T>(T)")]
	[RequiresDynamicCode("Calls System.Text.Json.Nodes.JsonArray.Add<T>(T)")]
	private static string BuildAnthropicRequest(ForgeChatRequest request)
	{
		JsonArray messages = [];
		string system = string.Join("\n\n", request.Messages.Where(static message => message.Role == "system").Select(static message => message.Content));

		foreach (ForgeChatMessage message in request.Messages.Where(static message => message.Role != "system"))
		{
			if (message.Role == "tool")
			{
				messages.Add(new JsonObject
				{
					["role"] = "user",
					["content"] = new JsonArray(new JsonObject
					{
						["type"] = "tool_result",
						["tool_use_id"] = message.ToolCallId,
						["content"] = message.Content ?? string.Empty,
					}),
				});
				continue;
			}

			JsonArray content = [];
			if (!string.IsNullOrWhiteSpace(message.Content)) content.Add(new JsonObject { ["type"] = "text", ["text"] = message.Content });
			if (message.ToolCalls != null)
			{
				foreach (ForgeChatToolCall call in message.ToolCalls)
					content.Add(new JsonObject { ["type"] = "tool_use", ["id"] = call.Id, ["name"] = call.Function.Name, ["input"] = JsonNode.Parse(call.Function.Arguments) });
			}
			messages.Add(new JsonObject { ["role"] = message.Role, ["content"] = content });
		}

		JsonObject root = new()
		{
			["model"] = request.Model,
			["max_tokens"] = 4096,
			["system"] = system,
			["messages"] = messages,
		};
		if (request.Temperature.HasValue) root["temperature"] = request.Temperature.Value;
		if (request.Tools != null)
		{
			JsonArray tools = [];
			foreach (ForgeChatToolDefinition tool in request.Tools)
				tools.Add(new JsonObject { ["name"] = tool.Function.Name, ["description"] = tool.Function.Description, ["input_schema"] = JsonNode.Parse(tool.Function.Parameters.GetRawText()) });
			root["tools"] = tools;
		}
		return root.ToJsonString();
	}

	[RequiresDynamicCode("Calls System.Text.Json.Nodes.JsonArray.Add<T>(T)")]
	private static string ConvertAnthropicResponse(string body)
	{
		JsonNode root = JsonNode.Parse(body) ?? throw new InvalidDataException("Anthropic returned invalid JSON.");
		JsonArray content = root["content"]?.AsArray() ?? [];
		JsonArray toolCalls = [];
		StringBuilder text = new();
		foreach (JsonNode? block in content)
		{
			string? type = block?["type"]?.GetValue<string>();
			if (type == "text") text.Append(block?["text"]?.GetValue<string>());
			if (type == "tool_use")
			{
				toolCalls.Add(new JsonObject
				{
					["id"] = block?["id"]?.GetValue<string>(),
					["type"] = "function",
					["function"] = new JsonObject { ["name"] = block?["name"]?.GetValue<string>(), ["arguments"] = block?["input"]?.ToJsonString() ?? "{}" },
				});
			}
		}
		JsonObject message = new() { ["role"] = "assistant", ["content"] = text.ToString() };
		if (toolCalls.Count > 0) message["tool_calls"] = toolCalls;
		return new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = message }) }.ToJsonString();
	}

	private static string BuildErrorMessage(HttpResponseMessage response, string body)
	{
		string defaultMessage = $"Forge provider request failed: {(int)response.StatusCode} {response.ReasonPhrase}";
		if (string.IsNullOrWhiteSpace(body))
		{
			return defaultMessage;
		}

		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			JsonElement root = doc.RootElement;
			if (root.TryGetProperty("error", out JsonElement error))
			{
				if (error.ValueKind == JsonValueKind.String)
				{
					return error.GetString() ?? defaultMessage;
				}

				if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement message))
				{
					return message.GetString() ?? defaultMessage;
				}
			}
		}
		catch (InvalidDataException)
		{
		}
		catch (JsonException)
		{
		}

		return defaultMessage + "\n" + body;
	}

	private static ForgeChatMessage ParseAssistantMessage(string body)
	{
		using JsonDocument doc = JsonDocument.Parse(body);
		JsonElement root = doc.RootElement;
		JsonElement choices = root.GetProperty("choices");
		if (choices.GetArrayLength() == 0)
		{
			throw new InvalidDataException("The provider returned no choices.");
		}

		JsonElement messageElement = choices[0].GetProperty("message");
		ForgeChatMessage message = new()
		{
			Role = "assistant",
			Content = ExtractContent(messageElement),
		};

		if (messageElement.TryGetProperty("tool_calls", out JsonElement toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
		{
			message.ToolCalls = [];
			foreach (JsonElement toolCallElement in toolCallsElement.EnumerateArray())
			{
				JsonElement functionElement = toolCallElement.GetProperty("function");
				message.ToolCalls.Add(new ForgeChatToolCall
				{
					Id = toolCallElement.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N"),
					Type = toolCallElement.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() ?? "function" : "function",
					Function = new ForgeChatToolFunctionCall
					{
						Name = functionElement.GetProperty("name").GetString() ?? string.Empty,
						Arguments = functionElement.GetProperty("arguments").GetString() ?? "{}",
					},
				});
			}
		}

		return message;
	}

	private static string? ExtractContent(JsonElement messageElement)
	{
		if (!messageElement.TryGetProperty("content", out JsonElement contentElement) || contentElement.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		if (contentElement.ValueKind == JsonValueKind.String)
		{
			return contentElement.GetString();
		}

		if (contentElement.ValueKind != JsonValueKind.Array)
		{
			return contentElement.GetRawText();
		}

		StringBuilder builder = new();
		foreach (JsonElement item in contentElement.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.String)
			{
				builder.Append(item.GetString());
				continue;
			}

			if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out JsonElement textElement))
			{
				builder.Append(textElement.GetString());
			}
		}

		return builder.ToString();
	}
}

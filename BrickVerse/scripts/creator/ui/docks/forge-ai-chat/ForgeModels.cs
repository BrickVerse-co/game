// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BrickVerse.Creator.Utils;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Creator.UI;

public enum ForgeProviderKind
{
	ForgeFree = 0,
	OpenAI = 1,
	Anthropic = 2,
	XAI = 3,
	Google = 4,
	OpenRouter = 5,
	Compatible = 6,
}

public sealed record ForgeModelDefinition(
	string Id,
	string DisplayName,
	ForgeProviderKind Provider,
	string Description,
	bool SupportsTools = true,
	bool IsRecommended = false
);

public static class ForgeModelCatalog
{
	public static readonly IReadOnlyList<ForgeModelDefinition> Models =
	[
		new(
			"forge-free",
			"Forge Free",
			ForgeProviderKind.ForgeFree,
			"BrickVerse-hosted model for Astro Basic+ members. Heavily rate-limited.",
			true,
			true
		),
		new(
			"gpt-5.2",
			"GPT-5.2",
			ForgeProviderKind.OpenAI,
			"OpenAI flagship coding and agent model.",
			true,
			true
		),
		new(
			"gpt-5.2-mini",
			"GPT-5.2 mini",
			ForgeProviderKind.OpenAI,
			"Faster and lower-cost OpenAI model.",
			true
		),
		new(
			"gpt-4.1",
			"GPT-4.1",
			ForgeProviderKind.OpenAI,
			"General-purpose OpenAI model with tool support.",
			true
		),
		new(
			"claude-opus-4-6",
			"Claude Opus 4.6",
			ForgeProviderKind.Anthropic,
			"Anthropic's highest-capability model.",
			true,
			true
		),
		new(
			"claude-sonnet-4-6",
			"Claude Sonnet 4.6",
			ForgeProviderKind.Anthropic,
			"Balanced Claude model for coding and iteration.",
			true
		),
		new(
			"claude-haiku-4-5",
			"Claude Haiku 4.5",
			ForgeProviderKind.Anthropic,
			"Fast Claude model for lightweight requests.",
			true
		),
		new("grok-4", "Grok 4", ForgeProviderKind.XAI, "xAI flagship reasoning model.", true, true),
		new("grok-4-fast", "Grok 4 Fast", ForgeProviderKind.XAI, "Lower-latency xAI model.", true),
		new(
			"gemini-2.5-pro",
			"Gemini 2.5 Pro",
			ForgeProviderKind.Google,
			"Google model for complex coding and large context.",
			true,
			true
		),
		new(
			"gemini-2.5-flash",
			"Gemini 2.5 Flash",
			ForgeProviderKind.Google,
			"Fast Google model for interactive work.",
			true
		),
		new(
			"openrouter/auto",
			"OpenRouter Auto",
			ForgeProviderKind.OpenRouter,
			"Let OpenRouter route the request.",
			true
		),
		new(
			"custom",
			"Custom model ID…",
			ForgeProviderKind.Compatible,
			"Any OpenAI-compatible local or hosted model.",
			true
		),
	];

	public static IEnumerable<ForgeModelDefinition> ForProvider(ForgeProviderKind provider) =>
		Models.Where(model => model.Provider == provider);

	public static ForgeModelDefinition? Find(string id) =>
		Models.FirstOrDefault(model => model.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

public sealed class ForgeProviderSettings
{
	public ForgeProviderKind Provider { get; set; } = ForgeProviderKind.ForgeFree;
	public string Endpoint { get; set; } = string.Empty;
	public string ApiKey { get; set; } = string.Empty;
	public string Model { get; set; } = "forge-free";
	public bool StoreKey { get; set; } = true;
	public bool StreamResponses { get; set; } = true;
	public float Temperature { get; set; } = 0.2f;
	public int MaxContextCharacters { get; set; } = 12000;

	public bool RequiresApiKey =>
		Provider is not ForgeProviderKind.ForgeFree and not ForgeProviderKind.Compatible;

	public string GetProviderLabel() =>
		Provider switch
		{
			ForgeProviderKind.ForgeFree => "Forge Free",
			ForgeProviderKind.OpenAI => "OpenAI",
			ForgeProviderKind.Anthropic => "Anthropic",
			ForgeProviderKind.XAI => "xAI",
			ForgeProviderKind.Google => "Google Gemini",
			ForgeProviderKind.OpenRouter => "OpenRouter",
			_ => "Local / OpenAI-compatible",
		};

	public string GetBaseEndpoint()
	{
		string fallback = Provider switch
		{
			ForgeProviderKind.ForgeFree => "https://api.brickverse.gg/api/v3/forge-llm",
			ForgeProviderKind.OpenAI => "https://api.openai.com/v1",
			ForgeProviderKind.Anthropic => "https://api.anthropic.com/v1",
			ForgeProviderKind.XAI => "https://api.x.ai/v1",
			ForgeProviderKind.Google => "https://generativelanguage.googleapis.com/v1beta/openai",
			ForgeProviderKind.OpenRouter => "https://openrouter.ai/api/v1",
			_ => "http://localhost:11434/v1",
		};
		return (string.IsNullOrWhiteSpace(Endpoint) ? fallback : Endpoint.Trim()).TrimEnd('/');
	}

	public string GetChatCompletionsEndpoint()
	{
		string endpoint = GetBaseEndpoint();
		if (Provider == ForgeProviderKind.ForgeFree)
			return endpoint + "/completions";
		if (Provider == ForgeProviderKind.Anthropic)
			return endpoint + "/messages";
		return endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
			? endpoint
			: endpoint + "/chat/completions";
	}

	public ForgeProviderSettings CloneForSave() =>
		new()
		{
			Provider = Provider,
			Endpoint = Endpoint,
			ApiKey = StoreKey ? ApiKey : string.Empty,
			Model = Model,
			StoreKey = StoreKey,
			StreamResponses = StreamResponses,
			Temperature = Temperature,
			MaxContextCharacters = MaxContextCharacters,
		};
}

public sealed class ForgeConversation
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");
	public string Title { get; set; } = "New chat";
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public string Model { get; set; } = string.Empty;
	public List<ForgeChatMessage> Messages { get; set; } = [];
}

internal static class ForgeProviderSettingsStore
{
	private const string SettingsPath = "user://creator/forge_settings.json";

	public static ForgeProviderSettings Load()
	{
		try
		{
			return FileAccess.FileExists(SettingsPath)
				? JsonSerializer.Deserialize(
					FileAccess.GetFileAsString(SettingsPath),
					ForgeJsonContext.Default.ForgeProviderSettings
				) ?? new()
				: new();
		}
		catch (Exception ex)
		{
			BV.PrintErr("Forge settings load failed: ", ex.Message);
			return new();
		}
	}

	public static void Save(ForgeProviderSettings settings)
	{
		try
		{
			using FileAccess file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
			file.StoreString(
				JsonSerializer.Serialize(
					settings.CloneForSave(),
					ForgeJsonContext.Default.ForgeProviderSettings
				)
			);
		}
		catch (Exception ex)
		{
			BV.PrintErr("Forge settings save failed: ", ex.Message);
		}
	}
}

internal static class ForgeConversationStore
{
	private const string Path = "user://creator/forge_chats.json";
	private static readonly System.Net.Http.HttpClient Http = new()
	{
		Timeout = TimeSpan.FromSeconds(20),
	};

	public static List<ForgeConversation> Load()
	{
		try
		{
			return FileAccess.FileExists(Path)
				? JsonSerializer.Deserialize(
					FileAccess.GetFileAsString(Path),
					ForgeJsonContext.Default.ListForgeConversation
				) ?? []
				: [];
		}
		catch
		{
			return [];
		}
	}

	public static void Save(List<ForgeConversation> chats)
	{
		using FileAccess file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
		file.StoreString(
			JsonSerializer.Serialize(chats, ForgeJsonContext.Default.ListForgeConversation)
		);
	}

	public static async Task<List<ForgeConversation>> LoadRemoteAsync()
	{
		if (string.IsNullOrWhiteSpace(CreatorAPI.Token))
			return [];
		using HttpRequestMessage listRequest = Request(
			HttpMethod.Get,
			"/v3/forge-llm/conversations"
		);
		using HttpResponseMessage listResponse = await Http.SendAsync(listRequest);
		if (!listResponse.IsSuccessStatusCode)
			return [];
		using JsonDocument listJson = JsonDocument.Parse(
			await listResponse.Content.ReadAsStringAsync()
		);
		if (!listJson.RootElement.TryGetProperty("conversations", out JsonElement summaries))
			return [];
		List<Task<ForgeConversation?>> loads = [];
		foreach (JsonElement summary in summaries.EnumerateArray().Take(30))
		{
			string id = summary.GetProperty("id").GetString() ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(id))
				loads.Add(LoadOneRemoteAsync(id));
		}
		return (await Task.WhenAll(loads))
			.Where(static chat => chat != null)
			.Select(static chat => chat!)
			.ToList();
	}

	public static async Task SaveRemoteAsync(ForgeConversation chat)
	{
		if (string.IsNullOrWhiteSpace(CreatorAPI.Token))
			return;
		string createJson = JsonSerializer.Serialize(
			new
			{
				id = chat.Id,
				title = chat.Title,
				source = "creator",
			}
		);
		using HttpRequestMessage create = Request(
			HttpMethod.Post,
			"/v3/forge-llm/conversations",
			createJson
		);
		using HttpResponseMessage createResponse = await Http.SendAsync(create);
		if (!createResponse.IsSuccessStatusCode)
			return;
		var messages = chat
			.Messages.Where(static message =>
				message.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(message.Content)
			)
			.Select(static message => new { role = message.Role, content = message.Content! })
			.TakeLast(200)
			.ToArray();
		string syncJson = JsonSerializer.Serialize(new { title = chat.Title, messages });
		using HttpRequestMessage sync = Request(
			HttpMethod.Put,
			$"/v3/forge-llm/conversations/{Uri.EscapeDataString(chat.Id)}/messages",
			syncJson
		);
		using HttpResponseMessage _ = await Http.SendAsync(sync);
	}

	private static async Task<ForgeConversation?> LoadOneRemoteAsync(string id)
	{
		using HttpRequestMessage request = Request(
			HttpMethod.Get,
			$"/v3/forge-llm/conversations/{Uri.EscapeDataString(id)}"
		);
		using HttpResponseMessage response = await Http.SendAsync(request);
		if (!response.IsSuccessStatusCode)
			return null;
		using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		JsonElement root = json.RootElement;
		JsonElement summary = root.GetProperty("conversation");
		ForgeConversation chat = new()
		{
			Id = id,
			Title = summary.GetProperty("title").GetString() ?? "New chat",
			Model = "forge-free",
		};
		if (
			summary.TryGetProperty("createdAt", out JsonElement created)
			&& DateTime.TryParse(created.GetString(), out DateTime createdAt)
		)
			chat.CreatedAt = createdAt;
		if (
			summary.TryGetProperty("updatedAt", out JsonElement updated)
			&& DateTime.TryParse(updated.GetString(), out DateTime updatedAt)
		)
			chat.UpdatedAt = updatedAt;
		foreach (JsonElement message in root.GetProperty("messages").EnumerateArray())
			chat.Messages.Add(
				new ForgeChatMessage
				{
					Role = message.GetProperty("role").GetString() ?? "assistant",
					Content = message.GetProperty("content").GetString() ?? string.Empty,
				}
			);
		return chat;
	}

	private static HttpRequestMessage Request(HttpMethod method, string path, string? json = null)
	{
		HttpRequestMessage request = new(method, Globals.ApiEndpoint.PathJoin(path));
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreatorAPI.Token);
		if (json != null)
			request.Content = new StringContent(json, Encoding.UTF8, "application/json");
		return request;
	}
}

public sealed class ForgeChatRequest
{
	[JsonPropertyName("model")]
	public string Model { get; set; } = string.Empty;

	[JsonPropertyName("messages")]
	public List<ForgeChatMessage> Messages { get; set; } = [];

	[JsonPropertyName("tools")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<ForgeChatToolDefinition>? Tools { get; set; }

	[JsonPropertyName("tool_choice")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ToolChoice { get; set; }

	[JsonPropertyName("temperature")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public float? Temperature { get; set; }

	[JsonPropertyName("chat_id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ChatId { get; set; }
}

public sealed class ForgeChatMessage
{
	[JsonPropertyName("role")]
	public string Role { get; set; } = string.Empty;

	[JsonPropertyName("content")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Content { get; set; }

	[JsonPropertyName("tool_calls")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<ForgeChatToolCall>? ToolCalls { get; set; }

	[JsonPropertyName("tool_call_id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ToolCallId { get; set; }

	[JsonPropertyName("name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Name { get; set; }

	public ForgeChatMessage Clone() =>
		new()
		{
			Role = Role,
			Content = Content,
			ToolCallId = ToolCallId,
			Name = Name,
			ToolCalls = ToolCalls?.ConvertAll(static call => call.Clone()),
		};
}

public sealed class ForgeChatToolDefinition
{
	[JsonPropertyName("type")]
	public string Type { get; set; } = "function";

	[JsonPropertyName("function")]
	public ForgeChatToolDefinitionFunction Function { get; set; } = new();
}

public sealed class ForgeChatToolDefinitionFunction
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("description")]
	public string Description { get; set; } = string.Empty;

	[JsonPropertyName("parameters")]
	public JsonElement Parameters { get; set; }
}

public sealed class ForgeChatToolCall
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("type")]
	public string Type { get; set; } = "function";

	[JsonPropertyName("function")]
	public ForgeChatToolFunctionCall Function { get; set; } = new();

	public ForgeChatToolCall Clone() =>
		new()
		{
			Id = Id,
			Type = Type,
			Function = new() { Name = Function.Name, Arguments = Function.Arguments },
		};
}

public sealed class ForgeChatToolFunctionCall
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("arguments")]
	public string Arguments { get; set; } = "{}";
}

public sealed class ForgeCompletionResult
{
	public List<ForgeChatMessage> TranscriptDelta { get; set; } = [];
	public string AssistantText { get; set; } = string.Empty;
	public List<ForgeToolEvent> ToolEvents { get; set; } = [];
}

public sealed class ForgeSearchInstancesArgs
{
	[JsonPropertyName("query")]
	public string? Query { get; set; }

	[JsonPropertyName("class_name")]
	public string? ClassName { get; set; }

	[JsonPropertyName("under_path")]
	public string? UnderPath { get; set; }

	[JsonPropertyName("limit")]
	public int Limit { get; set; } = 20;
}

public sealed class ForgeInspectInstanceArgs
{
	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;
}

public sealed class ForgeSelectInstancesArgs
{
	[JsonPropertyName("paths")]
	public List<string> Paths { get; set; } = [];

	[JsonPropertyName("mode")]
	public string Mode { get; set; } = "replace";
}

public sealed class ForgeDeleteInstanceArgs
{
	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;
}

public sealed class ForgeCreateInstanceArgs
{
	[JsonPropertyName("class_name")]
	public string ClassName { get; set; } = string.Empty;

	[JsonPropertyName("parent_path")]
	public string? ParentPath { get; set; }

	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("properties")]
	public JsonElement Properties { get; set; }
}

public sealed class ForgeRunLuauArgs
{
	[JsonPropertyName("source")]
	public string Source { get; set; } = string.Empty;

	[JsonPropertyName("compatibility")]
	public bool Compatibility { get; set; }

	[JsonPropertyName("reason")]
	public string? Reason { get; set; }
}

public sealed class ForgeSetInstancePropertiesArgs
{
	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;

	[JsonPropertyName("properties")]
	public JsonElement Properties { get; set; }
}

public sealed class ForgeEditScriptSourceArgs
{
	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;

	[JsonPropertyName("source")]
	public string Source { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ForgeProviderSettings))]
[JsonSerializable(typeof(ForgeConversation))]
[JsonSerializable(typeof(List<ForgeConversation>))]
[JsonSerializable(typeof(ForgeChatRequest))]
[JsonSerializable(typeof(ForgeChatMessage))]
[JsonSerializable(typeof(List<ForgeChatMessage>))]
[JsonSerializable(typeof(ForgeChatToolDefinition))]
[JsonSerializable(typeof(List<ForgeChatToolDefinition>))]
[JsonSerializable(typeof(ForgeChatToolCall))]
[JsonSerializable(typeof(List<ForgeChatToolCall>))]
[JsonSerializable(typeof(ForgeSearchInstancesArgs))]
[JsonSerializable(typeof(ForgeInspectInstanceArgs))]
[JsonSerializable(typeof(ForgeSelectInstancesArgs))]
[JsonSerializable(typeof(ForgeDeleteInstanceArgs))]
[JsonSerializable(typeof(ForgeCreateInstanceArgs))]
[JsonSerializable(typeof(ForgeSetInstancePropertiesArgs))]
[JsonSerializable(typeof(ForgeRunLuauArgs))]
[JsonSerializable(typeof(ForgeEditScriptSourceArgs))]
internal partial class ForgeJsonContext : JsonSerializerContext { }

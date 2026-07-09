// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Client.WebAPI.Interfaces;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

/// <summary>
/// Production server API implementation used by ServerAPI for world download,
/// player validation, heartbeat, and event logging.
/// </summary>
public sealed class HttpServerInterface : IServerInterface
{
	private readonly BVHttpClient _httpClient = new();
	private string _token = "";

	public void SetToken(string token)
	{
		_token = token ?? "";
	}

	public async Task<byte[]> DownloadWorld(long worldID)
	{
		using HttpRequestMessage request = CreateBinaryRequest(HttpMethod.Get, ApiPath($"/v3/world/server/tree?worldId={worldID}&stream=true"));
		using HttpResponseMessage response = await _httpClient.SendAsync(request);

		if (!response.IsSuccessStatusCode)
		{
			string body = await response.Content.ReadAsStringAsync();
			throw new HttpRequestException($"BrickVerse world download failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
		}

		byte[] data = await response.Content.ReadAsByteArrayAsync();
		if (LooksLikeJson(data))
		{
			string body = Encoding.UTF8.GetString(data);
			throw new InvalidOperationException($"BrickVerse world download returned JSON instead of stream: {body}");
		}

		return data;
	}

	public async Task<APIHeartbeatResponse> Heartbeat(string[] playerIDs)
	{
		HeartbeatRequest body = new(playerIDs?.Length ?? 0);
		using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, ApiPath("/v3/world/server/heartbeat"), body, BrickVerseJsonContext.Default.HeartbeatRequest);
		using HttpResponseMessage response = await _httpClient.SendAsync(request);
		return await ReadJsonResponse(response, "server heartbeat", BrickVerseJsonContext.Default.APIHeartbeatResponse);
	}

	public async Task<APIValidateResponse> ValidatePlayer(string token)
	{
		string escapedToken = Uri.EscapeDataString(token ?? string.Empty);
		using HttpRequestMessage request = CreateRequest(HttpMethod.Get, ApiPath($"/v3/world/server/user?joinToken={escapedToken}"));
		using HttpResponseMessage response = await _httpClient.SendAsync(request);
		return await ReadJsonResponse(response, "player validate", BrickVerseJsonContext.Default.APIValidateResponse);
	}

	public async Task LogEvent(ServerEventType eventType, Dictionary<string, string>? data = null)
	{
		string message = eventType switch
		{
			ServerEventType.ServerStarted => "Server started",
			ServerEventType.ServerStopped => "Server stopped",
			ServerEventType.ClientConnected => BuildPlayerEventMessage("Client connected", data),
			ServerEventType.ClientDisconnected => BuildPlayerEventMessage("Client disconnected", data),
			_ => eventType.ToString(),
		};

		await Log(message, ServerLogSource.Server, ServerLogLevel.Info);
	}

	public async Task Log(string log, ServerLogSource source = ServerLogSource.Server, ServerLogLevel level = ServerLogLevel.Info, long? timestampUnixMs = null)
	{
		long timestamp = timestampUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		LogIngestRequest body = new(
			NormalizeLogMessage(log),
			timestamp,
			MapSource(source),
			MapLevel(level)
		);

		using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, ApiPath("/v3/world/server/logs/ingest"), body, BrickVerseJsonContext.Default.LogIngestRequest);
		using HttpResponseMessage response = await _httpClient.SendAsync(request);

		if (!response.IsSuccessStatusCode)
		{
			string responseBody = await response.Content.ReadAsStringAsync();
			throw new HttpRequestException($"BrickVerse server event log failed: {(int)response.StatusCode} {response.ReasonPhrase} {responseBody}");
		}
	}

	private HttpRequestMessage CreateRequest(HttpMethod method, string url)
	{
		HttpRequestMessage request = new(method, url);
		request.Headers.TryAddWithoutValidation("Accept", "application/json");
		ApplyAuthorization(request);
		return request;
	}

	private HttpRequestMessage CreateBinaryRequest(HttpMethod method, string url)
	{
		HttpRequestMessage request = new(method, url);
		request.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
		ApplyAuthorization(request);
		return request;
	}

	private HttpRequestMessage CreateJsonRequest<T>(HttpMethod method, string url, T value, JsonTypeInfo<T> typeInfo)
	{
		string json = JsonSerializer.Serialize(value, typeInfo);
		HttpRequestMessage request = CreateRequest(method, url);
		request.Content = new StringContent(json, Encoding.UTF8, "application/json");
		return request;
	}

	private void ApplyAuthorization(HttpRequestMessage request)
	{
		if (string.IsNullOrWhiteSpace(_token))
		{
			return;
		}

		string authorization = _token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
			? _token
			: "Bearer " + _token;

		request.Headers.TryAddWithoutValidation("Authorization", authorization);
	}

	private static async Task<T> ReadJsonResponse<T>(HttpResponseMessage response, string action, JsonTypeInfo<T> typeInfo)
	{
		string body = await response.Content.ReadAsStringAsync();

		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"BrickVerse {action} failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
		}

		T? result = JsonSerializer.Deserialize(body, typeInfo);
		return result ?? throw new InvalidOperationException($"BrickVerse {action} returned an empty response.");
	}

	private static string ApiPath(string path)
	{
		return Globals.ApiEndpoint.TrimEnd('/') + path;
	}

	private static bool LooksLikeJson(byte[] bytes)
	{
		for (int i = 0; i < bytes.Length; i++)
		{
			byte value = bytes[i];
			if (value == (byte)' ' || value == (byte)'\t' || value == (byte)'\r' || value == (byte)'\n')
			{
				continue;
			}

			return value == (byte)'{' || value == (byte)'[';
		}

		return false;
	}

	private static string NormalizeLogMessage(string? log)
	{
		if (string.IsNullOrWhiteSpace(log))
		{
			return "(empty log)";
		}

		string normalized = log.Trim();
		return normalized.Length <= 1000 ? normalized : normalized[..1000];
	}

	private static string BuildPlayerEventMessage(string prefix, Dictionary<string, string>? data)
	{
		if (data != null && data.TryGetValue("userID", out string? userID) && !string.IsNullOrWhiteSpace(userID))
		{
			return $"{prefix}: userID={userID}";
		}

		return prefix;
	}

	private static string MapSource(ServerLogSource source)
	{
		return source switch
		{
			ServerLogSource.Client => "CLIENT",
			_ => "SERVER"
		};
	}

	private static string MapLevel(ServerLogLevel level)
	{
		return level switch
		{
			ServerLogLevel.Warning => "WARNING",
			ServerLogLevel.Error => "ERROR",
			_ => "INFO"
		};
	}
}

internal sealed record HeartbeatRequest(
	[property: JsonPropertyName("connectedClients")] int ConnectedClients
);

internal sealed record ValidatePlayerRequest(
	[property: JsonPropertyName("Token")] string Token
);

internal sealed record LogIngestRequest(
	[property: JsonPropertyName("Log")] string Log,
	[property: JsonPropertyName("Timestamp")] long Timestamp,
	[property: JsonPropertyName("Source")] string Source,
	[property: JsonPropertyName("Level")] string Level
);

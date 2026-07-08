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
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

/// <summary>
/// Production server API implementation used by ServerAPI for world download,
/// player validation, heartbeat, and event logging.
/// </summary>
public sealed class HttpServerInterface : IServerInterface
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true,
	};

	private readonly BVHttpClient _httpClient = new();
	private string _token = "";

	public void SetToken(string token)
	{
		_token = token ?? "";
	}

	public async Task<byte[]> DownloadWorld(int worldID)
	{
		using HttpRequestMessage request = CreateRequest(HttpMethod.Get, ApiPath($"/v3/world/server/tree?worldId={worldID}"));
		using HttpResponseMessage response = await _httpClient.SendAsync(request);

		if (!response.IsSuccessStatusCode)
		{
			string body = await response.Content.ReadAsStringAsync();
			throw new HttpRequestException($"BrickVerse world download failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
		}

		return await response.Content.ReadAsByteArrayAsync();
	}

	public async Task<APIHeartbeatResponse> Heartbeat(string[] playerIDs)
	{
		HeartbeatRequest body = new(playerIDs, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
		using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, ApiPath("/v3/world/server/heartbeat"), body);
		using HttpResponseMessage response = await _httpClient.SendAsync(request);
		return await ReadJsonResponse<APIHeartbeatResponse>(response, "server heartbeat");
	}

	public async Task<APIValidateResponse> ValidatePlayer(string token)
	{
		ValidatePlayerRequest body = new(token);
		using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, ApiPath("/v3/world/server/user"), body);
		using HttpResponseMessage response = await _httpClient.SendAsync(request);
		return await ReadJsonResponse<APIValidateResponse>(response, "player validate");
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

		using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, ApiPath("/v3/world/server/logs/ingest"), body);
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

	private HttpRequestMessage CreateJsonRequest<T>(HttpMethod method, string url, T value)
	{
		string json = JsonSerializer.Serialize(value, JsonOptions);
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

	private static async Task<T> ReadJsonResponse<T>(HttpResponseMessage response, string action)
	{
		string body = await response.Content.ReadAsStringAsync();

		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"BrickVerse {action} failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
		}

		T? result = JsonSerializer.Deserialize<T>(body, JsonOptions);
		return result ?? throw new InvalidOperationException($"BrickVerse {action} returned an empty response.");
	}

	private static string ApiPath(string path)
	{
		return Globals.ApiEndpoint.TrimEnd('/') + path;
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

	private sealed record HeartbeatRequest(string[] PlayerIDs, long SentAtUnix);
	private sealed record ValidatePlayerRequest(string Token);
	private sealed record LogIngestRequest(string Log, long Timestamp, string Source, string Level);
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Client.WebAPI;
using BrickVerse.Shared;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BrickVerse.Datamodel.Services;

[Static("Config"), ExplorerExclude]
[SaveIgnore]
public sealed partial class ConfigService : Instance
{
	private const int MaxKeyLength = 32;
	private const int MaxValueLength = 255;

	private readonly BVHttpClient _client = new();

	[ScriptMethod]
	public async Task<object?> GetConfigAsync(string key)
	{
		EnsureProduction();
		ValidateKey(key);
		ApplyAuthorizationHeader();

		string universeId = GetUniverseId();
		string routeType = Root.Network.IsServer ? "server" : "client";

		string url = Globals.ApiEndpoint +
			$"/v3/world/{routeType}/world-config/" +
			$"{Uri.EscapeDataString(universeId)}/" +
			$"{Uri.EscapeDataString(key)}";

		using HttpResponseMessage response = await _client.GetAsync(url);

		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}

		if (response.StatusCode == HttpStatusCode.Forbidden)
		{
			throw new UnauthorizedAccessException(
				await GetErrorMessageAsync(
					response,
					"This config is secret and cannot be accessed by the client."));
		}

		await EnsureSuccessAsync(response);

		string responseBody = await response.Content.ReadAsStringAsync();
		using JsonDocument document = JsonDocument.Parse(responseBody);

		if (!document.RootElement.TryGetProperty(
			"data",
			out JsonElement dataElement))
		{
			return null;
		}

		if (!dataElement.TryGetProperty(
			"value",
			out JsonElement valueElement))
		{
			return null;
		}

		return ParseJsonValue(valueElement);
	}

	[ScriptMethod]
	public async Task SetConfigAsync(
		string key,
		string value,
		bool secret = false)
	{
		EnsureProduction();
		EnsureServer();
		ValidateKey(key);
		ValidateValue(value);
		ApplyAuthorizationHeader();

		string universeId = GetUniverseId();

		string url = Globals.ApiEndpoint +
			"/v3/world/server/world-config/" +
			$"{Uri.EscapeDataString(universeId)}/" +
			$"{Uri.EscapeDataString(key)}";

		JsonObject requestBody = new()
		{
			["value"] = value,
			["secret"] = secret
		};

		using StringContent content = new(
			requestBody.ToJsonString(),
			Encoding.UTF8,
			"application/json");

		using HttpResponseMessage response = await _client.PutAsync(
			url,
			content);

		await EnsureSuccessAsync(response);
	}

	[ScriptMethod]
	public async Task DeleteConfigAsync(string key)
	{
		EnsureProduction();
		EnsureServer();
		ValidateKey(key);
		ApplyAuthorizationHeader();

		string universeId = GetUniverseId();

		string url = Globals.ApiEndpoint +
			"/v3/world/server/world-config/" +
			$"{Uri.EscapeDataString(universeId)}/" +
			$"{Uri.EscapeDataString(key)}";

		using HttpRequestMessage request = new(HttpMethod.Delete, url);
		using HttpResponseMessage response = await _client.SendAsync(request);

		await EnsureSuccessAsync(response);
	}

	private void ApplyAuthorizationHeader()
	{
		_client.DefaultRequestHeaders.Remove("Authorization");

		if (Root.Network.IsServer)
		{
			string serverAuthorization =
				ServerAPI.GetAuthorizationHeaderValue();

			if (string.IsNullOrWhiteSpace(serverAuthorization))
			{
				throw new InvalidOperationException(
					"Server authorization token is unavailable.");
			}

			_client.DefaultRequestHeaders["Authorization"] =
				NormalizeAuthorizationHeader(serverAuthorization);

			return;
		}

		if (string.IsNullOrWhiteSpace(ClientAuthAPI.JoinToken))
		{
			throw new InvalidOperationException(
				"Client join authorization token is unavailable.");
		}

		_client.DefaultRequestHeaders["Authorization"] =
			NormalizeAuthorizationHeader(ClientAuthAPI.JoinToken);
	}

	private string GetUniverseId()
	{
		if (Root.UniverseID == 0)
		{
			throw new InvalidOperationException(
				"Universe ID is unavailable.");
		}

		return Root.UniverseID.ToString();
	}

	private static string? GetStringClaim(
		JsonElement payload,
		string claimName)
	{
		if (!payload.TryGetProperty(
			claimName,
			out JsonElement claim))
		{
			return null;
		}

		return claim.ValueKind switch
		{
			JsonValueKind.String => claim.GetString(),
			JsonValueKind.Number => claim.ToString(),
			_ => null
		};
	}

	private static string NormalizeAuthorizationHeader(string token)
	{
		string normalized = token.Trim();

		if (normalized.StartsWith(
			"Bearer ",
			StringComparison.OrdinalIgnoreCase))
		{
			return normalized;
		}

		return "Bearer " + normalized;
	}

	private static async Task EnsureSuccessAsync(
		HttpResponseMessage response)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}

		string message = await GetErrorMessageAsync(
			response,
			$"Config request failed with HTTP status " +
			$"{(int)response.StatusCode}.");

		throw new HttpRequestException(
			message,
			null,
			response.StatusCode);
	}

	private static async Task<string> GetErrorMessageAsync(
		HttpResponseMessage response,
		string fallback)
	{
		string responseBody =
			await response.Content.ReadAsStringAsync();

		if (string.IsNullOrWhiteSpace(responseBody))
		{
			return fallback;
		}

		try
		{
			using JsonDocument document =
				JsonDocument.Parse(responseBody);

			if (document.RootElement.TryGetProperty(
				"message",
				out JsonElement messageElement))
			{
				return messageElement.GetString() ?? fallback;
			}
		}
		catch (JsonException)
		{
			// The API returned a non-JSON error response.
		}

		return fallback;
	}

	private static object? ParseJsonValue(JsonElement value)
	{
		return value.ValueKind switch
		{
			JsonValueKind.Null => null,
			JsonValueKind.String => value.GetString(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,

			JsonValueKind.Number
				when value.TryGetInt32(out int intValue)
				=> intValue,

			JsonValueKind.Number
				when value.TryGetInt64(out long longValue)
				=> longValue,

			JsonValueKind.Number => value.GetDouble(),

			JsonValueKind.Object or JsonValueKind.Array =>
				value.GetRawText(),

			_ => null
		};
	}

	private void EnsureProduction()
	{
		if (!Root.Network.IsProd)
		{
			throw new InvalidOperationException(
				"ConfigService can only be used in production.");
		}
	}

	private void EnsureServer()
	{
		if (!Root.Network.IsServer)
		{
			throw new InvalidOperationException(
				"This ConfigService method can only be called on the server.");
		}
	}

	private static void ValidateKey(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			throw new ArgumentException(
				"Config key cannot be empty.",
				nameof(key));
		}

		if (key.Length > MaxKeyLength)
		{
			throw new ArgumentOutOfRangeException(
				nameof(key),
				$"Config key cannot be longer than {MaxKeyLength} characters.");
		}
	}

	private static void ValidateValue(string value)
	{
		if (value is null)
		{
			throw new ArgumentNullException(nameof(value));
		}

		if (value.Length == 0)
		{
			throw new ArgumentException(
				"Config value cannot be empty.",
				nameof(value));
		}

		if (value.Length > MaxValueLength)
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				$"Config value cannot be longer than {MaxValueLength} characters.");
		}
	}
}

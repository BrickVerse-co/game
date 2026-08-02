// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Client.WebAPI.Interfaces;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

/// <summary>
/// Production client connector used by ClientAuthAPI.
/// </summary>
internal sealed class ClientConnector : IClientConnector
{
	private readonly BVHttpClient _httpClient = new();
	private string _token = "";

	public ClientConnector()
	{
		_httpClient.DefaultRequestHeaders["User-Agent"] = OfficialClientIntegrity.BuildUserAgent();
	}

	public void SetToken(string token)
	{
		_token = (token ?? "").Trim();
	}

	public async Task<APIServerStatus> CheckServerStatus()
	{
		using HttpRequestMessage request = CreateRequest(HttpMethod.Get, ApiPath("/v3/world/client/server/status"));
		using HttpResponseMessage response = await _httpClient.SendAsync(request);
		return await ReadJsonResponse(response, "server status", BrickVerseJsonContext.Default.APIServerStatus);
	}

	public async Task<APIClientAuthResponseMessage> Connect()
	{
		ClientConnectRequest body = new(
			Integrity: OfficialClientIntegrity.CreateProof()
		);

		for (int attempt = 1; attempt <= 6; attempt++)
		{
			using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, ApiPath("/v3/world/client/server/authorize-connection"), body, BrickVerseJsonContext.Default.ClientConnectRequest);
			using HttpResponseMessage response = await _httpClient.SendAsync(request);

			if (response.IsSuccessStatusCode)
			{
				return await ReadJsonResponse(response, "client connect", BrickVerseJsonContext.Default.APIClientAuthResponseMessage);
			}

			string responseBody = await response.Content.ReadAsStringAsync();
			if (!ShouldRetryClientConnect(response, responseBody) || attempt == 6)
			{
				string reason = ReadErrorMessage(responseBody)
					?? response.ReasonPhrase
					?? "The server rejected this client.";
				throw new HttpRequestException(
					$"Unable to join this BrickVerse server: {reason} (HTTP {(int)response.StatusCode})"
				);
			}

			BV.Print($"Client connect retry {attempt}/6: waiting for server awaken...");
			await Task.Delay(TimeSpan.FromMilliseconds(750));
		}

		throw new InvalidOperationException("BrickVerse client connect retry loop exhausted.");
	}

	private static string? ReadErrorMessage(string responseBody)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(responseBody);
			return document.RootElement.TryGetProperty("message", out JsonElement message)
				? message.GetString()
				: null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static bool ShouldRetryClientConnect(HttpResponseMessage response, string responseBody)
	{
		if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
		{
			return false;
		}

		return responseBody.Contains("World server not found", StringComparison.OrdinalIgnoreCase);
	}

	private HttpRequestMessage CreateRequest(HttpMethod method, string url)
	{
		HttpRequestMessage request = new(method, url);
		request.Headers.TryAddWithoutValidation("Accept", "application/json");
		ApplyAuthorization(request);
		ApplyIntegrityHeaders(request);
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

		string token = _token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
			? _token["Bearer ".Length..].Trim()
			: _token;

		request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
	}

	private static void ApplyIntegrityHeaders(HttpRequestMessage request)
	{
		ClientIntegrityProof proof = OfficialClientIntegrity.CreateProof();
		request.Headers.TryAddWithoutValidation("X-BrickVerse-Version", proof.Version);
		request.Headers.TryAddWithoutValidation("X-BrickVerse-Platform", proof.Platform);
		request.Headers.TryAddWithoutValidation("X-BrickVerse-Executable-Sha256", proof.ExecutableSha256);
		request.Headers.TryAddWithoutValidation("X-BrickVerse-Managed-Sha256", proof.ManagedSha256);
		request.Headers.TryAddWithoutValidation("X-BrickVerse-Build-Channel", proof.BuildChannel);
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
}

internal sealed record ClientConnectRequest(
	[property: JsonPropertyName("Integrity")] ClientIntegrityProof Integrity
);

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
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

/// <summary>
/// Production client connector used by ClientAuthAPI.
/// </summary>
internal sealed class ClientConnector : IClientConnector
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true,
	};

	private readonly BVHttpClient _httpClient = new();
	private string _token = "";

	public ClientConnector()
	{
		_httpClient.DefaultRequestHeaders["User-Agent"] = OfficialClientIntegrity.BuildUserAgent();
	}

	public void SetToken(string token)
	{
		_token = token ?? "";
	}

	public async Task<APIServerStatus> CheckServerStatus()
	{
		using HttpRequestMessage request = CreateRequest(HttpMethod.Get, ApiPath("/v3/world/client/server/status"));
		using HttpResponseMessage response = await _httpClient.SendAsync(request);
		return await ReadJsonResponse<APIServerStatus>(response, "server status");
	}

	public async Task<APIClientAuthResponseMessage> Connect()
	{
		ClientConnectRequest body = new(
			Integrity: OfficialClientIntegrity.CreateProof()
		);

		using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, ApiPath("/v3/world/client/server/authorize-connection"), body);
		using HttpResponseMessage response = await _httpClient.SendAsync(request);
		return await ReadJsonResponse<APIClientAuthResponseMessage>(response, "client connect");
	}

	private HttpRequestMessage CreateRequest(HttpMethod method, string url)
	{
		HttpRequestMessage request = new(method, url);
		request.Headers.TryAddWithoutValidation("Accept", "application/json");
		ApplyAuthorization(request);
		ApplyIntegrityHeaders(request);
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

	private static void ApplyIntegrityHeaders(HttpRequestMessage request)
	{
		ClientIntegrityProof proof = OfficialClientIntegrity.CreateProof();
		request.Headers.TryAddWithoutValidation("X-BrickVerse-Version", proof.Version);
		request.Headers.TryAddWithoutValidation("X-BrickVerse-Platform", proof.Platform);
		request.Headers.TryAddWithoutValidation("X-BrickVerse-Executable-Sha256", proof.ExecutableSha256);
		request.Headers.TryAddWithoutValidation("X-BrickVerse-Managed-Sha256", proof.ManagedSha256);
		request.Headers.TryAddWithoutValidation("X-BrickVerse-Build-Channel", proof.BuildChannel);
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

	private sealed record ClientConnectRequest(ClientIntegrityProof Integrity);
}

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
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

/// <summary>
/// Production server listener used by ClientAuthAPI.SendServerListen().
/// </summary>
internal sealed class ServerListener : IServerListener
{
	private readonly BVHttpClient _httpClient = new();
	private string _token = "";

	public void SetToken(string token)
	{
		_token = token ?? "";
	}

	public async Task<APIServerListenResponse> Listen()
	{
		ServerListenRequest body = new(
			Token: _token,
			Version: Globals.AppVersion,
			Platform: Globals.ResolveCurrentPlatform(),
			StartedAtUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds()
		);

		using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, ApiPath("/v3/world/server/awaken"), body, BrickVerseJsonContext.Default.ServerListenRequest);
		using HttpResponseMessage response = await _httpClient.SendAsync(request);
		return await ReadJsonResponse(response, "server listen", BrickVerseJsonContext.Default.APIServerListenResponse);
	}

	private HttpRequestMessage CreateJsonRequest<T>(HttpMethod method, string url, T value, JsonTypeInfo<T> typeInfo)
	{
		string json = JsonSerializer.Serialize(value, typeInfo);
		HttpRequestMessage request = new(method, url)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};

		request.Headers.TryAddWithoutValidation("Accept", "application/json");
		ApplyAuthorization(request);
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
}

internal sealed record ServerListenRequest(string Token, string Version, string Platform, long StartedAtUnix);

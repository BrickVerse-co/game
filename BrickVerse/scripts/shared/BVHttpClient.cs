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
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace BrickVerse.Shared;

public partial class BVHttpClient
{
	private static readonly System.Net.Http.HttpClient _httpClient = new();
	public Dictionary<string, string> DefaultRequestHeaders { get; set; } = [];

	public BVHttpClient()
	{
		DefaultRequestHeaders["User-Agent"] = $"BrickVerse Client {Globals.AppVersion}";
	}

	public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg)
	{
		if (Globals.UseNoHttp)
			throw new HttpRequestException("Http is disabled via feature flag");

		if (IsMultipart(msg.Content))
			ValidateMultipartBody(
				msg.Content,
				await msg.Content!.ReadAsByteArrayAsync()
			);

		ApplyDefaultHeaders(msg);
		return await _httpClient.SendAsync(
			msg,
			HttpCompletionOption.ResponseHeadersRead
		);
	}

	private static bool IsMultipart(HttpContent? content)
	{
		return string.Equals(
			content?.Headers.ContentType?.MediaType,
			"multipart/form-data",
			StringComparison.OrdinalIgnoreCase
		);
	}

	private void ApplyDefaultHeaders(HttpRequestMessage msg)
	{
		foreach ((string key, string val) in DefaultRequestHeaders)
		{
			if (!msg.Headers.Contains(key))
				msg.Headers.TryAddWithoutValidation(key, val);
		}
	}

	private static void ValidateMultipartBody(HttpContent? content, byte[] body)
	{
		MediaTypeHeaderValue? contentType = content?.Headers.ContentType;
		if (
			contentType == null
			|| !string.Equals(
				contentType.MediaType,
				"multipart/form-data",
				StringComparison.OrdinalIgnoreCase
			)
		)
			return;

		string? boundary = contentType.Parameters
			.FirstOrDefault(parameter =>
				parameter.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase)
			)
			?.Value
			?.Trim('"');

		if (string.IsNullOrWhiteSpace(boundary))
			throw new HttpRequestException("Multipart form is missing its boundary parameter.");

		byte[] openingBoundary = Encoding.ASCII.GetBytes($"--{boundary}\r\n");
		byte[] closingBoundary = Encoding.ASCII.GetBytes($"--{boundary}--");
		if (
			!body.AsSpan().StartsWith(openingBoundary)
			|| body.AsSpan().IndexOf(closingBoundary) < 0
		)
		{
			throw new HttpRequestException(
				"Multipart form boundary does not match the serialized request body."
			);
		}
	}

	public async Task<HttpResponseMessage> GetAsync(string url)
	{
		using HttpRequestMessage msg = new(HttpMethod.Get, url);
		return await SendAsync(msg);
	}

	public async Task<T?> GetFromJsonAsync<T>(string url, JsonTypeInfo<T> jsonTypeInfo)
	{
		using HttpRequestMessage msg = new(HttpMethod.Get, url);
		msg.Headers.TryAddWithoutValidation("Accept", "application/json");

		using HttpResponseMessage response = await SendAsync(msg);
		response.EnsureSuccessStatusCode();

		string json = await response.Content.ReadAsStringAsync();
		return JsonSerializer.Deserialize(json, jsonTypeInfo);
	}

	public async Task<byte[]> GetByteArrayAsync(string url)
	{
		using HttpResponseMessage response = await GetAsync(url);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadAsByteArrayAsync();
	}

	public async Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
	{
		using HttpRequestMessage msg = new(HttpMethod.Post, url) { Content = content };

		return await SendAsync(msg);
	}

	public async Task<HttpResponseMessage> PostAsJsonAsync<T>(
		string url,
		T value,
		JsonTypeInfo<T> jsonTypeInfo
	)
	{
		string json = JsonSerializer.Serialize(value, jsonTypeInfo);

		using HttpRequestMessage msg = new(HttpMethod.Post, url)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};

		return await SendAsync(msg);
	}

	public async Task<string> GetStringAsync(string url)
	{
		using HttpResponseMessage response = await GetAsync(url);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadAsStringAsync();
	}

	public async Task<HttpResponseMessage> PutAsync(string url, HttpContent content)
	{
		using HttpRequestMessage msg = new(HttpMethod.Put, url) { Content = content };
		return await SendAsync(msg);
	}

	public async Task<HttpResponseMessage> PutAsJsonAsync<T>(
		string url,
		T value,
		JsonTypeInfo<T> jsonTypeInfo
	)
	{
		string json = JsonSerializer.Serialize(value, jsonTypeInfo);
		using HttpRequestMessage msg = new(HttpMethod.Put, url)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
		return await SendAsync(msg);
	}
}

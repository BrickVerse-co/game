// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Godot;
#if !USE_NATIVE_HTTP
using System;
using System.Net;
#endif

namespace BrickVerse.Shared;

public partial class BVHttpClient
{
	private const int DefaultDownloadChunkSize = 10000;
#if USE_NATIVE_HTTP
	private static readonly HttpClient _httpClient = new();
#endif
	public Dictionary<string, string> DefaultRequestHeaders { get; set; } = [];

	public BVHttpClient()
	{
		DefaultRequestHeaders["User-Agent"] = $"BrickVerse Client {Globals.AppVersion}";
	}

#if !USE_NATIVE_HTTP
	public Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg)
	{
		if (Globals.UseNoHttp)
			throw new HttpRequestException("Http is disabled via feature flag");

		TaskCompletionSource<HttpResponseMessage> tcs = new();

		Callable
			.From(() =>
			{
				async void Run()
				{
					try
					{
						byte[] body =
							msg.Content != null ? await msg.Content.ReadAsByteArrayAsync() : [];

						List<string> headers = [];

						foreach ((string k, string v) in DefaultRequestHeaders)
						{
							if (
								k.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
								&& msg.Headers.Authorization != null
							)
							{
								continue;
							}

							headers.Add($"{k}: {v}");
						}

						foreach (var item in msg.Headers)
							headers.Add($"{item.Key}: {string.Join(", ", item.Value)}");

						if (msg.Content != null)
						{
							foreach (var item in msg.Content.Headers)
							{
								if (item.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
									continue;

								headers.Add($"{item.Key}: {string.Join(", ", item.Value)}");
							}

							headers.Add($"Content-Length: {body.Length}");
						}

						/*PT.Print("=== BVHttpClient Request ===");
						PT.Print($"URL: {msg.RequestUri}");
						PT.Print($"Method: {msg.Method}");
						PT.Print($"Body Length: {body.Length}");
						foreach (string header in headers)
							PT.Print(header);

						*/

						HttpRequest req = new() { DownloadChunkSize = DefaultDownloadChunkSize };

						Globals.Singleton.AddChild(req);

						req.RequestCompleted += (
							result,
							responseCode,
							responseHeaders,
							responseBody
						) =>
						{
							HttpResponseMessage response = new((HttpStatusCode)responseCode)
							{
								Content = new ByteArrayContent(responseBody),
							};

							foreach (string header in responseHeaders)
							{
								string[] parts = header.Split(':', 2);

								if (parts.Length != 2)
									continue;

								string key = parts[0].Trim();
								string value = parts[1].Trim();

								if (!response.Headers.TryAddWithoutValidation(key, value))
									response.Content.Headers.TryAddWithoutValidation(key, value);
							}

							req.QueueFree();
							tcs.TrySetResult(response);
						};

						Godot.HttpClient.Method method = Enum.Parse<Godot.HttpClient.Method>(
							msg.Method.Method.ToLowerInvariant().Capitalize()
						);

						string[] headerArray = headers.ToArray();
						byte[] bodyArray = body;

						Error error = req.RequestRaw(
							msg.RequestUri?.ToString() ?? throw new InvalidOperationException("URL is null"),
							headerArray,
							method,
							bodyArray
						);
						
						if (error != Error.Ok)
						{
							req.QueueFree();
							tcs.TrySetException(
								new HttpRequestException($"HttpRequest failed with error: {error}")
							);
						}
					}
					catch (Exception ex)
					{
						tcs.TrySetException(ex);
					}
				}

				Run();
			})
			.CallDeferred();

		return tcs.Task;
	}
#else
	public Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg)
	{
		foreach ((string key, string val) in DefaultRequestHeaders)
		{
			msg.Headers.TryAddWithoutValidation(key, val);
		}
		return _httpClient.SendAsync(msg);
	}
#endif

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

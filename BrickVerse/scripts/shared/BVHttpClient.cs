// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace BrickVerse.Shared;

public partial class BVHttpClient
{
	// Android's .NET HTTP transport is unreliable in some Godot builds.
	// Register a native JSON POST bridge once during mobile startup and every
	// BVHttpClient caller can keep using the normal PostAsync/SendAsync API.
	public static Func<
		HttpRequestMessage,
		CancellationToken,
		Task<HttpResponseMessage>
	>? NativeSender { get; set; }

	// Let .NET select the appropriate HTTP transport for the current platform.
	// This is especially important on Android, where forcing SocketsHttpHandler
	// bypasses the platform-specific handler/runtime configuration.
	private static readonly System.Net.Http.HttpClient _httpClient = new()
	{
		Timeout = TimeSpan.FromSeconds(30),
	};
	public Dictionary<string, string> DefaultRequestHeaders { get; } = [];
	public Func<CancellationToken, Task>? BeforeRequestAsync { get; set; }

	public BVHttpClient()
	{
		DefaultRequestHeaders["User-Agent"] = $"BrickVerse Client {Globals.AppVersion}";
	}

	public static StringContent FormString(string name, string value)
	{
		StringContent content = new(value, Encoding.UTF8, "text/plain");
		content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
		{
			Name = QuoteFormValue(name, nameof(name)),
		};
		return content;
	}

	public static ByteArrayContent FormFile(
		string name,
		string fileName,
		byte[] data,
		string contentType = "application/octet-stream"
	)
	{
		ByteArrayContent content = new(data);
		content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
		content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
		{
			Name = QuoteFormValue(name, nameof(name)),
			FileName = QuoteFormValue(System.IO.Path.GetFileName(fileName), nameof(fileName)),
		};
		return content;
	}

	private static string QuoteFormValue(string value, string parameterName)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new ArgumentException("Multipart form value cannot be empty.", parameterName);
		if (value.Contains('\r') || value.Contains('\n') || value.Contains('"'))
			throw new ArgumentException(
				"Multipart form value contains invalid header characters.",
				parameterName
			);

		return $"\"{value}\"";
	}

	public async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage msg,
		CancellationToken cancellationToken = default
	)
	{
		if (Globals.UseNoHttp)
			throw new HttpRequestException("Http is disabled via feature flag");

		if (BeforeRequestAsync != null)
			await BeforeRequestAsync(cancellationToken);

		ApplyDefaultHeaders(msg);

		if (Godot.OS.HasFeature("android") && NativeSender != null)
		{
			return await NativeSender(msg, cancellationToken);
		}

		return await _httpClient.SendAsync(
			msg,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken
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

	public async Task<HttpResponseMessage> PostAsync(
		string url,
		HttpContent content,
		CancellationToken cancellationToken = default
	)
	{
		using HttpRequestMessage msg = new(HttpMethod.Post, url) { Content = content };
		try
		{
			return await SendAsync(msg, cancellationToken);
		}
		finally
		{
			// HttpClient.PostAsync does not take ownership of caller content.
			// Match that behavior when disposing our temporary request message.
			msg.Content = null;
		}
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
		try
		{
			return await SendAsync(msg);
		}
		finally
		{
			msg.Content = null;
		}
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

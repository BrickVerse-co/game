// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Data;
using BrickVerse.Scripting;
using BrickVerse.Shared;

namespace BrickVerse.Datamodel.Services;

[Static("Http"), ExplorerExclude]
[SaveIgnore]
public sealed partial class HttpService : Instance
{
	private const int MaxRequestsPerMinute = 90;
	private const int MaxResponseBytes = 16 * 1024 * 1024;
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
	private static readonly HashSet<string> BlockedRequestHeaders = new(
		StringComparer.OrdinalIgnoreCase
	)
	{
		"Host",
		"Content-Length",
		"Transfer-Encoding",
		"Connection",
		"Proxy-Connection",
		"Proxy-Authorization",
		"Forwarded",
		"X-Forwarded-For",
		"X-Forwarded-Host",
		"X-Forwarded-Proto",
		"BV-World-ID",
		"BV-Game-ID",
	};

	private readonly object _rateLimitLock = new();
	private int _requestsThisMinute = 0;
	private int _currentMinute;
	private BVHttpClient _client = new();

	public override void Init()
	{
		_client = new BVHttpClient();
		base.Init();
	}

	private bool RateLimit()
	{
		lock (_rateLimitLock)
		{
			int minute = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60);

			if (minute != _currentMinute)
			{
				_currentMinute = minute;
				_requestsThisMinute = 0;
			}

			if (_requestsThisMinute >= MaxRequestsPerMinute)
			{
				return false;
			}

			_requestsThisMinute++;
			return true;
		}
	}

	private bool LegacyRateLimit(BVCallback? callback = null)
	{
		if (!Root.Network.IsServer)
		{
			callback?.Invoke(null, true, "Cannot call Http functions from client");
			return false;
		}

		bool ratelimit = RateLimit();

		if (!ratelimit)
		{
			callback?.Invoke(null, true, "Http limit exceeded");
			return false;
		}
		return true;
	}

	[ScriptMethod]
	public async Task<HttpResponseData> RequestAsync(HttpRequestData data)
	{
		ServerGuard();

		if (string.IsNullOrWhiteSpace(data.URL))
		{
			throw new InvalidOperationException("URL is required");
		}

		if (!RateLimit())
		{
			throw new InvalidOperationException("Http limit exceeded");
		}

		Uri requestUri = await ValidateUrlAsync(data.URL, Root.IsLocalTest);

		HttpMethod method = data.Method switch
		{
			HttpRequestData.HttpRequestMethodEnum.Get => HttpMethod.Get,
			HttpRequestData.HttpRequestMethodEnum.Post => HttpMethod.Post,
			HttpRequestData.HttpRequestMethodEnum.Put => HttpMethod.Put,
			HttpRequestData.HttpRequestMethodEnum.Patch => HttpMethod.Patch,
			HttpRequestData.HttpRequestMethodEnum.Delete => HttpMethod.Delete,
			_ => throw new InvalidOperationException("Method not supported"),
		};

		using HttpRequestMessage msg = CreateRequestMessage(
			method,
			requestUri,
			data.Body,
			data.Headers
		);
		msg.Headers.TryAddWithoutValidation("BV-World-ID", Root.WorldID.ToString());

		using CancellationTokenSource timeout = new(RequestTimeout);
		using HttpResponseMessage res = await _client.SendAsync(msg, timeout.Token);

		// This catches redirect escapes only if BVHttpClient exposes the final RequestUri.
		// For complete protection, automatic redirects must be disabled in BVHttpClient.
		if (res.RequestMessage?.RequestUri is Uri finalUri)
		{
			await ValidateUrlAsync(finalUri.AbsoluteUri, Root.IsLocalTest);
		}

		if (
			res.Content.Headers.ContentLength is long contentLength
			&& contentLength > MaxResponseBytes
		)
		{
			throw new InvalidOperationException(
				$"HTTP response exceeds the {MaxResponseBytes} byte limit"
			);
		}

		byte[] buffer = await ReadLimitedResponseAsync(res.Content, timeout.Token);
		Dictionary<string, string> headers = [];

		foreach ((string key, IEnumerable<string> val) in res.Headers)
		{
			headers[key] = string.Join(",", val);
		}

		foreach ((string key, IEnumerable<string> val) in res.Content.Headers)
		{
			headers[key] = string.Join(",", val);
		}

		return new HttpResponseData
		{
			Success = res.IsSuccessStatusCode,
			StatusCode = (int)res.StatusCode,
			Body = DecodeResponseBody(buffer, res.Content.Headers.ContentType?.CharSet),
			Buffer = buffer,
			Headers = headers,
		};
	}

	public static void CheckURLPass(string url, bool isLocalTest)
	{
		ValidateUrlAsync(url, isLocalTest).GetAwaiter().GetResult();
	}

	private static async Task<Uri> ValidateUrlAsync(string url, bool isLocalTest)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri))
		{
			throw new InvalidOperationException("Invalid URL");
		}

		if (parsedUri.Scheme != Uri.UriSchemeHttps && parsedUri.Scheme != Uri.UriSchemeHttp)
		{
			throw new InvalidOperationException("Only HTTP and HTTPS URLs are allowed");
		}

		if (!string.IsNullOrEmpty(parsedUri.UserInfo))
		{
			throw new InvalidOperationException("URLs containing credentials are not allowed");
		}

		if (
			string.IsNullOrWhiteSpace(parsedUri.Host)
			|| parsedUri.HostNameType == UriHostNameType.Unknown
		)
		{
			throw new InvalidOperationException("Invalid URL host");
		}

		if (isLocalTest)
		{
			return parsedUri;
		}

		if (parsedUri.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidOperationException("Only HTTPS is allowed in production");
		}

		string host = parsedUri.IdnHost.TrimEnd('.').ToLowerInvariant();
		if (
			host is "localhost" or "localhost.localdomain" or "loopback"
			|| host.EndsWith(".localhost", StringComparison.Ordinal)
		)
		{
			throw new InvalidOperationException("Access to local hosts is not allowed");
		}

		if (IPAddress.TryParse(host, out IPAddress? literalAddress))
		{
			if (IsBlockedAddress(literalAddress))
			{
				throw new InvalidOperationException(
					"Access to local or private IP addresses is not allowed"
				);
			}

			throw new InvalidOperationException("Raw IP address URLs are not allowed");
		}

		IPAddress[] addresses;
		try
		{
			addresses = await Dns.GetHostAddressesAsync(host);
		}
		catch (Exception ex) when (ex is not InvalidOperationException)
		{
			throw new InvalidOperationException("URL host could not be resolved", ex);
		}

		if (addresses.Length == 0)
		{
			throw new InvalidOperationException("URL host did not resolve to an address");
		}

		foreach (IPAddress address in addresses)
		{
			if (IsBlockedAddress(address))
			{
				throw new InvalidOperationException(
					"URL resolves to a local, private, reserved, or metadata address"
				);
			}
		}

		return parsedUri;
	}

	private static bool IsBlockedAddress(IPAddress address)
	{
		if (address.IsIPv4MappedToIPv6)
		{
			address = address.MapToIPv4();
		}

		if (
			IPAddress.IsLoopback(address)
			|| address.Equals(IPAddress.Any)
			|| address.Equals(IPAddress.IPv6Any)
			|| address.Equals(IPAddress.None)
		)
		{
			return true;
		}

		byte[] bytes = address.GetAddressBytes();
		if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
		{
			return bytes[0] == 0
				|| bytes[0] == 10
				|| bytes[0] == 127
				|| (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
				|| (bytes[0] == 169 && bytes[1] == 254)
				|| (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
				|| (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
				|| (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
				|| (bytes[0] == 192 && bytes[1] == 168)
				|| (bytes[0] == 198 && bytes[1] is 18 or 19)
				|| (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
				|| (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
				|| bytes[0] >= 224;
		}

		return address.IsIPv6LinkLocal
			|| address.IsIPv6Multicast
			|| address.IsIPv6SiteLocal
			|| address.Equals(IPAddress.IPv6Loopback)
			|| (bytes[0] & 0xFE) == 0xFC
			|| IsIPv6DocumentationRange(bytes);
	}

	private static bool IsIPv6DocumentationRange(byte[] bytes)
	{
		return bytes.Length == 16
			&& bytes[0] == 0x20
			&& bytes[1] == 0x01
			&& bytes[2] == 0x0D
			&& bytes[3] == 0xB8;
	}

	private static HttpRequestMessage CreateRequestMessage(
		HttpMethod method,
		Uri uri,
		string? body,
		Dictionary<string, string>? headers
	)
	{
		HttpContent? content = body == null ? null : new StringContent(body, Encoding.UTF8);
		HttpRequestMessage message = new(method, uri) { Content = content };

		if (headers == null)
		{
			return message;
		}

		foreach ((string key, string value) in headers)
		{
			if (string.IsNullOrWhiteSpace(key) || value.Contains('\r') || value.Contains('\n'))
			{
				message.Dispose();
				throw new InvalidOperationException("Invalid HTTP header");
			}

			if (BlockedRequestHeaders.Contains(key))
			{
				message.Dispose();
				throw new InvalidOperationException($"The '{key}' header is not allowed");
			}

			if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
			{
				if (
					message.Content == null
					|| !MediaTypeHeaderValue.TryParse(value, out MediaTypeHeaderValue? contentType)
				)
				{
					message.Dispose();
					throw new InvalidOperationException("Invalid Content-Type header");
				}
				message.Content.Headers.ContentType = contentType;
			}
			else if (!message.Headers.TryAddWithoutValidation(key, value))
			{
				message.Dispose();
				throw new InvalidOperationException($"Invalid HTTP header '{key}'");
			}
		}

		return message;
	}

	private static async Task<byte[]> ReadLimitedResponseAsync(
		HttpContent content,
		CancellationToken cancellationToken
	)
	{
		await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
		using MemoryStream output = new();
		byte[] chunk = new byte[81920];
		int total = 0;

		while (true)
		{
			int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
			if (read == 0)
			{
				break;
			}

			total += read;
			if (total > MaxResponseBytes)
			{
				throw new InvalidOperationException(
					$"HTTP response exceeds the {MaxResponseBytes} byte limit"
				);
			}

			await output.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
		}

		return output.ToArray();
	}

	private static string DecodeResponseBody(byte[] buffer, string? charset)
	{
		if (buffer.Length == 0)
		{
			return string.Empty;
		}

		try
		{
			return string.IsNullOrWhiteSpace(charset)
				? Encoding.UTF8.GetString(buffer)
				: Encoding.GetEncoding(charset.Trim('"')).GetString(buffer);
		}
		catch (ArgumentException)
		{
			return Encoding.UTF8.GetString(buffer);
		}
	}

	[ScriptMethod]
	public async Task<string> GetAsync(string url, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(
			new()
			{
				URL = url,
				Method = HttpRequestData.HttpRequestMethodEnum.Get,
				Headers = headers,
			}
		);
		return response.Body;
	}

	[ScriptMethod]
	public async Task<string> PostAsync(
		string url,
		string body,
		Dictionary<string, string>? headers = null
	)
	{
		HttpResponseData response = await RequestAsync(
			new()
			{
				URL = url,
				Body = body,
				Method = HttpRequestData.HttpRequestMethodEnum.Post,
				Headers = headers,
			}
		);
		return response.Body;
	}

	[ScriptMethod]
	public async Task<string> PutAsync(
		string url,
		string body,
		Dictionary<string, string>? headers = null
	)
	{
		HttpResponseData response = await RequestAsync(
			new()
			{
				URL = url,
				Body = body,
				Method = HttpRequestData.HttpRequestMethodEnum.Put,
				Headers = headers,
			}
		);
		return response.Body;
	}

	[ScriptMethod]
	public async Task<string> DeleteAsync(
		string url,
		string body,
		Dictionary<string, string>? headers = null
	)
	{
		HttpResponseData response = await RequestAsync(
			new()
			{
				URL = url,
				Body = body,
				Method = HttpRequestData.HttpRequestMethodEnum.Delete,
				Headers = headers,
			}
		);
		return response.Body;
	}

	[ScriptMethod]
	public async Task<string> PatchAsync(
		string url,
		string body,
		Dictionary<string, string>? headers = null
	)
	{
		HttpResponseData response = await RequestAsync(
			new()
			{
				URL = url,
				Body = body,
				Method = HttpRequestData.HttpRequestMethodEnum.Patch,
				Headers = headers,
			}
		);
		return response.Body;
	}

	[ScriptMethod]
	public async Task<byte[]> GetBufferAsync(string url, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(
			new()
			{
				URL = url,
				Method = HttpRequestData.HttpRequestMethodEnum.Get,
				Headers = headers,
			}
		);
		return response.Buffer;
	}

	[ScriptMethod]
	public async Task<byte[]> PostBufferAsync(
		string url,
		string body,
		Dictionary<string, string>? headers = null
	)
	{
		HttpResponseData response = await RequestAsync(
			new()
			{
				URL = url,
				Body = body,
				Method = HttpRequestData.HttpRequestMethodEnum.Post,
				Headers = headers,
			}
		);
		return response.Buffer;
	}

	[ScriptMethod]
	public async Task<byte[]> PutBufferAsync(
		string url,
		string body,
		Dictionary<string, string>? headers = null
	)
	{
		HttpResponseData response = await RequestAsync(
			new()
			{
				URL = url,
				Body = body,
				Method = HttpRequestData.HttpRequestMethodEnum.Put,
				Headers = headers,
			}
		);
		return response.Buffer;
	}

	[ScriptMethod]
	public async Task<byte[]> DeleteBufferAsync(
		string url,
		string body,
		Dictionary<string, string>? headers = null
	)
	{
		HttpResponseData response = await RequestAsync(
			new()
			{
				URL = url,
				Body = body,
				Method = HttpRequestData.HttpRequestMethodEnum.Delete,
				Headers = headers,
			}
		);
		return response.Buffer;
	}

	[ScriptMethod]
	public async Task<byte[]> PatchBufferAsync(
		string url,
		string body,
		Dictionary<string, string>? headers = null
	)
	{
		HttpResponseData response = await RequestAsync(
			new()
			{
				URL = url,
				Body = body,
				Method = HttpRequestData.HttpRequestMethodEnum.Patch,
				Headers = headers,
			}
		);
		return response.Buffer;
	}

	[ScriptMethod, Attributes.Obsolete("Use GetAsync instead")]
	public void Get(
		string url,
		BVCallback? callback = null,
		Dictionary<string, string>? headers = null
	)
	{
		ServerGuard();
		if (LegacyRateLimit(callback))
		{
			DoLegacyRequest(HttpMethod.Get, url, null, callback, headers);
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use PostAsync instead")]
	public void Post(
		string url,
		string body,
		BVCallback? callback = null,
		Dictionary<string, string>? headers = null
	)
	{
		ServerGuard();
		if (LegacyRateLimit(callback))
		{
			DoLegacyRequest(HttpMethod.Post, url, body, callback, headers);
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use PutAsync instead")]
	public void Put(
		string url,
		string body,
		BVCallback? callback = null,
		Dictionary<string, string>? headers = null
	)
	{
		ServerGuard();
		if (LegacyRateLimit(callback))
		{
			DoLegacyRequest(HttpMethod.Put, url, body, callback, headers);
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use DeleteAsync instead")]
	public void Delete(
		string url,
		string body,
		BVCallback? callback = null,
		Dictionary<string, string>? headers = null
	)
	{
		ServerGuard();
		if (LegacyRateLimit(callback))
		{
			DoLegacyRequest(HttpMethod.Delete, url, body, callback, headers);
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use PatchAsync instead")]
	public void Patch(
		string url,
		string body,
		BVCallback? callback = null,
		Dictionary<string, string>? headers = null
	)
	{
		ServerGuard();
		if (LegacyRateLimit(callback))
		{
			DoLegacyRequest(HttpMethod.Patch, url, body, callback, headers);
		}
	}

	private async void DoLegacyRequest(
		HttpMethod method,
		string url,
		string? body,
		BVCallback? callback,
		Dictionary<string, string>? headers
	)
	{
		try
		{
			Uri requestUri = await ValidateUrlAsync(url, Root.IsLocalTest);
			using HttpRequestMessage msg = CreateRequestMessage(method, requestUri, body, headers);
			msg.Headers.TryAddWithoutValidation("BV-World-ID", Root.WorldID.ToString());

			using CancellationTokenSource timeout = new(RequestTimeout);
			using HttpResponseMessage res = await _client.SendAsync(msg, timeout.Token);

			if (res.RequestMessage?.RequestUri is Uri finalUri)
			{
				await ValidateUrlAsync(finalUri.AbsoluteUri, Root.IsLocalTest);
			}

			res.EnsureSuccessStatusCode();
			byte[] responseBytes = await ReadLimitedResponseAsync(res.Content, timeout.Token);
			callback?.Invoke(
				DecodeResponseBody(responseBytes, res.Content.Headers.ContentType?.CharSet),
				false,
				""
			);
		}
		catch (Exception ex)
		{
			callback?.Invoke(null, true, ex.Message);
		}
	}

	private void ServerGuard()
	{
		if (!Root.Network.IsServer)
			throw new InvalidOperationException("Http can only be accessed by server");
	}
}

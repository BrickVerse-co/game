// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Client.WebAPI;
using BrickVerse.Shared;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BrickVerse.Providers.Datastore;

public sealed class BVDatastoreProvider : IDatastoreProvider
{
	private const int MaxReadRequestsPerMinute = 30;
	private const int ReadRequestsPerPlayerModifier = 10;

	private const int MaxWriteRequestsPerMinute = 30;
	private const int WriteRequestsPerPlayerModifier = 10;

	private readonly BVHttpClient _client = new();

	private string _dataStoreName = string.Empty;
	private Datamodel.Data.Datastore _dataStore = null!;

	private int _quotaMinute = -1;
	private int _readRequestsThisMinute;
	private int _writeRequestsThisMinute;

	private bool _disposed;

	public void Connect(string dataStoreName, Datamodel.Data.Datastore dataStore)
	{
		ThrowIfDisposed();

		_dataStoreName = dataStoreName;
		_dataStore = dataStore;

		_client.DefaultRequestHeaders.Remove("Authorization");

		string authorization = ServerAPI.GetAuthorizationHeaderValue();
		if (!string.IsNullOrWhiteSpace(authorization))
		{
			_client.DefaultRequestHeaders["Authorization"] = authorization;
		}
	}

	public async Task<object?> GetAsync(string key)
	{
		ThrowIfDisposed();
		EnsureConnected();
		ValidateKey(key);
		ConsumeReadRequest();

		string url = Globals.ApiEndpoint + $"/v3/world/server/storage?storeName={Uri.EscapeDataString(_dataStoreName)}&key={Uri.EscapeDataString(key)}";

		using HttpResponseMessage response = await _client.GetAsync(url);

		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}

		response.EnsureSuccessStatusCode();

		using JsonDocument document = JsonDocument.Parse(
			await response.Content.ReadAsStringAsync());

		if (!document.RootElement.TryGetProperty("value", out JsonElement valueRoot))
		{
			return null;
		}

		JsonElement value = valueRoot;

		if (valueRoot.ValueKind == JsonValueKind.Object &&
			valueRoot.TryGetProperty("value", out JsonElement wrapped))
		{
			value = wrapped;
		}

		return ParseJsonValue(value);
	}

	[RequiresUnreferencedCode()]
	[RequiresDynamicCode()]
	public async Task SetAsync(string key, object? value)
	{
		ThrowIfDisposed();
		EnsureConnected();
		ValidateKey(key);
		ValidateValue(value);
		ConsumeWriteRequest();

		JsonObject body = new()
		{
			["storeName"] = _dataStoreName,
			["key"] = key,
			["value"] = new JsonObject
			{
				["value"] = JsonSerializer.SerializeToNode(value)
			}
		};

		using StringContent content = new(
			body.ToJsonString(),
			Encoding.UTF8,
			"application/json");

		using HttpResponseMessage response = await _client.PutAsync(
			Globals.ApiEndpoint + "/v3/world/server/storage",
			content);

		response.EnsureSuccessStatusCode();
	}

	// ---------------------------------------------------------------------
	// Compatibility wrappers
	// ---------------------------------------------------------------------

	[ObsoleteAttribute("Use GetAsync instead.")]
	public Task<object?> ReadData(string key)
	{
		return GetAsync(key);
	}

	[ObsoleteAttribute("Use SetAsync instead.")]
	public Task WriteData(string key, object? value)
	{
		return SetAsync(key, value);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		GC.SuppressFinalize(this);
	}

	private void ConsumeReadRequest()
	{
		ResetQuotaWindow();

		int limit = MaxReadRequestsPerMinute +
			(ReadRequestsPerPlayerModifier * _dataStore.DatastoreService.Root.Players.PlayersCount);

		if (_readRequestsThisMinute >= limit)
		{
			throw new DataStoreQuotaException("Read quota exceeded.");
		}

		_readRequestsThisMinute++;
	}

	private void ConsumeWriteRequest()
	{
		ResetQuotaWindow();

		int limit = MaxWriteRequestsPerMinute +
			(WriteRequestsPerPlayerModifier * _dataStore.DatastoreService.Root.Players.PlayersCount);

		if (_writeRequestsThisMinute >= limit)
		{
			throw new DataStoreQuotaException("Write quota exceeded.");
		}

		_writeRequestsThisMinute++;
	}

	private void ResetQuotaWindow()
	{
		int currentMinute =
			(int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60);

		if (_quotaMinute == currentMinute)
		{
			return;
		}

		_quotaMinute = currentMinute;
		_readRequestsThisMinute = 0;
		_writeRequestsThisMinute = 0;
	}

	private void EnsureConnected()
	{
		if (_dataStore == null || string.IsNullOrWhiteSpace(_dataStoreName))
		{
			throw new InvalidOperationException(
				"The datastore provider has not been connected.");
		}
	}

	private static void ValidateKey(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			throw new ArgumentException(
				"Datastore key cannot be empty.",
				nameof(key));
		}
	}

	private static void ValidateValue(object? value)
	{
		if (value is null ||
			value is string ||
			value is bool ||
			value is byte ||
			value is sbyte ||
			value is short ||
			value is ushort ||
			value is int ||
			value is uint ||
			value is long ||
			value is ulong ||
			value is float ||
			value is double ||
			value is decimal)
		{
			return;
		}

		throw new InvalidOperationException(
			"Datastore values must be null, strings, booleans, or numbers.");
	}

	private static object? ParseJsonValue(JsonElement value)
	{
		return value.ValueKind switch
		{
			JsonValueKind.Null => null,
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.String => value.GetString(),

			JsonValueKind.Number
				when value.TryGetInt32(out int intValue)
				=> intValue,

			JsonValueKind.Number
				when value.TryGetInt64(out long longValue)
				=> longValue,

			JsonValueKind.Number
				=> value.GetDouble(),

			_ => null
		};
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	public sealed class DataStoreQuotaException(string message)
		: Exception(message);

	[Obsolete("Use DataStoreQuotaException instead.")]
	public sealed class PTDatastoreQuotaException(string message)
		: Exception(message);
}

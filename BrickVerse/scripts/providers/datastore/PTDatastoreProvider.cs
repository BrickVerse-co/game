// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Client.WebAPI;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BrickVerse.Providers.Datastore;

public class PTDatastoreProvider : IDatastoreProvider
{
	private const int MaxReadRequestsPerMinute = 30;
	private const int ReadRequestsPerPlayerModifier = 10;
	private const int MaxWriteRequestsPerMinute = 30;
	private const int WriteRequestsPerPlayerModifier = 10;

	private string _dsKey = "";
	private readonly PTHttpClient _client = new();
	private readonly Dictionary<string, DatastoreEntry> _data = [];
	private static int _readRequestsThisMinute = 0, _writeRequestThisMinute = 0, _currentMinute = 0;
	private Datamodel.Data.Datastore _ds = null!;

	public void Connect(string key, Datamodel.Data.Datastore ds)
	{
		_dsKey = key;
		_ds = ds;
		_client.DefaultRequestHeaders["Authorization"] = PolyServerAPI.GetAuthorizationHeaderValue();
	}

	public bool UseReadRequest()
	{
		if (_currentMinute != DateTime.Now.Minute)
		{
			_currentMinute = DateTime.Now.Minute;
			_readRequestsThisMinute = 0;
		}

		if (_readRequestsThisMinute >= MaxReadRequestsPerMinute + (ReadRequestsPerPlayerModifier * _ds.DatastoreService.Root.Players.PlayersCount))
		{
			return false;
		}
		else
		{
			_readRequestsThisMinute++;
			return true;
		}
	}

	public bool UseWriteRequest()
	{
		if (_currentMinute != DateTime.Now.Minute)
		{
			_currentMinute = DateTime.Now.Minute;
			_writeRequestThisMinute = 0;
		}

		if (_writeRequestThisMinute >= MaxWriteRequestsPerMinute + (WriteRequestsPerPlayerModifier * _ds.DatastoreService.Root.Players.PlayersCount))
		{
			return false;
		}
		else
		{
			_writeRequestThisMinute++;
			return true;
		}
	}


	public async Task<object?> ReadData(string key)
	{
		if (!UseReadRequest()) throw new PTDatastoreQuotaException("Read quota exceeded");

		string storeName = Uri.EscapeDataString(_dsKey);
		string escapedKey = Uri.EscapeDataString(key);
		using var req = await _client.GetAsync(Globals.ApiEndpoint.PathJoin($"/v3/world/server/storage?storeName={storeName}&key={escapedKey}"));
		if (!req.IsSuccessStatusCode)
		{
			return null;
		}

		using JsonDocument document = JsonDocument.Parse(await req.Content.ReadAsStringAsync());
		if (!document.RootElement.TryGetProperty("value", out JsonElement valueRoot))
		{
			return null;
		}

		JsonElement value = valueRoot;
		if (valueRoot.ValueKind == JsonValueKind.Object && valueRoot.TryGetProperty("value", out JsonElement wrappedValue))
		{
			value = wrappedValue;
		}

		return ParsePrimitiveJsonValue(value);
	}

	public async Task WriteData(string key, object? value)
	{
		if (value != null && !CheckSupportedType(value))
		{
			throw new InvalidOperationException("Invalid value type");
		}
		if (!UseWriteRequest()) throw new PTDatastoreQuotaException("Write quota exceeded");

		JsonNode? valueNode = value switch
		{
			null => null,
			string stringValue => JsonValue.Create(stringValue),
			bool boolValue => JsonValue.Create(boolValue),
			int intValue => JsonValue.Create(intValue),
			float floatValue => JsonValue.Create(floatValue),
			double doubleValue => JsonValue.Create(doubleValue),
			_ => null,
		};

		JsonObject requestBody =
		[
			new("storeName", _dsKey),
			new("key", key),
			new("value", new JsonObject { ["value"] = valueNode }),
		];

		string jsonBody = requestBody.ToJsonString();
		using var req = await _client.PutAsync(
			Globals.ApiEndpoint.PathJoin("/v3/world/server/storage"),
			new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
		);
	}

	private static object? ParsePrimitiveJsonValue(JsonElement value)
	{
		switch (value.ValueKind)
		{
			case JsonValueKind.True:
			case JsonValueKind.False:
				return value.GetBoolean();
			case JsonValueKind.String:
				return value.GetString() ?? "";
			case JsonValueKind.Number:
				return value.GetDouble();
			default:
				return null;
		}
	}

	private static bool CheckSupportedType(object obj)
	{
		if (obj == null) return false;
		if (obj is int) return true;
		if (obj is double) return true;
		if (obj is float) return true;
		if (obj is string) return true;
		if (obj is bool) return true;
		return false;
	}


	public void Dispose()
	{
		_data.Clear();
		GC.SuppressFinalize(this);
	}

	private struct DatastoreEntry
	{
		public object Value;
		public float Timestamp;
	}

	public class PTDatastoreQuotaException(string msg) : Exception(msg);
}

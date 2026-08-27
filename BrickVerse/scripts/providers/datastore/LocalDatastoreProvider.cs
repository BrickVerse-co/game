// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using System.Threading.Tasks;
using ObsoleteAttribute = BrickVerse.Attributes.ObsoleteAttribute;

namespace BrickVerse.Providers.Datastore;

public sealed class LocalDatastoreProvider : IDatastoreProvider
{
	private static readonly Dictionary<string, LocalDatastoreProvider> ConnectedStores = new(StringComparer.Ordinal);
	private readonly Dictionary<string, object?> _data =
		new(StringComparer.Ordinal);

	private bool _disposed;
	private string _name = "Default";

	public static IReadOnlyDictionary<string, LocalDatastoreProvider> Stores => ConnectedStores;
	public IReadOnlyDictionary<string, object?> Entries => _data;
	private static string StorageDirectory => ProjectSettings.GlobalizePath("user://creator-datastores");

	public void Connect(
		string dataStoreName,
		Datamodel.Data.Datastore dataStore)
	{
		ThrowIfDisposed();
		_name = string.IsNullOrWhiteSpace(dataStoreName) ? "Default" : dataStoreName;
		ConnectedStores[_name] = this;
		LoadPersisted();
	}

	public Task<object?> GetAsync(string key)
	{
		ThrowIfDisposed();
		ValidateKey(key);

		_data.TryGetValue(key, out object? value);
		return Task.FromResult(value);
	}

	public Task SetAsync(string key, object? value)
	{
		ThrowIfDisposed();
		ValidateKey(key);

		_data[key] = value;
		Persist();
		return Task.CompletedTask;
	}

	public static Dictionary<string, Dictionary<string, object?>> GetPersistedStores()
	{
		Dictionary<string, Dictionary<string, object?>> result = new(StringComparer.Ordinal);
		if (!Directory.Exists(StorageDirectory)) return result;
		foreach (string path in Directory.GetFiles(StorageDirectory, "*.json"))
		{
			string name = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(path));
			result[name] = ReadValues(path);
		}
		foreach ((string name, LocalDatastoreProvider provider) in ConnectedStores) result[name] = new(provider._data);
		return result;
	}

	public static void SetPersistedValue(string storeName, string key, object? value)
	{
		if (ConnectedStores.TryGetValue(storeName, out LocalDatastoreProvider? provider)) { provider._data[key] = value; provider.Persist(); return; }
		Directory.CreateDirectory(StorageDirectory);
		string path = StorePath(storeName); Dictionary<string, object?> values = ReadValues(path); values[key] = value;
		File.WriteAllText(path, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
	}

	public static bool DeletePersistedValue(string storeName, string key)
	{
		if (ConnectedStores.TryGetValue(storeName, out LocalDatastoreProvider? provider))
		{
			bool removed = provider._data.Remove(key);
			if (removed) provider.Persist();
			return removed;
		}
		string path = StorePath(storeName);
		Dictionary<string, object?> values = ReadValues(path);
		if (!values.Remove(key)) return false;
		File.WriteAllText(path, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
		return true;
	}

	private void LoadPersisted() { foreach ((string key, object? value) in ReadValues(StorePath(_name))) _data[key] = value; }
	private void Persist() { Directory.CreateDirectory(StorageDirectory); File.WriteAllText(StorePath(_name), JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true })); }
	private static string StorePath(string name) => Path.Combine(StorageDirectory, Uri.EscapeDataString(name) + ".json");
	private static Dictionary<string, object?> ReadValues(string path)
	{
		Dictionary<string, object?> result = new(StringComparer.Ordinal); if (!File.Exists(path)) return result;
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
		foreach (JsonProperty property in document.RootElement.EnumerateObject()) result[property.Name] = JsonValue(property.Value);
		return result;
	}

	private static object? JsonValue(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.Null => null,
		JsonValueKind.String => value.GetString(),
		JsonValueKind.True => true,
		JsonValueKind.False => false,
		JsonValueKind.Number when value.TryGetInt64(out long number) => number,
		JsonValueKind.Number => value.GetDouble(),
		JsonValueKind.Array => value.EnumerateArray().Select(JsonValue).ToList(),
		JsonValueKind.Object => value.EnumerateObject().ToDictionary(item => item.Name, item => JsonValue(item.Value), StringComparer.Ordinal),
		_ => null,
	};

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

		_data.Clear();
		if (ConnectedStores.TryGetValue(_name, out LocalDatastoreProvider? current) && ReferenceEquals(current, this)) ConnectedStores.Remove(_name);
		_disposed = true;

		GC.SuppressFinalize(this);
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

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}

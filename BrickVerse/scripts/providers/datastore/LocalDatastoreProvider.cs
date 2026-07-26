// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ObsoleteAttribute = BrickVerse.Attributes.ObsoleteAttribute;

namespace BrickVerse.Providers.Datastore;

public sealed class LocalDatastoreProvider : IDatastoreProvider
{
	private readonly Dictionary<string, object?> _data =
		new(StringComparer.Ordinal);

	private bool _disposed;

	public void Connect(
		string dataStoreName,
		Datamodel.Data.Datastore dataStore)
	{
		ThrowIfDisposed();
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
		return Task.CompletedTask;
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

		_data.Clear();
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

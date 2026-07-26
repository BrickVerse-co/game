// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;

namespace BrickVerse.Providers.Datastore;

public interface IDatastoreProvider : IDisposable
{
	void Connect(string dataStoreName, Datamodel.Data.Datastore dataStore);

	/// <summary>
	/// GetAsync.
	/// Returns null if the key does not exist.
	/// </summary>
	Task<object?> GetAsync(string key);

	/// <summary>
	/// SetAsync.
	/// </summary>
	Task SetAsync(string key, object? value);

	// ---------------------------------------------------------------------
	// Compatibility wrappers so existing engine code continues to compile.
	// ---------------------------------------------------------------------

	[ObsoleteAttribute("Use GetAsync instead.")]
	Task<object?> ReadData(string key)
	{
		return GetAsync(key);
	}

	[ObsoleteAttribute("Use SetAsync instead.")]
	Task WriteData(string key, object? value)
	{
		return SetAsync(key, value);
	}
}
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Datamodel.Data;
using BrickVerse.Providers.Datastore;
using System;
using System.Collections.Generic;

namespace BrickVerse.Datamodel.Services;

[Static("Datastore"), ExplorerExclude]
[SaveIgnore]
public sealed partial class DatastoreService : Instance
{
	private const int MaxDataStoreNameLength = 32;

	private readonly Dictionary<string, Datastore> _dataStores =
		new(StringComparer.Ordinal);

	[ScriptMethod]
	public Datastore GetDataStore(string name)
	{
		EnsureServerAccess();
		ValidateDataStoreName(name);

		if (_dataStores.TryGetValue(name, out Datastore? dataStore))
		{
			return dataStore;
		}

		IDatastoreProvider provider = CreateProvider();

		dataStore = new Datastore
		{
			DatastoreService = this
		};

		dataStore.Connect(name, provider);
		_dataStores.Add(name, dataStore);

		return dataStore;
	}

	[System.Obsolete("Use GetDataStore instead.")]
	public Datastore GetDatastore(string key)
	{
		return GetDataStore(key);
	}

	private void EnsureServerAccess()
	{
		if (!Root.Network.IsServer)
		{
			throw new InvalidOperationException(
				"DataStoreService can only be accessed from the server.");
		}
	}

	private static void ValidateDataStoreName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"Datastore name cannot be empty.",
				nameof(name));
		}

		if (name.Length > MaxDataStoreNameLength)
		{
			throw new ArgumentOutOfRangeException(
				nameof(name),
				$"Datastore name must be {MaxDataStoreNameLength} characters or fewer.");
		}
	}

	private IDatastoreProvider CreateProvider()
	{
		return Root.Network.IsProd
			? new BVDatastoreProvider()
			: new LocalDatastoreProvider();
	}
}

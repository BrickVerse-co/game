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
	private readonly Dictionary<string, Datastore> datastores = [];

	[ScriptMethod]
	public Datastore GetDatastore(string key)
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException("Datastore can only be accessed by server");
		if (key.Length > 32)
		{
			throw new System.Exception("Datastore key must be 32 characters or less");
		}
		if (!datastores.TryGetValue(key, out Datastore? ds))
		{
			IDatastoreProvider provider;

			if (Root.Network.IsProd)
			{
				provider = new BVDatastoreProvider();
			}
			else
			{
				provider = new LocalDatastoreProvider();
			}

			ds = new()
			{
				DatastoreService = this
			};
			ds.Connect(key, provider);
			datastores.Add(key, ds);
		}
		return ds;
	}
}

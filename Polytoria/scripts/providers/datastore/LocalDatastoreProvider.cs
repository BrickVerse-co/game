// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Providers.Datastore;

public class LocalDatastoreProvider : IDatastoreProvider
{
	private readonly Dictionary<string, object?> _data = [];

	public void Connect(string key, Datamodel.Data.Datastore ds) { }

	public async Task<object?> ReadData(string key)
	{
		if (_data.TryGetValue(key, out object? val))
		{
			return val;
		}
		else
		{
			return null;
		}
	}

	public async Task WriteData(string key, object? value)
	{
		_data[key] = value;
	}

	public void Dispose()
	{
		_data.Clear();
		GC.SuppressFinalize(this);
	}
}

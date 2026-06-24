// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;

namespace Polytoria.Providers.Datastore;

public interface IDatastoreProvider : IDisposable
{
	void Connect(string key, Datamodel.Data.Datastore ds);
	Task WriteData(string key, object? value);
	Task<object?> ReadData(string key);
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using BrickVerse.Shared.AssetLoaders;

namespace BrickVerse.Providers.AssetLoaders;

public interface IAssetProvider : IDisposable
{
	Task<CacheItem> LoadResource(CacheItem item);
	string GetAssetServeURL(string id, ResourceType itemType);
}

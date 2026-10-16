// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Providers.AssetLoaders;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BrickVerse.Shared.AssetLoaders;

public partial class AssetLoader : Node
{
	private readonly record struct AssetCacheKey(ResourceType Type, string ID, Vector2I? Resize);
	public const int DefaultMaxConcurrentRequests = 8;
	public const int MaximumConcurrentRequests = 16;

	public AssetLoader()
	{
		Singleton = this;
		AssetProvider = new BVAssetProvider();
	}

	public static AssetLoader Singleton { get; private set; } = null!;
	public bool UseAssetLoader { get; set; } = true;

	private long _assetSizeBytes = 0;
	internal long AssetSizeBytes => _assetSizeBytes;
	internal int PendingAssetsCount => _pendingRequests.Count;
	internal int AssetCacheCount => _cache.Count;

	private readonly ConcurrentDictionary<AssetCacheKey, CacheItem> _cache = [];
	private readonly ConcurrentDictionary<AssetCacheKey, Lazy<Task<CacheItem>>> _pendingRequests = [];
	private int _maxConcurrentRequests = DefaultMaxConcurrentRequests;
	public int MaxConcurrentRequests
	{
		get => _maxConcurrentRequests;
		set => _maxConcurrentRequests = Math.Clamp(value, 1, MaximumConcurrentRequests);
	}

	private SemaphoreSlim _loadSlots = null!;
	private readonly object _loadSlotsLock = new();

	public IAssetProvider AssetProvider = null!;

	private static AssetCacheKey KeyFor(CacheItem item)
	{
		return new AssetCacheKey(item.Type, item.ID, item.Resize);
	}

	private async Task<CacheItem> LoadResource(CacheItem item)
	{
		if (string.IsNullOrEmpty(item.ID))
		{
			return item;
		}

		if (!UseAssetLoader)
		{
			return item;
		}

		return await AssetProvider.LoadResource(item);
	}

	public void GetResource(CacheItem item, Action<Resource> callback)
	{
		GetRawCache(item, result =>
		{
			callback(result.Resource);
		});
	}

	private async Task<CacheItem> LoadItem(CacheItem item, AssetCacheKey key)
	{
		SemaphoreSlim loadSlots;
		lock (_loadSlotsLock)
		{
			_loadSlots ??= new(MaxConcurrentRequests);
			loadSlots = _loadSlots;
		}

		await loadSlots.WaitAsync();
		try
		{
			CacheItem result = await LoadResource(item);
			_cache[key] = result;
			Interlocked.Add(ref _assetSizeBytes, result.SizeBytes);
			return result;
		}
		finally
		{
			_pendingRequests.TryRemove(key, out _);
			loadSlots.Release();
		}
	}

	private async Task<CacheItem> LoadItemAndReport(CacheItem item, AssetCacheKey key)
	{
		try
		{
			return await LoadItem(item, key);
		}
		catch (Exception exception)
		{
			Callable.From(() => BV.PrintErr(
				"Failed to load resource (Type: " + item.Type + ", ID: " + item.ID + "): " + exception.Message
			)).CallDeferred();
			throw;
		}
	}

	public void GetRawCache(CacheItem item, Action<CacheItem> callback)
	{
		AssetCacheKey key = KeyFor(item);

		// Return cached asset
		if (_cache.TryGetValue(key, out CacheItem cached))
		{
			Callable.From(() => callback(cached)).CallDeferred();
			return;
		}

		Lazy<Task<CacheItem>> task = _pendingRequests.GetOrAdd(
			key,
			_ => new Lazy<Task<CacheItem>>(
				() => LoadItemAndReport(item, key),
				LazyThreadSafetyMode.ExecutionAndPublication
			)
		);

		_ = WaitForResource(task.Value, item, callback);
	}

	private static async Task WaitForResource(Task<CacheItem> task, CacheItem item, Action<CacheItem> callback)
	{
		try
		{
			CacheItem result = await task;
			Callable.From(() => callback(result)).CallDeferred();
		}
		catch
		{
			// The shared load task reports once, even when several callbacks await it.
		}
	}
}

public enum ResourceType
{
	// Developer Asset Types
	Texture,
	Sound,
	Mesh,
	Prefab,
	Animation,
	Plugin,
	Video,

	UniverseThumbnail,
	GuildIcon,
	GuildBanner,

	UserBodyshot,
	UserHeadshot,

	// Old BV Types
	AssetThumbnail,
	Font
}


public struct CacheItem
{
	public ResourceType Type { get; set; }
	public string ID { get; set; }
	public string DirectURL { get; set; }
	public Vector2I? Resize { get; set; }
	public Resource Resource { get; set; }
	public long SizeBytes { get; set; }

	public override readonly bool Equals(object? obj)
	{
		return obj is CacheItem item && item.Type == Type && item.ID == ID && item.Resize == Resize;
	}

	public override readonly int GetHashCode()
	{
		return HashCode.Combine(Type, ID, Resize);
	}

	public static bool operator ==(CacheItem left, CacheItem right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(CacheItem left, CacheItem right)
	{
		return !(left == right);
	}
}

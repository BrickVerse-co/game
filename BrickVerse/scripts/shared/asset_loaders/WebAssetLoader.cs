// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BrickVerse.Shared.AssetLoaders;

public partial class WebAssetLoader : Node
{
	private const int MaxConcurrentRequests = 6;

	public WebAssetLoader()
	{
		Singleton = this;
	}

	public static WebAssetLoader Singleton { get; private set; } = null!;

	private readonly BVHttpClient _client = new();
	private readonly ConcurrentDictionary<WebCacheItem, WebCacheItem> _cache = [];
	private readonly ConcurrentDictionary<WebCacheItem, Lazy<Task<WebCacheItem>>> _pendingRequests = [];
	private readonly SemaphoreSlim _loadSlots = new(MaxConcurrentRequests);

	private async Task<WebCacheItem> LoadResource(WebCacheItem item)
	{
		if (string.IsNullOrEmpty(item.URL))
		{
			return new WebCacheItem();
		}

		await _loadSlots.WaitAsync();
		try
		{
			byte[] buffer = await _client.GetByteArrayAsync(item.URL);

			switch (item.Type)
			{
				case WebResourceType.Image:
					{
						Image image = ImageBufferDecoder.Decode(buffer, item.URL);
						image.FixAlphaEdges();
						Error mipmapError = image.GenerateMipmaps();
						if (mipmapError != Error.Ok)
							throw new InvalidOperationException($"Could not generate mipmaps ({mipmapError}).");
						item.Resource = ImageTexture.CreateFromImage(image);

						_cache[item] = item;
						return item;
					}
				default:
					throw new NotImplementedException($"Resource type {item.Type} not implemented!");
			}
		}
		finally
		{
			_loadSlots.Release();
		}
	}

	private async Task<WebCacheItem> LoadResourceAndReport(WebCacheItem item)
	{
		try
		{
			return await LoadResource(item);
		}
		catch (Exception exception)
		{
			Callable.From(() => BV.PrintErr(
				"Failed to load brickverse.gg resource (Type: " + item.Type + ", URL: " + item.URL + "): " + exception.Message
			)).CallDeferred();
			throw;
		}
	}

	public void GetResource(WebCacheItem item, Action<Resource> callback)
	{
		_ = GetResourceAsync(item, callback);
	}

	private async Task GetResourceAsync(WebCacheItem item, Action<Resource> callback)
	{
		try
		{
			if (_cache.TryGetValue(item, out WebCacheItem cached))
			{
				Callable.From(() => callback(cached.Resource)).CallDeferred();
				return;
			}

			Lazy<Task<WebCacheItem>> pending = _pendingRequests.GetOrAdd(
				item,
				key => new Lazy<Task<WebCacheItem>>(
					() => LoadResourceAndReport(key),
					LazyThreadSafetyMode.ExecutionAndPublication
				)
			);
			WebCacheItem result = await pending.Value;
			Callable.From(() => callback(result.Resource)).CallDeferred();
		}
		catch
		{
			// The shared load task reports once, even when several callbacks await it.
		}
		finally
		{
			_pendingRequests.TryRemove(item, out _);
		}
	}
}

public enum WebResourceType
{
	Image
}

public struct WebCacheItem
{
	public string URL { get; set; }
	public WebResourceType Type { get; set; }
	public Resource Resource { get; set; }

	public override readonly bool Equals(object? obj)
	{
		return obj is WebCacheItem item && item.Type == Type && item.URL == URL;
	}

	public override readonly int GetHashCode()
	{
		return HashCode.Combine(Type, URL);
	}

	public static bool operator ==(WebCacheItem left, WebCacheItem right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(WebCacheItem left, WebCacheItem right)
	{
		return !(left == right);
	}
}

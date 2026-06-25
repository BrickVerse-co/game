// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Client.WebAPI;
#if CREATOR
using BrickVerse.Creator.Utils;
#endif
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BrickVerse.Providers.AssetLoaders;

public class PTAssetProvider : IAssetProvider
{
	private readonly PTHttpClient _client = new();

	public async Task<CacheItem> LoadResource(CacheItem item)
	{
		ApplyAssetAuthHeaders();

		string url = GetAssetServeURL(item.ID, item.Type);
		byte[] buffer = await GetResourceBuffer(url, item.Type);
		item.SizeBytes = buffer.LongLength;
		item.DirectURL = url;

		switch (item.Type)
		{
			case ResourceType.Mesh:
				{
					GltfDocument document = new();
					GltfState state = new() { CreateAnimations = true };

					document.AppendFromBuffer(buffer, null, state);

					Node3D scene = (Node3D)document.GenerateScene(state);

					// Remove arbitrary nodes that may come with the GLTF (eg. Rigidbodies)
					RemoveNonMeshNodes(scene);

					// Set mipmap texture filter for meshes
					SetMipmapTextureFilter(scene);

					TaskCompletionSource<PackedScene> callback = new();

					Callable.From(() =>
					{
						PackedScene mesh = new();
						mesh.Pack(scene);
						scene.Free();

						callback.SetResult(mesh);
					}).CallDeferred();

					item.Resource = await callback.Task;

					return item;
				}
			case ResourceType.Sound:
				{
					item.Resource = AudioStreamOggVorbis.LoadFromBuffer(buffer);

					return item;
				}
			case ResourceType.Asset:
			case ResourceType.Decal:
			case ResourceType.AssetThumbnail:
			case ResourceType.PlaceThumbnail:
			case ResourceType.PlaceIcon:
			case ResourceType.UserThumbnail:
			case ResourceType.UserHeadshot:
			case ResourceType.GuildThumbnail:
			case ResourceType.GuildBanner:
				{
					Image image = new();
					image.LoadPngFromBuffer(buffer);
					image.GenerateMipmaps();
					image.FixAlphaEdges();

					if (item.Resize != null)
					{
						image.Resize(item.Resize.Value.X, item.Resize.Value.Y, Image.Interpolation.Lanczos);
					}

					item.Resource = ImageTexture.CreateFromImage(image);

					return item;
				}
			default: throw new NotImplementedException();
		}
	}

	public string GetAssetServeURL(string id, ResourceType itemType)
	{
		if (itemType is ResourceType.AssetThumbnail or ResourceType.PlaceThumbnail or ResourceType.PlaceIcon or ResourceType.GuildThumbnail or ResourceType.GuildBanner)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + id);
		}

		if (itemType == ResourceType.UserThumbnail)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/thumbnails/bodyshot/" + id);
		}

		if (itemType == ResourceType.UserHeadshot)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/thumbnails/headshot/" + id);
		}

		// Runtime-specific DRM endpoints:
		// Client build uses world client token, server build uses host token,
		// creator/workshop uses user cookie with regular asset download.
		if (Globals.IsServerBuild)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/world/server/asset/" + id);
		}

#if CREATOR
		return Globals.ApiEndpoint.PathJoin("/v3/asset/" + id + "/download");
#else
		return Globals.ApiEndpoint.PathJoin("/v3/world/client/asset/" + id);
#endif
	}

	private async Task<byte[]> GetResourceBuffer(string url, ResourceType itemType)
	{
		if (itemType is ResourceType.Mesh or ResourceType.Sound or ResourceType.Asset or ResourceType.Decal)
		{
			return await _client.GetByteArrayAsync(url);
		}

		ThumbnailUrlResponse? thumb = await _client.GetFromJsonAsync(url, PTAssetProviderGenerationContext.Default.ThumbnailUrlResponse);
		if (thumb is null || string.IsNullOrWhiteSpace(thumb.Value.Url))
			throw new InvalidOperationException("Failed to resolve thumbnail URL");

		return await _client.GetByteArrayAsync(thumb.Value.Url);
	}

	private void ApplyAssetAuthHeaders()
	{
		_client.DefaultRequestHeaders.Remove("Authorization");
		_client.DefaultRequestHeaders.Remove("Cookie");

		if (Globals.IsServerBuild)
		{
			string serverToken = ServerAPI.GetAuthorizationHeaderValue();
			if (!string.IsNullOrWhiteSpace(serverToken))
			{
				_client.DefaultRequestHeaders["Authorization"] = serverToken;
			}
			return;
		}

#if CREATOR
		if (!string.IsNullOrWhiteSpace(CreatorAPI.Token))
		{
			string cookieToken = CreatorAPI.Token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
				? CreatorAPI.Token[7..]
				: CreatorAPI.Token;
			_client.DefaultRequestHeaders["Cookie"] = "auth_token=" + Uri.EscapeDataString(cookieToken);
		}
#else
		if (!string.IsNullOrWhiteSpace(ClientAuthAPI.Token))
		{
			_client.DefaultRequestHeaders["Authorization"] = BuildBearerToken(ClientAuthAPI.Token);
		}
#endif
	}

	private static string BuildBearerToken(string token)
	{
		if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			return token;
		}

		return "Bearer " + token;
	}

	public void Dispose()
	{
		GC.SuppressFinalize(this);
	}

	private static void RemoveNonMeshNodes(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			RemoveNonMeshNodes(child); // recurse first

			bool isMesh = child is MeshInstance3D;
			bool isSkeleton = child is Skeleton3D;
			bool isExactNode3D = child.GetType() == typeof(Node3D);
			bool isAnimationPlayer = child is AnimationPlayer;
			bool isAnimationTree = child is AnimationTree;

			if (!isMesh && !isSkeleton && !isExactNode3D && !isAnimationPlayer && !isAnimationTree)
			{
				child.Free();
			}
		}
	}

	private static void SetMipmapTextureFilter(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			SetMipmapTextureFilter(child);

			if (child is MeshInstance3D meshInstance)
			{
				for (int s = 0; s < meshInstance.Mesh.GetSurfaceCount(); s++)
				{
					if (meshInstance.GetActiveMaterial(s) is BaseMaterial3D material)
					{
						if (material.AlbedoTexture is ImageTexture albedoTex)
						{
							Image img = albedoTex.GetImage();
							img.GenerateMipmaps();
							material.AlbedoTexture = ImageTexture.CreateFromImage(img);
						}

						material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
					}
				}
			}
		}
	}
}

internal struct ThumbnailUrlResponse
{
	[JsonPropertyName("url")]
	public string Url { get; set; }
}

[JsonSerializable(typeof(ThumbnailUrlResponse))]
internal partial class PTAssetProviderGenerationContext : JsonSerializerContext { }

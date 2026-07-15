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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BrickVerse.Providers.AssetLoaders;

public class BVAssetProvider : IAssetProvider
{
	private readonly BVHttpClient _client = new();

	public async Task<CacheItem> LoadResource(CacheItem item)
	{
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
			case ResourceType.Texture:
			case ResourceType.AssetThumbnail:
			case ResourceType.UniverseThumbnail:
			case ResourceType.UserBodyshot:
			case ResourceType.UserHeadshot:
			case ResourceType.GuildIcon:
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
		if (itemType is ResourceType.UserBodyshot or ResourceType.UserHeadshot)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/thumbnails/" + (itemType is ResourceType.UserBodyshot ? "bodyshot" : "headshot") + "/" + id + "?stream=true");
		}

		if (itemType is ResourceType.GuildIcon or ResourceType.GuildBanner) // or ResourceType.Texture or ResourceType.AssetThumbnail)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + id + "?stream=true");
		}

		// Runtime-specific DRM endpoints (must be runtime mode, not build feature)
		if (BV.IsServer)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/world/server/asset/" + id);
		}

		if (!string.IsNullOrWhiteSpace(CreatorAPI.Token) && string.IsNullOrWhiteSpace(ClientAuthAPI.JoinToken))
		{
			return Globals.ApiEndpoint.PathJoin("/v3/world/editor/asset/" + id);
		}

		return Globals.ApiEndpoint.PathJoin("/v3/world/client/asset/" + id);
	}

	private async Task<byte[]> GetResourceBuffer(string url, ResourceType itemType)
	{
		using HttpRequestMessage request = new(HttpMethod.Get, url);
		request.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
		ApplyAssetAuthHeaders(request);
		//BV.Print("Fetching resource buffer from URL: ", url, " for resource type: ", itemType, " Authorization: ", request.Headers.Authorization);

		using HttpResponseMessage response = await _client.SendAsync(request);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadAsByteArrayAsync();
	}

	private void ApplyAssetAuthHeaders(HttpRequestMessage request)
	{
		string? token = null;

#if CREATOR
		if (!string.IsNullOrWhiteSpace(CreatorAPI.Token))
		{
			token = CreatorAPI.Token;
		}
#endif

		if (string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(ClientAuthAPI.JoinToken))
		{
			token = ClientAuthAPI.JoinToken;
		}

		if (BV.IsServer)
		{
			string serverToken = ServerAPI.GetAuthorizationHeaderValue();

			if (!string.IsNullOrWhiteSpace(serverToken))
			{
				token = serverToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
					? serverToken["Bearer ".Length..].Trim()
					: serverToken.Trim();
			}
		}

		if (!string.IsNullOrWhiteSpace(token))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
		}
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
internal partial class BVAssetProviderGenerationContext : JsonSerializerContext { }

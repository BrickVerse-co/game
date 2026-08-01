// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BrickVerse.Client.WebAPI;
using BrickVerse.Formats;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using Godot;
#if CREATOR
using BrickVerse.Creator.Utils;
#endif


namespace BrickVerse.Providers.AssetLoaders;

public class BVAssetProvider : IAssetProvider
{
	private readonly BVHttpClient _client = new();

	public async Task<CacheItem> LoadResource(CacheItem item)
	{
		string url = GetAssetServeURL(item.ID, item.Type);
		byte[] buffer;
		try
		{
			buffer = await GetResourceBuffer(url, item.Type);
		}
		catch (Exception exception) when (item.Type == ResourceType.Mesh)
		{
			return await UseUnavailableMesh(item, url, exception);
		}
		item.SizeBytes = buffer.LongLength;
		item.DirectURL = url;

		switch (item.Type)
		{
			case ResourceType.Animation:
				{
					item.Resource = BVAnimationFormat.ToLibrary(BVAnimationFormat.Read(buffer));
					return item;
				}
			case ResourceType.Mesh:
				{
					try
					{
						item.Resource = await LoadGlb(buffer, item.ID);
					}
					catch (Exception exception)
					{
						return await UseUnavailableMesh(item, url, exception);
					}

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
						image.Resize(
							item.Resize.Value.X,
							item.Resize.Value.Y,
							Image.Interpolation.Lanczos
						);
					}

					item.Resource = ImageTexture.CreateFromImage(image);

					return item;
				}
			default:
				throw new NotImplementedException();
		}
	}

	private static async Task<CacheItem> UseUnavailableMesh(
		CacheItem item,
		string url,
		Exception exception
	)
	{
		BV.PrintWarn(
			"Mesh asset ",
			item.ID,
			" could not be loaded; using the missing-mesh placeholder. ",
			exception.Message
		);
		item.DirectURL = url;
		item.SizeBytes = 0;
		item.Resource = await CreateUnavailableMesh();
		return item;
	}

	private static Task<PackedScene> CreateUnavailableMesh()
	{
		TaskCompletionSource<PackedScene> completion = new(
			TaskCreationOptions.RunContinuationsAsynchronously
		);

		Callable
			.From(() =>
			{
				Node3D placeholderRoot = new() { Name = "UnavailableMeshRoot" };

				try
				{
					Image checkerImage = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
					Color missingColor = Color.FromHtml("#ff00ff");
					for (int y = 0; y < checkerImage.GetHeight(); y++)
					{
						for (int x = 0; x < checkerImage.GetWidth(); x++)
						{
							checkerImage.SetPixel(
								x,
								y,
								((x / 2) + (y / 2)) % 2 == 0 ? missingColor : Colors.Black
							);
						}
					}

					StandardMaterial3D missingMaterial = new()
					{
						AlbedoTexture = ImageTexture.CreateFromImage(checkerImage),
						TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
						Roughness = 1.0f,
					};
					BoxMesh boxMesh = new() { Size = Vector3.One, Material = missingMaterial };
					MeshInstance3D placeholderMesh = new()
					{
						Name = "UnavailableMesh",
						Mesh = boxMesh,
					};
					placeholderRoot.AddChild(placeholderMesh);
					placeholderMesh.Owner = placeholderRoot;

					PackedScene packedScene = new();
					Error packError = packedScene.Pack(placeholderRoot);
					if (packError != Error.Ok)
					{
						throw new InvalidOperationException(
							$"Could not create unavailable mesh placeholder: {packError}."
						);
					}
					completion.SetResult(packedScene);
				}
				catch (Exception error)
				{
					completion.SetException(error);
				}
				finally
				{
					placeholderRoot.Free();
				}
			})
			.CallDeferred();

		return completion.Task;
	}

	private static Task<PackedScene> LoadGlb(byte[] buffer, string assetId)
	{
		TaskCompletionSource<PackedScene> completion = new(
			TaskCreationOptions.RunContinuationsAsynchronously
		);

		Callable
			.From(() =>
			{
				Node3D? scene = null;

				try
				{
					if (buffer == null || buffer.Length == 0)
					{
						throw new InvalidDataException(
							$"Mesh asset {assetId} contained no glTF data."
						);
					}

					bool isGlb =
						buffer.Length >= 12
						&& buffer[0] == (byte)'g'
						&& buffer[1] == (byte)'l'
						&& buffer[2] == (byte)'T'
						&& buffer[3] == (byte)'F';

					bool isGltfJson = IsGltfJson(buffer);

					if (!isGlb && !isGltfJson)
					{
						throw new InvalidDataException(
							$"Mesh asset {assetId} is not a valid GLB or glTF file."
						);
					}

					GltfDocument document = new();

					GltfState state = new()
					{
						CreateAnimations = true,

						// Empty is correct for GLB files and self-contained glTF files.
						// External .bin or image references require a real directory.
						BasePath = string.Empty,
					};

					Error importError = document.AppendFromBuffer(buffer, state.BasePath, state);

					if (importError != Error.Ok)
					{
						throw new InvalidDataException(
							$"Godot could not import mesh asset {assetId}: {importError}."
						);
					}

					scene =
						document.GenerateScene(state) as Node3D
						?? throw new InvalidDataException(
							$"Mesh asset {assetId} did not contain a 3D scene."
						);

					/*
					 * Do not strip all non-mesh nodes here.
					 *
					 * Skeleton3D, BoneAttachment3D, AnimationPlayer and ordinary
					 * Node3D hierarchy nodes may be necessary for the mesh to render
					 * or animate correctly.
					 *
					 * Only remove specific unwanted node types, such as imported
					 * cameras and lights.
					 */
					RemoveImportedCamerasAndLights(scene);

					// Applies filtering without replacing the imported PBR materials.
					SetMipmapTextureFilter(scene);

					// Artifically scale up the mesh to match the expected size of a BrickVerse avatar.
					// this is a temporary solution that end's up being permanent..
					scene.Scale = Vector3.One * BrickVerse.Datamodel.Mesh.ImportedAssetScale;

					PackedScene packedScene = new();
					Error packError = packedScene.Pack(scene);

					if (packError != Error.Ok)
					{
						throw new InvalidDataException(
							$"Could not pack mesh asset {assetId}: {packError}."
						);
					}

					completion.TrySetResult(packedScene);
				}
				catch (Exception exception)
				{
					completion.TrySetException(exception);
				}
				finally
				{
					scene?.Free();
				}
			})
			.CallDeferred();

		return completion.Task;
	}

	private static bool IsGltfJson(byte[] buffer)
	{
		int index = 0;

		// Skip UTF-8 BOM.
		if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
		{
			index = 3;
		}

		// Skip leading JSON whitespace.
		while (index < buffer.Length)
		{
			byte value = buffer[index];

			if (
				value != (byte)' '
				&& value != (byte)'\t'
				&& value != (byte)'\r'
				&& value != (byte)'\n'
			)
			{
				break;
			}

			index++;
		}

		return index < buffer.Length && buffer[index] == (byte)'{';
	}

	private static void RemoveImportedCamerasAndLights(Node node)
	{
		for (int index = node.GetChildCount() - 1; index >= 0; index--)
		{
			Node child = node.GetChild(index);

			if (child is Camera3D or Light3D)
			{
				node.RemoveChild(child);
				child.Free();
				continue;
			}

			RemoveImportedCamerasAndLights(child);
		}
	}

	public string GetAssetServeURL(string id, ResourceType itemType)
	{
		if (itemType is ResourceType.UserBodyshot or ResourceType.UserHeadshot)
		{
			return Globals.ApiEndpoint.PathJoin(
				"/v3/thumbnails/"
					+ (itemType is ResourceType.UserBodyshot ? "bodyshot" : "headshot")
					+ "/"
					+ id
					+ "?stream=true"
			);
		}

		if (itemType is ResourceType.GuildIcon or ResourceType.GuildBanner) // or ResourceType.Texture or ResourceType.AssetThumbnail)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + id + "?stream=true");
		}

		// Check if we are in creator studio
#if CREATOR
		if (
			!string.IsNullOrWhiteSpace(CreatorAPI.Token)
			&& string.IsNullOrWhiteSpace(ClientAuthAPI.JoinToken)
		)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/world/editor/asset/" + id);
		}
#endif

		// Server launches populate both ClientAuthAPI.JoinToken and
		// ServerAPI.HostToken from -token. Prefer the authoritative host token;
		// BV.IsServer may not yet reflect the initialized NetworkService here.
		if (
			BrickVerse.Datamodel.World.Current?.Network?.IsServer == true
			|| BV.IsServer
			|| !string.IsNullOrWhiteSpace(ServerAPI.HostToken)
		)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/world/server/asset/" + id);
		}

		// An authenticated play-test client must use its world join token. The
		// Creator OAuth token is only for the editor/local server loading source assets.
		if (!BV.IsServer && !string.IsNullOrWhiteSpace(ClientAuthAPI.JoinToken))
		{
			return Globals.ApiEndpoint.PathJoin("/v3/world/client/asset/" + id);
		}

		// Runtime-specific DRM endpoints (must be runtime mode, not build feature)
		if (BV.IsServer)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/world/server/asset/" + id);
		}

		// Check if we are in creator studio if we have a creator token, use the editor endpoint for asset loading
		if (!string.IsNullOrWhiteSpace(ClientAuthAPI.CreatorToken))
		{
			return Globals.ApiEndpoint.PathJoin("/v3/world/editor/asset/" + id);
		}

		// Fallback to client asset endpoint for regular clients (prod/non-creator)
		return Globals.ApiEndpoint.PathJoin("/v3/world/client/asset/" + id);
	}

	private async Task<byte[]> GetResourceBuffer(string url, ResourceType itemType)
	{
		bool requiresAuthorization = url.Contains("/v3/world/", StringComparison.OrdinalIgnoreCase);

		if (requiresAuthorization)
		{
			await WaitForAssetAuthorizationAsync(url);
		}

		for (int attempt = 0; attempt < 2; attempt++)
		{
			using HttpRequestMessage request = CreateAssetRequest(url);

			// BV.Print(
			// 	"Fetching resource buffer from URL: ", url,
			// 	" for resource type: ", itemType,
			// 	" Authorization: ", request.Headers.Authorization);

			HttpResponseMessage response;
			try
			{
				response = await _client.SendAsync(request);
			}
			catch (Exception exception)
			{
				throw CreateAssetRequestException(request, null, exception);
			}

			using (response)
			{
				if (
					response.StatusCode == HttpStatusCode.Unauthorized
					&& attempt == 0
					&& requiresAuthorization
				)
				{
					await WaitForAssetAuthorizationAsync(url);
					continue;
				}

				if (!response.IsSuccessStatusCode)
					throw CreateAssetRequestException(request, response.StatusCode);

				return await response.Content.ReadAsByteArrayAsync();
			}
		}

		throw new HttpRequestException("Asset request failed after authorization retry.");
	}

	private static HttpRequestException CreateAssetRequestException(
		HttpRequestMessage request,
		HttpStatusCode? statusCode,
		Exception? innerException = null
	)
	{
		string path = request.RequestUri?.AbsolutePath ?? "";
		string route =
			path.Contains("/world/editor/", StringComparison.OrdinalIgnoreCase) ? "Editor"
			: path.Contains("/world/client/", StringComparison.OrdinalIgnoreCase) ? "Client"
			: path.Contains("/world/server/", StringComparison.OrdinalIgnoreCase) ? "Server"
			: "Public";
		string? bearer = request.Headers.Authorization?.Parameter;
		string bearerPrefix = string.IsNullOrWhiteSpace(bearer)
			? "<missing>"
			: bearer[..Math.Min(6, bearer.Length)] + "…";
		string status = statusCode.HasValue
			? $"{(int)statusCode.Value} ({statusCode.Value})"
			: "request failed before a response";

		return new HttpRequestException(
			$"Asset request failed. Route: {route}; Bearer prefix: {bearerPrefix}; Status: {status}.",
			innerException,
			statusCode
		);
	}

	private HttpRequestMessage CreateAssetRequest(string url)
	{
		HttpRequestMessage request = new(HttpMethod.Get, url);
		request.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
		ApplyAssetAuthHeaders(request);
		return request;
	}

	private static async Task WaitForAssetAuthorizationAsync(string url)
	{
		const int maxAttempts = 30;
		const int delayMilliseconds = 50;

		for (int attempt = 0; attempt < maxAttempts; attempt++)
		{
			if (!string.IsNullOrWhiteSpace(GetAssetAuthorizationToken(url)))
			{
				return;
			}

			await Task.Delay(delayMilliseconds);
		}
	}

	private static void ApplyAssetAuthHeaders(HttpRequestMessage request)
	{
		string? token = GetAssetAuthorizationToken(request.RequestUri?.AbsolutePath);

		if (!string.IsNullOrWhiteSpace(token))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}
	}

	private static string? GetAssetAuthorizationToken(string? requestPath = null)
	{
		if (requestPath?.Contains("/world/client/", StringComparison.OrdinalIgnoreCase) == true)
		{
			return NormalizeBearerToken(ClientAuthAPI.JoinToken);
		}

		if (requestPath?.Contains("/world/server/", StringComparison.OrdinalIgnoreCase) == true)
		{
			return NormalizeBearerToken(
				!string.IsNullOrWhiteSpace(ServerAPI.HostToken)
					? ServerAPI.HostToken
					: ClientAuthAPI.JoinToken
			);
		}

		// Creator play-test servers and clients use the OAuth token passed by
		// Creator. Check it before BV.IsServer, which otherwise has no
		// production host token in a local test.
		if (!string.IsNullOrWhiteSpace(ClientAuthAPI.CreatorToken))
		{
			return NormalizeBearerToken(ClientAuthAPI.CreatorToken);
		}

		if (BV.IsServer)
		{
			return NormalizeBearerToken(ServerAPI.HostToken);
		}

		// Fallback to JoinToken for regular clients (prod/non-creator)
		if (!string.IsNullOrWhiteSpace(ClientAuthAPI.JoinToken))
		{
			return NormalizeBearerToken(ClientAuthAPI.JoinToken);
		}

#if CREATOR
		if (!string.IsNullOrWhiteSpace(CreatorAPI.Token))
		{
			return NormalizeBearerToken(CreatorAPI.Token);
		}
#endif

		return null;
	}

	private static string? NormalizeBearerToken(string? token)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return null;
		}

		string normalized = token.Trim();

		return normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
			? normalized["Bearer ".Length..].Trim()
			: normalized;
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

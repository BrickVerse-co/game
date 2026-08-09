// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using BrickVerse.Attributes;
using BrickVerse.Client.WebAPI;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Schemas.API;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using BrickVerse.Utils;
using Godot;
#if CREATOR
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel.Creator;
#endif

namespace BrickVerse.Datamodel.Services;

[Static("Insert"), ExplorerExclude, SaveIgnore]
public sealed partial class InsertService : Instance
{
	// CharacterPreview.tsx centers ClassicPose.obj using these vertical bounds.
	private const float MarketplaceCharacterBoundsMinY = -0.4980669915676117f;
	private const float MarketplaceCharacterBoundsMaxY = 0.421999990940094f;
	private const float MarketplaceRigOffsetY = 0.5f;

	private readonly BVHttpClient _httpClient = new();
	private static readonly Dictionary<string, APIStoreItem> _storeItemCache = [];

	[ScriptMethod, Attributes.Obsolete("Use ModelAsync instead")]
	public void Model(string id, BVCallback? callback = null)
	{
		_ = ModelAsync(id)
			.ContinueWith(tsk =>
			{
				if (tsk.IsCompletedSuccessfully)
				{
					callback?.Invoke(tsk.Result);
				}
			});
	}

	[ScriptMethod]
	public NPC DefaultNPC()
	{
		var npc = New<NPC>();
		InitializeDefaultNPC(npc);
		return npc;
	}

	[ScriptMethod]
	public void InitializeDefaultNPC(NPC npc)
	{
		int owner = npc.NetworkAuthority;

		// Default character
		var ptm = DefaultCharacter();
		npc.Character = ptm;
		ptm.Name = "Character";
		ptm.Parent = npc;
		ptm.LocalPosition = Vector3.Zero;
		ptm.LocalRotation = Vector3.Zero;
		ptm.LocalSize = Vector3.One;
		ptm.SetNetworkAuthority(npc.NetworkAuthority, false);
		ptm.Animator?.SetNetworkAuthority(owner, false);

		// Jump sound
		BuiltInAudioAsset audio = New<BuiltInAudioAsset>();
		audio.AudioPreset = BuiltInAudioAsset.BuiltInAudioPresetEnum.Jump;
		var jumpSound = New<Sound>();
		jumpSound.Name = "JumpSound";
		jumpSound.Parent = npc;
		jumpSound.Volume = 0.5f;
		jumpSound.Audio = audio;
		jumpSound.Autoplay = false;
		jumpSound.Loop = false;
		jumpSound.PlayInWorld = true;
		jumpSound.SetNetworkAuthority(owner, false);

		npc.JumpSound = jumpSound;

		jumpSound.LocalPosition = Vector3.Zero;
		jumpSound.LocalRotation = Vector3.Zero;
		jumpSound.LocalSize = Vector3.One;
	}

	[ScriptMethod]
	public BrickversianModel DefaultCharacter()
	{
		var ptm = New<BrickversianModel>();
		var animator = New<Animator>();
		animator.AutoInit = false;
		animator.Name = "Animator";
		animator.Parent = ptm;
		ptm.Animator = animator;

		return ptm;
	}

	[ScriptMethod]
	public async Task<Instance?> ModelAsync(string id)
	{
		ApplyAssetAuthHeaders();
		using HttpResponseMessage msg = await _httpClient.GetAsync(GetModelDownloadUrl(id));
		byte[] modelBytes = await msg.Content.ReadAsByteArrayAsync();
		Instance? model = await DatamodelLoader.LoadModelBytes(
			Root,
			modelBytes,
			Root.TemporaryContainer
		);
		return model;
	}

#if CREATOR
	public async Task<Instance?> CreatorImportWebModel(string id, string? optionalName = null)
	{
		ApplyAssetAuthHeaders();
		using HttpResponseMessage msg = await _httpClient.GetAsync(GetModelDownloadUrl(id));
		msg.EnsureSuccessStatusCode();
		byte[] modelBytes = await msg.Content.ReadAsByteArrayAsync();

		if (optionalName != null)
		{
			string importFolderName = await DatamodelLoader.GetImportFolderName(modelBytes);

			if (
				Root.LinkedSession.FileExists(
					Globals.ToolboxFolderName + "/" + importFolderName + "/"
				)
			)
			{
				if (
					!await CreatorService.Interface.PromptConfirmation(
						importFolderName + " already exists, do you want to update it?"
					)
				)
					return null;
			}
		}

		Instance? model = await DatamodelLoader.LoadModelBytes(
			Root,
			modelBytes,
			Root.TemporaryContainer,
			optionalName
		);
		if (model == null)
		{
			BV.PrintErr($"Toolbox prefab {id} could not be imported.");
			return null;
		}

		// PackedFormat writes embedded FileLinkAsset contents into toolbox/<model>/
		// before loading the tree. Rescan now so a clean project immediately owns
		// the scripts and subsequent saves/publishes retain them.
		Root.LinkedSession?.RescanFolder();
		return model;
	}
#endif

	[ScriptMethod]
	public async Task<Accessory?> AccessoryAsync(string id)
	{
		APIMarketplace3DResponse response = await BVAPI.GetMarketplace3D(id);
		if (!response.Success)
			return null;
		APIMarketplace3DItem item = response.Item;
		return CreateAccessory(
			id,
			item.MeshId,
			item.TextureId,
			item.Type,
			item.Name,
			item.MeshPosition
		);
	}

	internal Accessory? CreateAccessory(
		string marketplaceId,
		string? meshId,
		string? textureId,
		string? accessoryType,
		string? name,
		APIPosition3? meshPosition
	)
	{
		if (string.IsNullOrWhiteSpace(meshId))
			return null;

		BVMeshAsset meshAsset = New<BVMeshAsset>();
		meshAsset.AssetID = meshId;

		Accessory accessory = New<Accessory>(this);
		Mesh mesh = New<Mesh>();

		Vector3 size = Vector3.One;
		mesh.Size = size;
		mesh.Parent = accessory;
		mesh.Asset = meshAsset;

		if (!string.IsNullOrWhiteSpace(textureId))
		{
			BVImageAsset texture = New<BVImageAsset>();
			texture.ImageID = textureId;
			mesh.Texture = texture;
		}

		accessory.LocalRotation = Vector3.Zero;
		mesh.LocalRotation = Vector3.Zero;
		accessory.Size = Vector3.One;

		// CharacterPreview.tsx retains the GLB's authored origin: its position.set()
		// replaces the earlier centering adjustment. Match that behavior here.
		mesh.IncludeOffset = true;
		mesh.Name = "Mesh";
		mesh.CanCollide = false;
		mesh.Anchored = true;
		accessory.Name = string.IsNullOrWhiteSpace(name) ? $"Accessory_{marketplaceId}" : name;
		accessory.TargetAttachment = GetAccessoryAttachment(accessoryType);

		// Convert the legacy marketplace coordinates to the engine mesh scale
		APIPosition3 position = meshPosition ?? new APIPosition3();
		float frontendCharacterCenterY =
			(MarketplaceCharacterBoundsMinY + MarketplaceCharacterBoundsMaxY) * 0.5f;
		mesh.LocalPosition =
			new Vector3(position.X, position.Y - frontendCharacterCenterY, position.Z)
				* Mesh.ImportedAssetScale
			+ Vector3.Up * MarketplaceRigOffsetY;

		mesh.LocalPosition += Vector3.Up * 0.17f;

		return accessory;
	}

	private static BrickversianModel.CharacterAttachmentEnum GetAccessoryAttachment(string? accessoryType)
	{
		string normalized = (accessoryType ?? "")
			.Replace("_", "", StringComparison.Ordinal)
			.Replace("-", "", StringComparison.Ordinal)
			.Replace(" ", "", StringComparison.Ordinal)
			.ToLowerInvariant();

		return normalized switch
		{
			"neckaccessory" or "frontaccessory" or "backaccessory" or "shoulderaccessory" =>
				BrickversianModel.CharacterAttachmentEnum.UpperTorso,
			"waistaccessory" => BrickversianModel.CharacterAttachmentEnum.LowerTorso,
			_ => BrickversianModel.CharacterAttachmentEnum.Head,
		};
	}

	[ScriptMethod]
	public async Task<Tool?> ToolAsync(string id)
	{
		APIStoreItem storeItem = await GetStoreItemCachedAsync(id);

		BVMeshAsset meshAsset = New<BVMeshAsset>();
		meshAsset.AssetID = id;

		BVImageAsset icon = New<BVImageAsset>();
		icon.ImageID = id;
		icon.ImageType = ImageTypeEnum.AssetThumbnail;

		Tool tool = New<Tool>(this);
		Mesh mesh = New<Mesh>()!;
		mesh.Size = Vector3.One;
		mesh.Parent = tool;
		mesh.Asset = meshAsset;

		tool.Droppable = false;
		tool.IconImage = icon;

		tool.LocalRotation = Vector3.Zero;
		mesh.LocalRotation = Vector3.Zero;
		tool.Size = new Vector3(0.5f, 0.5f, 0.5f);

		mesh.IncludeOffset = true;
		mesh.Name = "Mesh";
		mesh.CanCollide = false;
		mesh.Anchored = true;
		tool.Name = string.IsNullOrWhiteSpace(storeItem.Name) ? $"Tool_{id}" : storeItem.Name;

		mesh.LocalPosition = new Vector3(1f, -7f, -3f);

		return tool;
	}

	private static async Task<APIStoreItem> GetStoreItemCachedAsync(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
			return CreateFallbackStoreItem(id, "Unknown Asset");

		if (_storeItemCache.TryGetValue(id, out var cached))
			return cached;

		APIStoreItem storeItem;
		try
		{
			storeItem = await BVAPI.GetStoreItem(id);
		}
		catch (HttpRequestException ex) when (IsRecoverableStoreLookupError(ex))
		{
			storeItem = CreateFallbackStoreItem(id, $"Asset_{id}");
			BV.PrintErr(
				$"Store metadata unavailable for asset {id} ({ex.StatusCode?.ToString() ?? "UnknownStatus"}). Using fallback metadata."
			);
		}

		_storeItemCache[id] = storeItem;
		return storeItem;
	}

	private static bool IsRecoverableStoreLookupError(HttpRequestException ex)
	{
		return ex.StatusCode == System.Net.HttpStatusCode.BadRequest
			|| ex.StatusCode == System.Net.HttpStatusCode.Unauthorized
			|| ex.StatusCode == System.Net.HttpStatusCode.Forbidden
			|| ex.StatusCode == System.Net.HttpStatusCode.NotFound;
	}

	private static APIStoreItem CreateFallbackStoreItem(string? id, string fallbackName)
	{
		return new APIStoreItem
		{
			Id = string.IsNullOrWhiteSpace(id) ? "0" : id,
			Type = "",
			AccessoryType = null,
			Name = fallbackName,
			Description = "",
			Tags = [],
			Creator = new APIStoreItemCreator
			{
				Type = "",
				Id = 0,
				Name = "",
				Thumbnail = "",
			},
			Thumbnail = "",
			Version = 0,
			Sales = null,
			Price = null,
			Favorites = null,
			IsLimited = false,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = null,
		};
	}

	private string GetModelDownloadUrl(string id)
	{
		if (Globals.IsServerBuild)
		{
			return Globals.ApiEndpoint.PathJoin("/v3/world/server/asset/" + id);
		}

#if CREATOR
		return Globals.ApiEndpoint.PathJoin("/v3/world/editor/asset/" + id);
#else
		return Globals.ApiEndpoint.PathJoin("/v3/world/client/asset/" + id);
#endif
	}

	private void ApplyAssetAuthHeaders()
	{
		_httpClient.DefaultRequestHeaders.Remove("Authorization");
		_httpClient.DefaultRequestHeaders.Remove("Cookie");

		if (Globals.IsServerBuild)
		{
			if (!string.IsNullOrWhiteSpace(ServerAPI.HostToken))
			{
				_httpClient.DefaultRequestHeaders["Authorization"] = BuildBearerToken(
					ServerAPI.HostToken
				);
			}
			return;
		}

#if CREATOR
		if (!string.IsNullOrWhiteSpace(CreatorAPI.Token))
		{
			_httpClient.DefaultRequestHeaders["Cookie"] =
				"auth_token=" + Uri.EscapeDataString(CreatorAPI.Token);
		}
#else
		if (!string.IsNullOrWhiteSpace(ClientAuthAPI.JoinToken))
		{
			_httpClient.DefaultRequestHeaders["Authorization"] = BuildBearerToken(
				ClientAuthAPI.JoinToken
			);
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
}

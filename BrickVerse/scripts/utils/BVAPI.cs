// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Providers.AssetLoaders;
using BrickVerse.Shared.AssetLoaders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text;

namespace BrickVerse.Utils;

public static class BVAPI
{
	private static readonly BVHttpClient _client = new();

	public static void SetAuthToken(string token)
	{
		_client.DefaultRequestHeaders.Remove("Authorization");
		_client.DefaultRequestHeaders.Remove("Cookie");

		if (string.IsNullOrWhiteSpace(token))
			return;

		_client.DefaultRequestHeaders["Authorization"] =
			$"Bearer {token}";

		_client.DefaultRequestHeaders["Cookie"] =
			$"auth_token={token}";
	}

	public static async Task<JsonDocument> GetJson(string apiPath)
	{
		using HttpResponseMessage response = await _client.GetAsync(Globals.ApiEndpoint.PathJoin(apiPath));
		response.EnsureSuccessStatusCode();
		return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
	}

	public static async Task<JsonDocument> SendJson(HttpMethod method, string apiPath, string json = "{}")
	{
		using HttpRequestMessage request = new(method, Globals.ApiEndpoint.PathJoin(apiPath))
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
		using HttpResponseMessage response = await _client.SendAsync(request);
		response.EnsureSuccessStatusCode();
		string body = await response.Content.ReadAsStringAsync();
		return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
	}

	public static async Task<string> ResolveThumbnailUrl(string type, string id)
	{
		if (string.IsNullOrWhiteSpace(id)) return "";
		using JsonDocument response = await GetJson($"/v3/thumbnails/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(id)}");
		return response.RootElement.TryGetProperty("url", out JsonElement url) && url.ValueKind == JsonValueKind.String
			? url.GetString() ?? "" : "";
	}

	public static async Task<string> GetUniverseThumbnailUrl(long universeId)
	{
		using JsonDocument response = await GetJson($"/v3/universe/{universeId}/thumbnails");
		if (!response.RootElement.TryGetProperty("thumbnails", out JsonElement thumbnails) || thumbnails.ValueKind != JsonValueKind.Array) return "";
		JsonElement? selected = null;
		foreach (JsonElement thumbnail in thumbnails.EnumerateArray())
		{
			if (!thumbnail.TryGetProperty("thumbnailId", out JsonElement id) || id.ValueKind == JsonValueKind.Null) continue;
			selected ??= thumbnail;
			if (thumbnail.TryGetProperty("primary", out JsonElement primary) && primary.ValueKind == JsonValueKind.True) { selected = thumbnail; break; }
		}
		if (!selected.HasValue || !selected.Value.TryGetProperty("thumbnailId", out JsonElement selectedId)) return "";
		return await ResolveThumbnailUrl("ASSET", selectedId.ToString());
	}

	private static string NormalizeToken(string token)
	{
		if (string.IsNullOrWhiteSpace(token)) return "";
		string normalized = token.Trim();
		return normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
			? normalized["Bearer ".Length..].Trim()
			: normalized;
	}

	public static Task<APIUserInfo> GetUserFromID(string userID)
		=> GetUserProfileFromID(userID);

	private static async Task<APIUserInfo> GetUserProfileFromID(string userID)
	{
		APIV3UserProfileRoot response = await _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v3/profile/" + userID + "/id"),
			APIGenerationContext.Default.APIV3UserProfileRoot
		);

		APIV3UserProfileUser user = response.User;
		APIV3UserProfileStatistics stats = user.Statistics;

		return new APIUserInfo
		{
			Id = user.Id ?? userID,
			Username = user.Username ?? "",
			Description = user.Description ?? "",
			MembershipType = user.Status ?? "",
			PlaceVisits = stats.Visits,
			ProfileViews = stats.ProfileViews,
			ForumPosts = stats.ForumPosts,
			RegisteredAt = user.CreatedAt,
			LastSeenAt = user.LastSeenAt,
			Signature = "",
			Thumbnail = new APIUserThumbnail(),
			NetWorth = 0,
			AssetSales = 0,
			IsStaff = false,
			UserRoleClass = "",
		};
	}

	public static async Task<APIV3AuthMeUser> GetCurrentUser()
	{
		APIV3AuthMeRoot response = await _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v3/auth/me"),
			APIGenerationContext.Default.APIV3AuthMeRoot
		);

		if (!response.Success || string.IsNullOrWhiteSpace(response.User.Id))
		{
			throw new Exception("Authentication failed: invalid /v3/auth/me response.");
		}

		return response.User;
	}

	public static async Task<APIJoinPlaceResponse> RequestJoinGame(APIJoinPlaceRequest req, Action<string, string>? onStatus = null)
	{
		onStatus?.Invoke("Preparing world", "Checking the experience and your access…");
		APIV3WorldRoot worldInfo = await GetWorldRootFromID(req.PlaceID);
		onStatus?.Invoke("Finding a server", "Looking for an available server near you…");
		string requestJson = $"{{\"universeId\":{JsonSerializer.Serialize(worldInfo.Universe.Id)},\"worldId\":{JsonSerializer.Serialize(worldInfo.World.Id)},\"platform\":{JsonSerializer.Serialize(Globals.IsMobileBuild ? "MOBILE" : "PC")}}}";
		using JsonDocument queued = await SendJson(HttpMethod.Post, "/v3/world/join", requestJson);
		onStatus?.Invoke("Joining world", queued.RootElement.TryGetProperty("message", out JsonElement queuedMessage) ? queuedMessage.GetString() ?? "Your join request is queued." : "Your join request is queued.");
		if (queued.RootElement.TryGetProperty("joinToken", out JsonElement immediateToken) && immediateToken.ValueKind == JsonValueKind.String)
			return new APIJoinPlaceResponse { Success = true, Token = immediateToken.GetString() ?? "" };
		if (!queued.RootElement.TryGetProperty("requestId", out JsonElement requestNode) || requestNode.ValueKind != JsonValueKind.String)
			throw new Exception(queued.RootElement.TryGetProperty("message", out JsonElement message) ? message.GetString() : "The server did not create a join request.");

		string requestId = requestNode.GetString()!;
		DateTime deadline = DateTime.UtcNow.AddMinutes(8);
		while (DateTime.UtcNow < deadline)
		{
			await Task.Delay(1000);
			using JsonDocument status = await GetJson("/v3/world/join/" + Uri.EscapeDataString(requestId));
			string state = status.RootElement.TryGetProperty("status", out JsonElement stateNode) ? stateNode.GetString() ?? "" : "";
			string detail = status.RootElement.TryGetProperty("message", out JsonElement statusMessage) ? statusMessage.GetString() ?? "" : "";
			onStatus?.Invoke(StatusTitle(state), string.IsNullOrWhiteSpace(detail) ? StatusDetail(state) : detail);
			if (state == "READY" && status.RootElement.TryGetProperty("joinToken", out JsonElement token) && token.ValueKind == JsonValueKind.String)
				return new APIJoinPlaceResponse { Success = true, Token = token.GetString() ?? "" };
			if (state == "FAILED" || (status.RootElement.TryGetProperty("success", out JsonElement success) && success.ValueKind == JsonValueKind.False))
				throw new Exception(status.RootElement.TryGetProperty("message", out JsonElement failure) ? failure.GetString() : "Unable to join this world.");
		}
		try { using JsonDocument _ = await SendJson(HttpMethod.Delete, "/v3/world/join/" + Uri.EscapeDataString(requestId)); } catch { }
		throw new TimeoutException("The game server took too long to start. Please try again.");
	}

	private static string StatusTitle(string state) => state switch
	{
		"QUEUED" => "Join queued",
		"FINDING_SERVER" => "Finding a server",
		"BOOTING_SERVER" => "Starting a server",
		"AUTHORIZING_CLIENT" => "Securing your session",
		"READY" => "World ready",
		_ => "Joining world",
	};

	private static string StatusDetail(string state) => state switch
	{
		"BOOTING_SERVER" => "No server was ready, so a new one is starting. This can take a minute.",
		"AUTHORIZING_CLIENT" => "Creating your secure game session…",
		_ => "This can take longer when an experience server is waking up.",
	};

	public static async Task<APIAvatarResponse> GetUserAvatarFromID(string userID)
	{
		APIV3CharacterAppearanceRoot response = await _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v3/character/" + userID + "/appearance"),
			APIGenerationContext.Default.APIV3CharacterAppearanceRoot
		);

		APIV3CharacterAppearance appearance = response.Appearance;
		APIV3CharacterAccessory[] appearanceAccessories = appearance.Accessories ?? [];
		Task<APIAvatarAsset>[] accessoryTasks = appearanceAccessories.Select(
			async item =>
			{
				string marketplaceId = item.Id ?? "0";
				APIMarketplace3DItem metadata = new()
				{
					Id = marketplaceId,
					Name = item.Name ?? "",
					Type = item.Type ?? "",
				};
				try
				{
					APIMarketplace3DResponse metadataResponse = await GetMarketplace3D(marketplaceId);
					if (metadataResponse.Success)
					{
						metadata = metadataResponse.Item;
						BVAssetProvider.RegisterDirectAssetUrl(ResourceType.Mesh, metadata.MeshId, metadata.MeshUrl);
						BVAssetProvider.RegisterDirectAssetUrl(ResourceType.Texture, metadata.TextureId, metadata.TextureUrl);
					}
				}
				catch (Exception exception)
				{
					BV.PrintWarn(
						"Could not resolve avatar marketplace item ", marketplaceId,
						": ", exception.Message
					);
				}

				return new APIAvatarAsset
				{
					ID = marketplaceId,
					TextureID = metadata.TextureId ?? "",
					MeshID = metadata.MeshId ?? "",
					MeshPosition = metadata.MeshPosition,
					Type = FirstNotEmpty(item.Type, metadata.Type).ToLowerInvariant(),
					AccessoryType = FirstNotEmpty(item.Type, metadata.Type),
					Name = FirstNotEmpty(item.Name, metadata.Name),
					Thumbnail = item.ThumbnailUrl ?? "",
					Path = "",
				};
			}
		).ToArray();
		APIAvatarAsset[] assets = await Task.WhenAll(accessoryTasks);

		return new APIAvatarResponse
		{
			Colors = new APIAvatarBodyColors
			{
				Head = NormalizeAppearanceColor(appearance.HeadColor),
				Torso = NormalizeAppearanceColor(appearance.TorsoColor),
				LeftArm = NormalizeAppearanceColor(appearance.LeftArmColor),
				RightArm = NormalizeAppearanceColor(appearance.RightArmColor),
				LeftLeg = NormalizeAppearanceColor(appearance.LeftLegColor),
				RightLeg = NormalizeAppearanceColor(appearance.RightLegColor),
			},
			Assets = assets,
			IsDefault = !response.Success,
		};
	}

	private static string FirstNotEmpty(string? preferred, string? fallback) =>
		!string.IsNullOrWhiteSpace(preferred) ? preferred : fallback ?? "";

	private static string NormalizeAppearanceColor(string? color)
	{
		if (string.IsNullOrWhiteSpace(color)) return "#ffffff";
		string normalized = color.Trim();
		if (!normalized.StartsWith('#')) normalized = "#" + normalized;
		return normalized;
	}

	public static async Task<APIPlaceInfo> GetWorldFromID(long placeID)
	{
		APIV3WorldRoot info = await GetWorldRootFromID(placeID);

		APIPlaceCreator creator = new()
		{
			Type = info.Universe.CreatorType.ToLowerInvariant(),
			Id = long.TryParse(info.Universe.CreatorId, out long creatorId) ? creatorId : 0,
			Name = info.Universe.CreatorType == "GUILD"
				? (info.Universe.CreatorGuild?.Name ?? "")
				: (info.Universe.CreatorUser?.Username ?? ""),
			Thumbnail = "",
		};

		return new APIPlaceInfo
		{
			Id = long.TryParse(info.World.Id, out long worldId) ? worldId : placeID,
			UniverseId = long.TryParse(info.Universe.Id, out long universeId) ? universeId : 0,
			Name = info.World.Name,
			UniverseName = info.Universe.Name,
			Description = info.Universe.Description,
			Creator = creator,
			Thumbnail = "",
			Genre = info.Universe.Genre,
			MaxPlayers = info.World.MaxPlayers,
			Visits = info.World.TotalVisits,
			Playing = info.World.TotalPlayers,
		};
	}

	public static Task<APIV3SocialGuild> GetGuildFromID(long guildID)
		=> GetGuildInfoV3(guildID);

	private static async Task<APIV3SocialGuild> GetGuildInfoV3(long guildID)
	{
		APIV3SocialGuildRoot response = await _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v3/social/guilds/" + guildID.ToString()),
			APIGenerationContext.Default.APIV3SocialGuildRoot
		);

		APIV3SocialGuild guild = response.Guild;
		return new APIV3SocialGuild
		{
			Id = guild.Id ?? guildID.ToString(),
			Name = guild.Name,
			Description = guild.Description,
			Creator = guild.Creator,
			MemberCount = guild.MemberCount,
			IsVerified = guild.IsVerified,
			CreatedAt = guild.CreatedAt,
		};
	}

	public static async Task<APIPlaceMedia[]?> GetWorldMedia(long placeID)
	{
		APIV3WorldRoot info = await GetWorldRootFromID(placeID);
		if (info.Universe.UniverseThumbnails == null || info.Universe.UniverseThumbnails.Length == 0)
		{
			return [];
		}

		List<APIPlaceMedia> media = [];
		foreach (APIV3UniverseThumbnail thumb in info.Universe.UniverseThumbnails)
		{
			media.Add(new APIPlaceMedia
			{
				Id = thumb.ThumbnailId ?? "0",
				Type = "image",
				Url = Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + (thumb.ThumbnailId ?? "0")),
			});
		}

		return [.. media];
	}

	private static Task<APIV3WorldRoot> GetWorldRootFromID(long placeID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v3/world/" + placeID.ToString()),
			APIGenerationContext.Default.APIV3WorldRoot
		);
	}

	public static Task<APIFeedPostRoot> GetFeedPosts(int page = 1)
	{
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/feed?page=" + page.ToString()),
			APIGenerationContext.Default.APIFeedPostRoot
		);
	}

	public static Task<APIWorldsRoot> GetWorlds()
	{
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/places"),
			APIGenerationContext.Default.APIWorldsRoot
		);
	}

	public static Task<APIStoreItem> GetStoreItem(string id)
		=> GetAssetStoreItem(id);

	public static Task<APIMarketplace3DResponse> GetMarketplace3D(string id)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v3/marketplace/" + id + "/3d"),
			APIGenerationContext.Default.APIMarketplace3DResponse
		);
	}

	private static async Task<APIStoreItem> GetAssetStoreItem(string id)
	{
		APIV3AssetDetailsRoot response = await _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v3/asset/" + id + "/details"),
			APIGenerationContext.Default.APIV3AssetDetailsRoot
		);

		APIV3AssetDetails asset = response.AssetInfo;
		return new APIStoreItem
		{
			Id = asset.Id ?? id,
			Type = asset.AssetType?.ToLowerInvariant() ?? "",
			AccessoryType = null,
			Name = asset.Name ?? "",
			Description = asset.Description ?? "",
			Tags = [],
			Creator = new APIStoreItemCreator
			{
				Type = asset.CreatorType?.ToLowerInvariant() ?? "",
				Id = int.TryParse(asset.CreatorId, out int creatorId) ? creatorId : 0,
				Name = asset.CreatorId ?? "",
				Thumbnail = "",
			},
			Thumbnail = Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + asset.Id),
			Version = 0,
			Sales = asset.Sales,
			Price = asset.Price,
			Favorites = asset.Favorites,
			IsLimited = false,
			CreatedAt = asset.CreatedAt,
			UpdatedAt = asset.UpdatedAt,
		};
	}

#if CREATOR
	public static async Task<APILibraryResponse> GetLibrary(
		LibraryQueryTypeEnum type,
		int page = 1,
		string searchQuery = "",
		string creatorSearch = "",
		string topCategory = "",
		string sortBy = "newlyCreated"
	)
	{
		string queryType = type switch
		{
			LibraryQueryTypeEnum.Model => "PREFAB",
			LibraryQueryTypeEnum.Image => "TEXTURE",
			LibraryQueryTypeEnum.Audio => "SOUND",
			LibraryQueryTypeEnum.Mesh => "MESH",
			LibraryQueryTypeEnum.Addon => "PLUGIN",
			LibraryQueryTypeEnum.Font => "FONT",
			_ => ""
		};

		const int pageSize = 24;
		string? cursor = null;
		APIV3AssetDiscoverRoot current = new() { Assets = [] };

		for (int i = 1; i <= Math.Max(page, 1); i++)
		{
			string query = $"/v3/asset/discover?limit={pageSize}&assetType={queryType}";
			if (!string.IsNullOrWhiteSpace(searchQuery))
			{
				query += "&search=" + Uri.EscapeDataString(searchQuery);
			}
			if (!string.IsNullOrWhiteSpace(creatorSearch))
				query += "&creatorSearch=" + Uri.EscapeDataString(creatorSearch);
			if (!string.IsNullOrWhiteSpace(topCategory))
				query += "&topCategory=" + Uri.EscapeDataString(topCategory);
			if (!string.IsNullOrWhiteSpace(sortBy))
				query += "&sortBy=" + Uri.EscapeDataString(sortBy);
			if (!string.IsNullOrWhiteSpace(cursor))
			{
				query += "&cursor=" + Uri.EscapeDataString(cursor);
			}

			current = await _client.GetFromJsonAsync(
				Globals.ApiEndpoint.PathJoin(query),
				APIGenerationContext.Default.APIV3AssetDiscoverRoot
			);

			if (i < page && string.IsNullOrWhiteSpace(current.NextCursor))
			{
				break;
			}

			cursor = current.NextCursor;
		}

		List<APILibraryItem> mapped = new(current.Assets.Length);
		foreach (APIV3AssetDiscoverItem item in current.Assets)
		{
			mapped.Add(new APILibraryItem
			{
				ID = item.Id ?? "0",
				Name = item.Name,
				ThumbnailUrl = item.ThumbnailUrl
					?? Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + item.Id),
				CreatorID = item.CreatorId ?? "0",
				CreatorType = item.CreatorType ?? "",
				CreatorName = item.CreatorName ?? item.CreatorId ?? "Unknown Creator",
				CreatorUrl = item.CreatorType == "GUILD"
					? Globals.MainEndpoint.PathJoin("/guilds/" + item.CreatorId)
					: Globals.MainEndpoint.PathJoin("/users/" + item.CreatorId),
			});
		}

		return new APILibraryResponse
		{
			Meta = new APIMeta
			{
				CurrentPage = page,
				PerPage = pageSize,
				LastPage = !string.IsNullOrWhiteSpace(current.NextCursor) ? page + 1 : page,
				FirstPage = 1,
				Total = mapped.Count,
			},
			Data = [.. mapped],
		};
	}
#endif

	public static string? ProfanityListCache { get; set; } = null;

	public static Task<string> GetProfanityList()
	{
		if (ProfanityListCache != null)
			return Task.FromResult(ProfanityListCache);

		string path = "res://assets/profanity.txt";
		string? data = null;

		if (FileAccess.FileExists(path))
		{
			using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			data = file.GetAsText();
		}
		else
		{
			const string resourceName = "BrickVerse.Assets.profanity.txt";
			using System.IO.Stream? stream = typeof(BVAPI).Assembly.GetManifestResourceStream(
				resourceName
			);
			if (stream is not null)
			{
				using var reader = new System.IO.StreamReader(stream);
				data = reader.ReadToEnd();
			}
		}

		if (string.IsNullOrWhiteSpace(data))
			throw new InvalidOperationException(
				$"Profanity list is unavailable at {path} and was not embedded in the application."
			);

		ProfanityListCache = data;
		return Task.FromResult(data);
	}
}

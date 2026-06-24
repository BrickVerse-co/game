// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace BrickVerse.Utils;

public static class PolyAPI
{
	private static readonly PTHttpClient _client = new();

	/// <summary>
	/// BrickVerse supports auth through either Authorization: Bearer {token}
	/// or Cookie: auth_token={token}. We send both so the client works with
	/// endpoints implemented with either auth guard.
	/// </summary>
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

	public static async Task<APIJoinPlaceResponse> RequestJoinGame(APIJoinPlaceRequest req)
	{
		APIV3WorldRoot worldInfo = await GetWorldRootFromID(req.PlaceID);

		APIV3JoinWorldRequest joinRequest = new()
		{
			Platform = Globals.IsMobileBuild ? "MOBILE" : "PC",
			UniverseId = worldInfo.Universe.Id,
			WorldId = worldInfo.World.Id,
		};

		HttpResponseMessage response = await _client.PostAsJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v3/world/join"),
			joinRequest,
			APIGenerationContext.Default.APIV3JoinWorldRequest
		);

		response.EnsureSuccessStatusCode();

		APIV3JoinWorldResponse result = await response.Content.ReadFromJsonAsync(
			APIGenerationContext.Default.APIV3JoinWorldResponse
		);

		return new APIJoinPlaceResponse
		{
			Success = result.Success,
			Token = result.JoinToken,
		};
	}

	public static async Task<APIAvatarResponse> GetUserAvatarFromID(string userID)
	{
		APIV3CharacterAppearanceRoot response = await _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v3/character/" + userID + "/appearance"),
			APIGenerationContext.Default.APIV3CharacterAppearanceRoot
		);

		APIV3CharacterAppearance appearance = response.Appearance;
		List<APIAvatarAsset> assets = [];
		foreach (APIV3CharacterAccessory item in appearance.Accessories ?? [])
		{
			assets.Add(new APIAvatarAsset
			{
				ID = int.TryParse(item.Id, out int id) ? id : 0,
				Type = (item.Type ?? string.Empty).ToLowerInvariant(),
				AccessoryType = item.Type ?? "",
				Name = item.Name ?? "",
				Thumbnail = item.ThumbnailUrl ?? "",
				Path = "",
			});
		}

		return new APIAvatarResponse
		{
			Colors = new APIAvatarBodyColors
			{
				Head = appearance.HeadColor,
				Torso = appearance.TorsoColor,
				LeftArm = appearance.LeftArmColor,
				RightArm = appearance.RightArmColor,
				LeftLeg = appearance.LeftLegColor,
				RightLeg = appearance.RightLegColor,
			},
			Assets = [.. assets],
			IsDefault = !response.Success,
		};
	}

	public static async Task<APIPlaceInfo> GetWorldFromID(int placeID)
	{
		APIV3WorldRoot info = await GetWorldRootFromID(placeID);

		APIPlaceCreator creator = new()
		{
			Type = info.Universe.CreatorType.ToLowerInvariant(),
			Id = int.TryParse(info.Universe.CreatorId, out int creatorId) ? creatorId : 0,
			Name = info.Universe.CreatorType == "GUILD"
				? (info.Universe.CreatorGuild?.Name ?? "")
				: (info.Universe.CreatorUser?.Username ?? ""),
			Thumbnail = "",
		};

		return new APIPlaceInfo
		{
			Id = int.TryParse(info.World.Id, out int worldId) ? worldId : placeID,
			Name = info.World.Name,
			Description = info.Universe.Description,
			Creator = creator,
			Thumbnail = "",
			Genre = info.Universe.Genre,
			MaxPlayers = info.World.MaxPlayers,
			Visits = info.World.TotalVisits,
			Playing = info.World.TotalPlayers,
		};
	}

	public static Task<APIV3SocialGuild> GetGuildFromID(int guildID)
		=> GetGuildInfoV3(guildID);

	private static async Task<APIV3SocialGuild> GetGuildInfoV3(int guildID)
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
			Creator =  guild.Creator,
			MemberCount = guild.MemberCount,
			IsVerified = guild.IsVerified,
			CreatedAt = guild.CreatedAt,
		};
	}

	public static async Task<APIPlaceMedia[]?> GetWorldMedia(int placeID)
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
				Id = int.TryParse(thumb.ThumbnailId, out int id) ? id : 0,
				Type = "image",
				Url = Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + thumb.ThumbnailId),
			});
		}

		return [.. media];
	}

	private static Task<APIV3WorldRoot> GetWorldRootFromID(int placeID)
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

	public static Task<APIStoreItem> GetStoreItem(int id)
		=> GetAssetStoreItem(id);

	private static async Task<APIStoreItem> GetAssetStoreItem(int id)
	{
		APIV3AssetDetailsRoot response = await _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v3/asset/" + id + "/details"),
			APIGenerationContext.Default.APIV3AssetDetailsRoot
		);

		APIV3AssetDetails asset = response.AssetInfo;
		return new APIStoreItem
		{
			Id = int.TryParse(asset.Id, out int assetId) ? assetId : id,
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
	public static async Task<APILibraryResponse> GetLibrary(LibraryQueryTypeEnum type, int page = 1, string searchQuery = "")
	{
		string queryType = type switch
		{
			LibraryQueryTypeEnum.Model => "PREFAB",
			LibraryQueryTypeEnum.Image => "TEXTURE",
			LibraryQueryTypeEnum.Audio => "SOUND",
			LibraryQueryTypeEnum.Mesh => "MESH",
			LibraryQueryTypeEnum.Addon => "PLUGIN",
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
				ID = uint.TryParse(item.Id, out uint parsedId) ? parsedId : 0,
				Name = item.Name,
				ThumbnailUrl = item.ThumbnailId != null
					? Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + item.ThumbnailId)
					: "",
				CreatorID = int.TryParse(item.CreatorId, out int creatorId) ? creatorId : 0,
				CreatorName = item.CreatorId,
				CreatorUrl = "",
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

	public static Task<string> GetProfanityList()
	{
		// v3 no longer exposes a dedicated profanity list endpoint.
		return Task.FromResult("swear\n");
	}
}

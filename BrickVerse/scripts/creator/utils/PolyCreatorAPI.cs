// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading.Tasks;

namespace BrickVerse.Creator.Utils;

public static class PolyCreatorAPI
{
	private static readonly PTHttpClient _client = new();

	public static int UserID { get; private set; } = 0;
	public static APIUserInfo UserInfo { get; private set; }
	public static string Token { get; private set; } = "";

	public static event Action<int>? LaunchPlaceRequest;
	public static event Action? UserAuthenticated;
	public static bool IsUserAuthenticated { get; private set; }

	public static void SetToken(string token)
	{
		Token = token;
		_client.DefaultRequestHeaders.Remove("Authorization");
		_client.DefaultRequestHeaders.Remove("Cookie");

		if (!string.IsNullOrWhiteSpace(token))
		{
			string bearerToken = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : "Bearer " + token;
			string cookieToken = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token[7..] : token;
			_client.DefaultRequestHeaders["Authorization"] = bearerToken;
			_client.DefaultRequestHeaders["Cookie"] = "auth_token=" + Uri.EscapeDataString(cookieToken);
		}
	}

	public static async Task LoginWithToken(string token)
	{
		SetToken(token);
		PolyAPI.SetAuthToken(token);
		APIMeResponse me = await PolyAPI.GetCurrentUser();

		UserID = me.Id;
		UserInfo = new APIUserInfo
		{
			Id = me.Id,
			Username = me.Username,
			Description = me.Description,
			MembershipType = me.MembershipType,
		};

		IsUserAuthenticated = true;
		UserAuthenticated?.Invoke();
	}

	public static async Task<CreatorPlaceItem[]> GetPublishedWorlds()
	{
		if (!IsUserAuthenticated) throw new AuthenticationException("User authentication required");
		using HttpResponseMessage msg = await _client.GetAsync(Globals.ApiEndpoint.PathJoin("/v3/created-worlds"));
		msg.EnsureSuccessStatusCode();

		using JsonDocument doc = JsonDocument.Parse(await msg.Content.ReadAsStringAsync());
		if (!doc.RootElement.TryGetProperty("universes", out JsonElement universes) || universes.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		System.Collections.Generic.List<CreatorPlaceItem> worlds = [];
		foreach (JsonElement universe in universes.EnumerateArray())
		{
			if (!universe.TryGetProperty("worlds", out JsonElement worldArray) || worldArray.ValueKind != JsonValueKind.Array)
			{
				continue;
			}

			foreach (JsonElement world in worldArray.EnumerateArray())
			{
				worlds.Add(new CreatorPlaceItem
				{
					Id = world.TryGetProperty("id", out JsonElement idNode) && int.TryParse(idNode.GetString(), out int id) ? id : 0,
					Name = world.TryGetProperty("name", out JsonElement nameNode) ? (nameNode.GetString() ?? "") : "",
					CreatedAt = world.TryGetProperty("createdAt", out JsonElement createdNode) && createdNode.ValueKind == JsonValueKind.String && DateTime.TryParse(createdNode.GetString(), out DateTime createdAt) ? createdAt : DateTime.UtcNow,
					UpdatedAt = world.TryGetProperty("updatedAt", out JsonElement updatedNode) && updatedNode.ValueKind == JsonValueKind.String && DateTime.TryParse(updatedNode.GetString(), out DateTime updatedAt) ? updatedAt : null,
					IconUrl = "",
				});
			}
		}

		return [.. worlds];
	}

	public static async Task<CreatorPublishResponse> UploadWorld(byte[] placeData, int placeID = 0, string mainWorldPath = "")
	{
		if (!IsUserAuthenticated) throw new AuthenticationException("User authentication required");
		string universeId;
		string worldId;

		if (placeID != 0)
		{
			(universeId, worldId) = await ResolveWorldTarget(placeID);
		}
		else
		{
			using HttpResponseMessage createWorld = await _client.PostAsJsonAsync(
				Globals.ApiEndpoint.PathJoin("/v3/world"),
				new
				{
					ownerId = UserID.ToString(),
					ownerType = "USER",
				}
			);
			createWorld.EnsureSuccessStatusCode();

			using JsonDocument createdDoc = JsonDocument.Parse(await createWorld.Content.ReadAsStringAsync());
			worldId = createdDoc.RootElement.GetProperty("worldId").GetString() ?? "";
			universeId = createdDoc.RootElement.GetProperty("universeId").GetString() ?? "";
		}

		using MultipartFormDataContent form = new()
		{
			{ new StringContent(universeId), "universeId" },
			{ new StringContent(worldId), "worldId" },
			{ new StringContent("true"), "publish" },
		};

		ByteArrayContent fileContent = new(placeData);
		fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		form.Add(fileContent, "file", "level.ptpacked");

		using HttpResponseMessage msg = await _client.PutAsync(Globals.ApiEndpoint.PathJoin("/v3/world/editor/tree"), form);
		msg.EnsureSuccessStatusCode();

		return new CreatorPublishResponse
		{
			Link = Globals.MainEndpoint.PathJoin("/world/" + worldId),
		};
	}

	public static async Task<CreatorPublishResponse> UploadModel(byte[] modelData, int modelId = 0)
	{
		if (!IsUserAuthenticated) throw new AuthenticationException("User authentication required");
		using MultipartFormDataContent form = new()
		{
			{ new StringContent("studio-upload"), "captchaToken" },
			{ new StringContent(UserID.ToString()), "ownerId" },
			{ new StringContent("USER"), "ownerType" },
			{ new StringContent("PREFAB"), "assetType" },
			{ new StringContent(modelId == 0 ? "Model" : "Model " + modelId), "name" },
			{ new StringContent(""), "description" },
			{ new StringContent("0"), "price" },
			{ new StringContent("false"), "isForSale" },
			{ new StringContent("Ownership"), "assetPrivacy" },
		};

		ByteArrayContent fileContent = new(modelData);
		fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		form.Add(fileContent, "file", "model.ptmd");

		using HttpResponseMessage msg = await _client.PostAsync(Globals.ApiEndpoint.PathJoin("/v3/asset/create"), form);
		msg.EnsureSuccessStatusCode();

		using JsonDocument createdDoc = JsonDocument.Parse(await msg.Content.ReadAsStringAsync());
		string assetId = createdDoc.RootElement.TryGetProperty("assetId", out JsonElement assetIdNode)
			? (assetIdNode.GetString() ?? "")
			: "";

		return new CreatorPublishResponse
		{
			Link = assetId.Length == 0
				? Globals.MainEndpoint.PathJoin("/creator")
				: Globals.MainEndpoint.PathJoin("/asset/" + assetId),
		};
	}

	private static async Task<(string UniverseId, string WorldId)> ResolveWorldTarget(int placeID)
	{
		using HttpResponseMessage msg = await _client.GetAsync(Globals.ApiEndpoint.PathJoin("/v3/created-worlds"));
		msg.EnsureSuccessStatusCode();
		using JsonDocument doc = JsonDocument.Parse(await msg.Content.ReadAsStringAsync());

		if (!doc.RootElement.TryGetProperty("universes", out JsonElement universes) || universes.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidOperationException("Unable to resolve world target for publishing.");
		}

		string placeId = placeID.ToString();
		foreach (JsonElement universe in universes.EnumerateArray())
		{
			string universeId = universe.TryGetProperty("id", out JsonElement universeIdNode)
				? (universeIdNode.GetString() ?? "")
				: "";

			if (!universe.TryGetProperty("worlds", out JsonElement worlds) || worlds.ValueKind != JsonValueKind.Array)
			{
				continue;
			}

			foreach (JsonElement world in worlds.EnumerateArray())
			{
				if (world.TryGetProperty("id", out JsonElement worldIdNode) && worldIdNode.GetString() == placeId)
				{
					return (universeId, placeId);
				}
			}
		}

		throw new InvalidOperationException("The target world could not be found in your created worlds list.");
	}
}

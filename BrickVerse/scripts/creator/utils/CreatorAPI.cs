// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BrickVerse.Creator.Utils;

public static class CreatorAPI
{
	private const string OpenIDClientId = "328382387274645504";

	private const string AuthorizePath = "/v3/openid/login";
	private const string TokenPath = "/v3/openid/token";
	private const string UserInfoPath = "/v3/openid/userinfo";

	private const string StoredTokenPath = "user://creator_auth";

	private static readonly PTHttpClient _client = new();

	public static string UserID { get; private set; } = "0";
	public static string Username { get; private set; } = "";
	public static string Token { get; private set; } = "";

	public static OpenIdUserInfoResponse? CurrentUserInfo { get; private set; }

	public static event Action<int>? LaunchPlaceRequest;
	public static event Action? UserAuthenticated;
	public static event Action<string>? AuthenticationFailed;

	public static bool IsUserAuthenticated { get; private set; }

	public static async Task SetupAuth()
	{
		CreatorAuthServer.StartServer();

		string? storedToken = LoadStoredToken();

		if (!string.IsNullOrWhiteSpace(storedToken))
		{
			try
			{
				await LoginWithToken(storedToken, saveToken: false);
				return;
			}
			catch
			{
				ClearAuth();
			}
		}

		await PromptLogin();
	}

	public static void SetToken(string token)
	{
		Token = NormalizeToken(token);

		_client.DefaultRequestHeaders.Remove("Authorization");

		if (!string.IsNullOrWhiteSpace(Token))
			_client.DefaultRequestHeaders["Authorization"] = "Bearer " + Token;
	}

	public static async Task PromptLogin()
	{
		CreatorAuthServer.StartServer();

		string state = CreateCryptoRandomString(32);
		string codeVerifier = CreateCryptoRandomString(64);
		string codeChallenge = CreatePkceChallenge(codeVerifier);

		CreatorAuthServer.BeginAuthAttempt(state, codeVerifier);

		string authorizeUrl =
			Globals.ApiEndpoint.PathJoin(AuthorizePath) +
			$"?client_id={Uri.EscapeDataString(OpenIDClientId)}" +
			$"&redirect_uri={Uri.EscapeDataString(CreatorAuthServer.RedirectUri)}" +
			"&response_type=code" +
			"&scope=openid%20profile%20email%20guilds%20creator" +
			$"&state={Uri.EscapeDataString(state)}" +
			$"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
			"&code_challenge_method=S256";

		OS.ShellOpen(authorizeUrl);

		await Task.CompletedTask;
	}

	public static async Task HandleOpenIdCallback(
		string code,
		string redirectUri,
		string codeVerifier
	)
	{
		string token = await ExchangeOpenIdCodeForToken(code, redirectUri, codeVerifier);
		await LoginWithToken(token, saveToken: true);
	}

	public static async Task<string> ExchangeOpenIdCodeForToken(
		string code,
		string redirectUri,
		string codeVerifier
	)
	{
		if (string.IsNullOrWhiteSpace(code))
			throw new ArgumentException("Code cannot be empty.", nameof(code));

		if (string.IsNullOrWhiteSpace(redirectUri))
			throw new ArgumentException("Redirect URI cannot be empty.", nameof(redirectUri));

		if (string.IsNullOrWhiteSpace(codeVerifier))
			throw new ArgumentException("Code verifier cannot be empty.", nameof(codeVerifier));

		using FormUrlEncodedContent form = new(new Dictionary<string, string>
		{
			["grant_type"] = "authorization_code",
			["client_id"] = OpenIDClientId,
			["code"] = code,
			["redirect_uri"] = redirectUri,
			["code_verifier"] = codeVerifier
		});

		using HttpResponseMessage msg = await _client.PostAsync(
			Globals.ApiEndpoint.PathJoin(TokenPath),
			form
		);

		string body = await msg.Content.ReadAsStringAsync();

		if (!msg.IsSuccessStatusCode)
			throw new InvalidOperationException($"OpenID token exchange failed: {msg.StatusCode} {body}");

		using JsonDocument doc = JsonDocument.Parse(body);

		if (!doc.RootElement.TryGetProperty("access_token", out JsonElement accessTokenNode) ||
			accessTokenNode.ValueKind != JsonValueKind.String)
		{
			throw new InvalidOperationException("OpenID token response did not include access_token.");
		}

		return accessTokenNode.GetString() ?? "";
	}

	public static async Task LoginWithToken(string token, bool saveToken = true)
	{
		token = NormalizeToken(token);

		if (string.IsNullOrWhiteSpace(token))
			throw new ArgumentException("Token cannot be empty.", nameof(token));

		SetToken(token);

		try
		{
			OpenIdUserInfoResponse userInfo = await GetUserInfo();

			if (!IsValidUserInfo(userInfo))
				throw new InvalidOperationException("OpenID userinfo response was invalid.");

			CurrentUserInfo = userInfo;
			UserID = userInfo.Sub;
			Username = userInfo.PreferredUsername;

			IsUserAuthenticated = true;

			if (saveToken)
				SaveToken(token);

			UserAuthenticated?.Invoke();
		}
		catch
		{
			ClearAuth();
			throw;
		}
	}

	public static async Task<OpenIdUserInfoResponse> GetUserInfo()
	{
		if (string.IsNullOrWhiteSpace(Token))
			throw new AuthenticationException("User authentication required.");

		using HttpRequestMessage request = new(
			HttpMethod.Get,
			Globals.ApiEndpoint.PathJoin(UserInfoPath)
		);

		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

		using HttpResponseMessage msg = await _client.SendAsync(request);
		string body = await msg.Content.ReadAsStringAsync();

		if (!msg.IsSuccessStatusCode)
			throw new InvalidOperationException($"OpenID userinfo failed: {msg.StatusCode} {body}");

		OpenIdUserInfoResponse userInfo = JsonSerializer.Deserialize(
			body,
			APIGenerationContextV3.Default.OpenIdUserInfoResponse
		);

		if (!IsValidUserInfo(userInfo))
			throw new InvalidOperationException("OpenID userinfo response was missing sub or preferred_username.");

		return userInfo;
	}

	public static void ClearAuth()
	{
		Token = "";
		UserID = "0";
		Username = "";
		CurrentUserInfo = null;
		IsUserAuthenticated = false;

		_client.DefaultRequestHeaders.Remove("Authorization");
		DeleteStoredToken();
	}

	public static async Task<CreatorPlaceItem[]> GetPublishedWorlds()
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		using HttpResponseMessage msg = await _client.GetAsync(Globals.ApiEndpoint.PathJoin("/v3/created-worlds"));
		msg.EnsureSuccessStatusCode();

		using JsonDocument doc = JsonDocument.Parse(await msg.Content.ReadAsStringAsync());

		if (!doc.RootElement.TryGetProperty("universes", out JsonElement universes) ||
			universes.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		List<CreatorPlaceItem> worlds = [];

		foreach (JsonElement universe in universes.EnumerateArray())
		{
			if (!universe.TryGetProperty("worlds", out JsonElement worldArray) ||
				worldArray.ValueKind != JsonValueKind.Array)
			{
				continue;
			}

			foreach (JsonElement world in worldArray.EnumerateArray())
			{
				worlds.Add(new CreatorPlaceItem
				{
					Id = world.TryGetProperty("id", out JsonElement idNode) &&
						 int.TryParse(idNode.GetString(), out int id)
						? id
						: 0,

					Name = world.TryGetProperty("name", out JsonElement nameNode)
						? nameNode.GetString() ?? ""
						: "",

					CreatedAt = world.TryGetProperty("createdAt", out JsonElement createdNode) &&
								createdNode.ValueKind == JsonValueKind.String &&
								DateTime.TryParse(createdNode.GetString(), out DateTime createdAt)
						? createdAt
						: DateTime.UtcNow,

					UpdatedAt = world.TryGetProperty("updatedAt", out JsonElement updatedNode) &&
								updatedNode.ValueKind == JsonValueKind.String &&
								DateTime.TryParse(updatedNode.GetString(), out DateTime updatedAt)
						? updatedAt
						: null,

					IconUrl = "",
				});
			}
		}

		return [.. worlds];
	}

	public static async Task<CreatorPublishResponse> UploadWorld(
		byte[] placeData,
		int placeID = 0,
		string mainWorldPath = ""
	)
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		string universeId;
		string worldId;

		if (placeID != 0)
		{
			(universeId, worldId) = await ResolveWorldTarget(placeID);
		}
		else
		{
			using FormUrlEncodedContent createForm = new(new Dictionary<string, string>
			{
				["ownerId"] = UserID,
				["ownerType"] = "USER"
			});

			using HttpResponseMessage createWorld = await _client.PostAsync(
				Globals.ApiEndpoint.PathJoin("/v3/world"),
				createForm
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

		using HttpResponseMessage msg = await _client.PutAsync(
			Globals.ApiEndpoint.PathJoin("/v3/world/editor/tree"),
			form
		);

		msg.EnsureSuccessStatusCode();

		return new CreatorPublishResponse
		{
			Link = Globals.MainEndpoint.PathJoin("/world/" + worldId),
		};
	}

	public static async Task<CreatorPublishResponse> UploadModel(byte[] modelData, int modelId = 0)
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		using MultipartFormDataContent form = new()
		{
			{ new StringContent("studio-upload"), "captchaToken" },
			{ new StringContent(UserID), "ownerId" },
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

		using HttpResponseMessage msg = await _client.PostAsync(
			Globals.ApiEndpoint.PathJoin("/v3/asset/create"),
			form
		);

		msg.EnsureSuccessStatusCode();

		using JsonDocument createdDoc = JsonDocument.Parse(await msg.Content.ReadAsStringAsync());

		string assetId = createdDoc.RootElement.TryGetProperty("assetId", out JsonElement assetIdNode)
			? assetIdNode.GetString() ?? ""
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

		if (!doc.RootElement.TryGetProperty("universes", out JsonElement universes) ||
			universes.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidOperationException("Unable to resolve world target for publishing.");
		}

		string placeId = placeID.ToString();

		foreach (JsonElement universe in universes.EnumerateArray())
		{
			string universeId = universe.TryGetProperty("id", out JsonElement universeIdNode)
				? universeIdNode.GetString() ?? ""
				: "";

			if (!universe.TryGetProperty("worlds", out JsonElement worlds) ||
				worlds.ValueKind != JsonValueKind.Array)
			{
				continue;
			}

			foreach (JsonElement world in worlds.EnumerateArray())
			{
				if (world.TryGetProperty("id", out JsonElement worldIdNode) &&
					worldIdNode.GetString() == placeId)
				{
					return (universeId, placeId);
				}
			}
		}

		throw new InvalidOperationException("The target world could not be found in your created worlds list.");
	}

	private static bool IsValidUserInfo(OpenIdUserInfoResponse userInfo)
	{
		return !string.IsNullOrWhiteSpace(userInfo.Sub)
			&& !string.IsNullOrWhiteSpace(userInfo.PreferredUsername);
	}

	private static string NormalizeToken(string token)
	{
		if (string.IsNullOrWhiteSpace(token))
			return "";

		string normalized = token.Trim();

		if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			normalized = normalized["Bearer ".Length..].Trim();

		return normalized;
	}

	private static string CreateCryptoRandomString(int byteLength)
	{
		byte[] bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(byteLength);
		return Base64UrlEncode(bytes);
	}

	private static string CreatePkceChallenge(string codeVerifier)
	{
		byte[] bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
		return Base64UrlEncode(bytes);
	}

	private static string Base64UrlEncode(byte[] bytes)
	{
		return Convert.ToBase64String(bytes)
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');
	}

	private static string? LoadStoredToken()
	{
		if (!FileAccess.FileExists(StoredTokenPath))
			return null;

		using FileAccess access = FileAccess.Open(StoredTokenPath, FileAccess.ModeFlags.Read);
		return access.GetAsText().Trim();
	}

	private static void SaveToken(string token)
	{
		using FileAccess f = FileAccess.Open(StoredTokenPath, FileAccess.ModeFlags.Write);
		f.StoreString(NormalizeToken(token));
	}

	private static void DeleteStoredToken()
	{
		if (FileAccess.FileExists(StoredTokenPath))
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(StoredTokenPath));
	}
}
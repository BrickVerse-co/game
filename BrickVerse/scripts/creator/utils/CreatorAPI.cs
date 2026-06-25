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

	private const string AuthorizePath = "/oauth/authorize";
	private const string TokenPath = "/v3/oauth/token";
	private const string UserInfoPath = "/v3/oauth/userinfo";

	private const string StoredTokenPath = "user://creator_auth";

	private static readonly PTHttpClient _client = new();

	public static string UserID { get; private set; } = "0";
	public static string Username { get; private set; } = "";
	public static string Token { get; private set; } = "";

	public static OpenIdUserInfoResponse? CurrentUserInfo { get; private set; }

	public static event Action<int>? LaunchPlaceRequest;
	public static event Action<OpenIdUserInfoResponse>? UserAuthenticated;
	public static event Action<string>? AuthenticationFailed;

	public static bool IsUserAuthenticated { get; private set; }

	public static async Task SetupAuth()
	{
		CreatorAuthServer.StartServer();

		OpenIdAuthSession? storedSession = LoadStoredSession();

		if (storedSession != null && !string.IsNullOrWhiteSpace(storedSession.AccessToken))
		{
			try
			{
				await LoginWithOpenIdSession(storedSession, saveToken: false);
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
			Globals.MainEndpoint.PathJoin(AuthorizePath) +
			$"?client_id={Uri.EscapeDataString(OpenIDClientId)}" +
			$"&redirect_uri={Uri.EscapeDataString(CreatorAuthServer.RedirectUri)}" +
			"&response_type=code" +
			"&scope=openid%20profile%20email%20guilds%20assets%20worlds" +
			$"&state={Uri.EscapeDataString(state)}" +
			$"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
			"&code_challenge_method=S256";

		OS.ShellOpen(authorizeUrl);

		await Task.CompletedTask;
	}

	private const string DiscoveryUrl = "https://api.brickverse.gg/.well-known/openid-configuration";

	private sealed class OpenIdConfig
	{
		public string AuthorizationEndpoint { get; init; } = "";
		public string TokenEndpoint { get; init; } = Globals.ApiEndpoint.PathJoin(TokenPath);
		public string UserInfoEndpoint { get; init; } = Globals.ApiEndpoint.PathJoin(UserInfoPath);
	}

	private sealed class OpenIdTokenResponse
	{
		public string AccessToken { get; init; } = "";
		public string TokenType { get; init; } = "Bearer";
		public string RefreshToken { get; init; } = "";
		public string IdToken { get; init; } = "";
		public int ExpiresIn { get; init; }
	}

	private sealed class OpenIdAuthSession
	{
		public string AccessToken { get; init; } = "";
		public string RefreshToken { get; init; } = "";
		public string IdToken { get; init; } = "";
	}

	private static async Task<OpenIdConfig> GetOpenIdConfig()
	{
		using HttpResponseMessage msg = await _client.GetAsync(DiscoveryUrl);
		string body = await msg.Content.ReadAsStringAsync();

		if (!msg.IsSuccessStatusCode)
			throw new InvalidOperationException($"OpenID discovery failed: {msg.StatusCode} {body}");

		using JsonDocument doc = JsonDocument.Parse(body);
		JsonElement root = doc.RootElement;

		string tokenEndpoint = GetString(root, "token_endpoint");
		string userInfoEndpoint = GetString(root, "userinfo_endpoint");

		if (string.IsNullOrWhiteSpace(tokenEndpoint))
			throw new InvalidOperationException("OpenID discovery response did not include token_endpoint.");

		if (string.IsNullOrWhiteSpace(userInfoEndpoint))
			throw new InvalidOperationException("OpenID discovery response did not include userinfo_endpoint.");

		return new OpenIdConfig
		{
			AuthorizationEndpoint = GetString(root, "authorization_endpoint"),
			TokenEndpoint = tokenEndpoint,
			UserInfoEndpoint = userInfoEndpoint,
		};
	}

	public static async Task HandleOpenIdCallback(
		string code,
		string redirectUri,
		string codeVerifier
	)
	{
		try
		{
			OpenIdConfig oidc = await GetOpenIdConfig();

			OpenIdTokenResponse tokens = await ExchangeOpenIdCodeForToken(
				oidc,
				code,
				redirectUri,
				codeVerifier
			);

			PT.Print($"Authenticated with OpenID. Received access token: {tokens.AccessToken[..Math.Min(tokens.AccessToken.Length, 10)]}...");

			await LoginWithOpenIdSession(new OpenIdAuthSession
			{
				AccessToken = tokens.AccessToken,
				RefreshToken = tokens.RefreshToken,
				IdToken = tokens.IdToken,
			}, saveToken: true, oidc);
		}
		catch (Exception ex)
		{
			AuthenticationFailed?.Invoke(ex.Message);
			throw;
		}
	}

	private static async Task<OpenIdTokenResponse> ExchangeOpenIdCodeForToken(
		OpenIdConfig oidc,
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

		using HttpResponseMessage msg = await _client.PostAsync(oidc.TokenEndpoint, form);
		string body = await msg.Content.ReadAsStringAsync();

		//PT.Print($"OpenID token exchange response: {body}");

		if (!msg.IsSuccessStatusCode)
			throw new InvalidOperationException($"OpenID token exchange failed: {msg.StatusCode} {body}");

		using JsonDocument doc = JsonDocument.Parse(body);
		JsonElement root = doc.RootElement;

		string accessToken = GetString(root, "access_token");

		if (string.IsNullOrWhiteSpace(accessToken))
			throw new InvalidOperationException("OpenID token response did not include access_token.");

		return new OpenIdTokenResponse
		{
			AccessToken = accessToken,
			TokenType = GetString(root, "token_type", "Bearer"),
			RefreshToken = GetString(root, "refresh_token"),
			IdToken = GetString(root, "id_token"),
			ExpiresIn = GetInt(root, "expires_in"),
		};
	}

	public static Task LoginWithToken(string token, bool saveToken)
	{
		return LoginWithOpenIdSession(new OpenIdAuthSession
		{
			AccessToken = NormalizeToken(token),
		}, saveToken, null);
	}

	private static async Task LoginWithOpenIdSession(
		OpenIdAuthSession session,
		bool saveToken,
		OpenIdConfig? oidc = null
	)
	{
		string accessToken = NormalizeToken(session.AccessToken);

		if (string.IsNullOrWhiteSpace(accessToken))
			throw new ArgumentException("Access token cannot be empty.", nameof(session));

		SetToken(accessToken);

		OpenIdUserInfoResponse userInfo;

		if (!string.IsNullOrWhiteSpace(session.IdToken))
		{
			userInfo = GetUserInfoFromIdToken(session.IdToken);
		}
		else
		{
			oidc ??= await GetOpenIdConfig();
			userInfo = await GetUserInfo(accessToken, oidc);
		}

		if (!IsValidUserInfo(userInfo))
			throw new InvalidOperationException("OpenID response did not include a valid subject and username.");

		if (saveToken)
			SaveStoredSession(new OpenIdAuthSession
			{
				AccessToken = accessToken,
				RefreshToken = session.RefreshToken,
				IdToken = session.IdToken,
			});

		CurrentUserInfo = userInfo;
		UserID = GetUserId(userInfo);
		Username = userInfo.PreferredUsername;
		IsUserAuthenticated = true;

		PT.Print($"CreatorAPI: User authenticated as {Username} ({UserID})");

		UserAuthenticated?.Invoke(userInfo);
	}

	private static OpenIdUserInfoResponse GetUserInfoFromIdToken(string idToken)
	{
		string[] parts = idToken.Split('.');

		if (parts.Length < 2)
			throw new InvalidOperationException("OpenID id_token is not a valid JWT.");

		byte[] payloadBytes = Base64UrlDecode(parts[1]);
		using JsonDocument doc = JsonDocument.Parse(payloadBytes);
		JsonElement root = doc.RootElement;

		string sub = GetString(root, "sub");
		string preferredUsername = GetString(root, "preferred_username");

		if (string.IsNullOrWhiteSpace(preferredUsername))
			preferredUsername = GetString(root, "name");

		return new OpenIdUserInfoResponse
		{
			Sub = sub,
			PreferredUsername = preferredUsername,
		};
	}

	private static async Task<OpenIdUserInfoResponse> GetUserInfo(
		string accessToken,
		OpenIdConfig oidc
	)
	{
		if (string.IsNullOrWhiteSpace(oidc.UserInfoEndpoint))
			throw new InvalidOperationException("OpenID discovery response did not include userinfo_endpoint.");

		using HttpRequestMessage req = new(HttpMethod.Get, oidc.UserInfoEndpoint);
		req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeToken(accessToken));

		using HttpResponseMessage msg = await _client.SendAsync(req);
		string body = await msg.Content.ReadAsStringAsync();

		PT.Print($"OpenID userinfo response: {body}");

		if (!msg.IsSuccessStatusCode)
			throw new InvalidOperationException($"OpenID userinfo failed: {msg.StatusCode} {body}");

		using JsonDocument doc = JsonDocument.Parse(body);
		JsonElement root = doc.RootElement;

		string sub = GetString(root, "sub");
		string preferredUsername = GetString(root, "preferred_username");

		if (string.IsNullOrWhiteSpace(preferredUsername))
			preferredUsername = GetString(root, "name");

		return new OpenIdUserInfoResponse
		{
			Sub = sub,
			PreferredUsername = preferredUsername,
		};
	}

	private static string GetUserId(OpenIdUserInfoResponse userInfo)
	{
		if (!string.IsNullOrWhiteSpace(userInfo.Sub))
			return userInfo.Sub;

		return "0";
	}

	private static string GetString(JsonElement root, string propertyName, string fallback = "")
	{
		if (!root.TryGetProperty(propertyName, out JsonElement node) ||
			node.ValueKind == JsonValueKind.Null ||
			node.ValueKind == JsonValueKind.Undefined)
		{
			return fallback;
		}

		if (node.ValueKind == JsonValueKind.String)
			return node.GetString() ?? fallback;

		return node.ToString();
	}

	private static int GetInt(JsonElement root, string propertyName, int fallback = 0)
	{
		if (!root.TryGetProperty(propertyName, out JsonElement node))
			return fallback;

		if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out int value))
			return value;

		if (node.ValueKind == JsonValueKind.String && int.TryParse(node.GetString(), out value))
			return value;

		return fallback;
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

	private static byte[] Base64UrlDecode(string value)
	{
		string normalized = value
			.Replace('-', '+')
			.Replace('_', '/');

		int padding = normalized.Length % 4;

		if (padding > 0)
			normalized = normalized.PadRight(normalized.Length + 4 - padding, '=');

		return Convert.FromBase64String(normalized);
	}

	private static OpenIdAuthSession? LoadStoredSession()
	{
		if (!FileAccess.FileExists(StoredTokenPath))
			return null;

		using FileAccess access = FileAccess.Open(StoredTokenPath, FileAccess.ModeFlags.Read);
		string raw = access.GetAsText().Trim();

		if (string.IsNullOrWhiteSpace(raw))
			return null;

		// Backwards compatibility with the old file format, which only stored the access token.
		if (!raw.StartsWith('{'))
		{
			return new OpenIdAuthSession
			{
				AccessToken = NormalizeToken(raw),
			};
		}

		using JsonDocument doc = JsonDocument.Parse(raw);
		JsonElement root = doc.RootElement;

		string accessToken = GetString(root, "access_token");

		if (string.IsNullOrWhiteSpace(accessToken))
			accessToken = GetString(root, "accessToken");

		return new OpenIdAuthSession
		{
			AccessToken = NormalizeToken(accessToken),
			RefreshToken = GetString(root, "refresh_token"),
			IdToken = GetString(root, "id_token"),
		};
	}

	private static void SaveStoredSession(OpenIdAuthSession session)
	{
		using FileAccess f = FileAccess.Open(StoredTokenPath, FileAccess.ModeFlags.Write);

		string json = "{" +
			$"\"access_token\":\"{EscapeJson(NormalizeToken(session.AccessToken))}\"," +
			$"\"refresh_token\":\"{EscapeJson(session.RefreshToken)}\"," +
			$"\"id_token\":\"{EscapeJson(session.IdToken)}\"" +
			"}";

		f.StoreString(json);
	}

	private static string EscapeJson(string value)
	{
		return value
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"");
	}

	private static void DeleteStoredToken()
	{
		if (FileAccess.FileExists(StoredTokenPath))
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(StoredTokenPath));
	}
}
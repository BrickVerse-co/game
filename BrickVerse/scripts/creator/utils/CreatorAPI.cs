// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SystemNetHttp = System.Net.Http;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Creator.Utils;

public static class CreatorAPI
{
	private const string OpenIDClientId = "328382387274645504";

	private static readonly string[] Scopes = new[]
	{
		"openid",
		"profile",
		"email",
		"guilds", // view guilds the user is a member of (get list of guilds they can publish to)
		"assets", // view owned assets (get list of models they can publish to)
		"worlds",
		"manage_assets", // manage owned assets (create/update models)
		"publish_worlds", // manage owned worlds (create/update worlds)
	};

	private const string AuthorizePath = "/oauth/authorize";
	private const string TokenPath = "/v3/oauth/token";
	private const string UserInfoPath = "/v3/oauth/userinfo";
	private const string StoredTokenPath = "user://creator_auth";

	private static readonly BVHttpClient _client = new();
	private static readonly SystemNetHttp.HttpClient _uploadClient = new();

	public static string UserID { get; private set; } = "0";
	public static string Username { get; private set; } = "";
	public static string Token { get; private set; } = "";

	public static OpenIdUserInfoResponse? CurrentUserInfo { get; private set; }
	public static AuthenticatedUserProfile? CurrentAuthenticatedProfile { get; private set; }
	public static ToolbarIdentity? CurrentToolbarIdentity { get; private set; }
	public static string? PendingIdToken { get; set; }

	public static event Action<int>? LaunchPlaceRequest;
	public static event Action<OpenIdUserInfoResponse>? UserAuthenticated;
	public static event Action<AuthenticatedUserProfile?>? AuthenticatedProfileUpdated;
	public static event Action<ToolbarIdentity?>? ToolbarIdentityUpdated;
	public static event Action<string>? AuthenticationFailed;

	public struct AuthenticatedUserProfile
	{
		public string Username { get; set; }
		public string? HeadshotUrl { get; set; }
		public bool IsModerator { get; set; }
		public bool IsVerified { get; set; }
	}

	public struct ToolbarIdentity
	{
		public string Username { get; set; }
		public string? HeadshotUrl { get; set; }
		public string? BadgeIconPath { get; set; }
		public string? BadgeTooltip { get; set; }
	}

	public static bool IsUserAuthenticated { get; private set; }

	public static async Task SetupAuth()
	{
		CreatorAuthServer.StartServer();

		OpenIdAuthSession? storedSession = LoadStoredSession();

		if (storedSession != null && !string.IsNullOrWhiteSpace(storedSession.AccessToken))
		{
			PT.Print("CreatorAPI: Attempting to restore auth session from storage...");

			try
			{
				// Check if token is expired and needs refresh
				if (IsTokenExpired(storedSession))
				{
					PT.Print("CreatorAPI: Stored token is expired, attempting refresh...");

					if (!string.IsNullOrWhiteSpace(storedSession.RefreshToken))
					{
						storedSession = await RefreshAccessToken(storedSession);
						if (storedSession == null)
						{
							PT.PrintErr("CreatorAPI: Failed to refresh expired token, prompting login");
							ClearAuth();
							await PromptLogin();
							return;
						}
					}
					else
					{
						// No refresh token and access token expired
						PT.PrintErr("CreatorAPI: Token expired and no refresh token available, prompting login");
						ClearAuth();
						await PromptLogin();
						return;
					}
				}
				else
				{
					PT.Print("CreatorAPI: Stored token is still valid");
				}

				PT.Print("CreatorAPI: Restoring session from stored token");
				await LoginWithOpenIdSession(storedSession, saveToken: false);
				return;
			}
			catch (Exception error)
			{
				PT.PrintErr("CreatorAPI: Failed to restore auth session: ", error.Message);
				ClearAuth();
			}
		}
		else
		{
			PT.Print("CreatorAPI: No stored session found or session is invalid");
		}

		await PromptLogin();
	}

	public static void SetToken(string token)
	{
		Token = NormalizeToken(token);

		_client.DefaultRequestHeaders.Remove("Authorization");
		_client.DefaultRequestHeaders.Remove("Cookie");

		if (!string.IsNullOrWhiteSpace(Token))
		{
			_client.DefaultRequestHeaders["Authorization"] = "Bearer " + Token;
			_client.DefaultRequestHeaders["Cookie"] = "auth_token=" + Uri.EscapeDataString(Token);
		}
	}

	public static async Task PromptLogin()
	{
		CreatorAuthServer.StartServer();

		string stateNonce = CreateCryptoRandomString(32);
		string codeVerifier = CreateCryptoRandomString(64);
		string codeChallenge = CreatePkceChallenge(codeVerifier);
		string state = stateNonce + "." + codeVerifier;

		CreatorAuthServer.BeginAuthAttempt(state, codeVerifier);

		string authorizeUrl =
			Globals.MainEndpoint.PathJoin(AuthorizePath)
			+ $"?client_id={Uri.EscapeDataString(OpenIDClientId)}"
			+ $"&redirect_uri={Uri.EscapeDataString(CreatorAuthServer.RedirectUri)}"
			+ "&response_type=code"
			+ $"&scope={Uri.EscapeDataString(string.Join(",", Scopes))}"
			+ $"&state={Uri.EscapeDataString(state)}"
			+ $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
			+ "&code_challenge_method=S256";

		OS.ShellOpen(authorizeUrl);

		await Task.CompletedTask;
	}

	private static readonly string DiscoveryUrl = new Uri(
		new Uri(Globals.ApiEndpoint),
		"/.well-known/openid-configuration"
	).ToString();

	private sealed class OpenIdConfig
	{
		public string AuthorizationEndpoint { get; init; } = "";
		public string TokenEndpoint { get; init; } = Globals.ApiEndpoint.PathJoin(TokenPath);
		public string UserInfoEndpoint { get; init; } = Globals.ApiEndpoint.PathJoin(UserInfoPath);
	}

	private sealed class OpenIdAuthSession
	{
		public string AccessToken { get; init; } = "";
		public string RefreshToken { get; init; } = "";
		public string IdToken { get; init; } = "";
		public long ExpiresAt { get; init; } = 0; // Unix timestamp
	}

	private static bool IsTokenExpired(OpenIdAuthSession session)
	{
		if (session.ExpiresAt <= 0)
			return false; // Unknown expiration, assume valid

		// Check if token expires in the next 5 minutes (300 seconds)
		long currentTime = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
		long bufferSeconds = 300;

		return currentTime >= (session.ExpiresAt - bufferSeconds);
	}

	private static async Task<OpenIdAuthSession?> RefreshAccessToken(OpenIdAuthSession session)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(session.RefreshToken))
				return null;

			OpenIdConfig oidc = await GetOpenIdConfig();

			var content = new FormUrlEncodedContent(new[]
			{
				new KeyValuePair<string, string>("grant_type", "refresh_token"),
				new KeyValuePair<string, string>("refresh_token", session.RefreshToken),
				new KeyValuePair<string, string>("client_id", OpenIDClientId),
			});

			using HttpResponseMessage msg = await _client.PostAsync(oidc.TokenEndpoint, content);
			string body = await msg.Content.ReadAsStringAsync();

			if (!msg.IsSuccessStatusCode)
			{
				PT.PrintErr($"CreatorAPI: Token refresh failed: {msg.StatusCode} {body}");
				return null;
			}

			using JsonDocument doc = JsonDocument.Parse(body);
			JsonElement root = doc.RootElement;

			string newAccessToken = GetString(root, "access_token");
			string newRefreshToken = GetString(root, "refresh_token");
			string newIdToken = GetString(root, "id_token");

			if (string.IsNullOrWhiteSpace(newAccessToken))
				return null;

			// Use new refresh token if provided, otherwise keep the old one
			if (string.IsNullOrWhiteSpace(newRefreshToken))
				newRefreshToken = session.RefreshToken;

			long expiresAt = 0;
			if (!string.IsNullOrWhiteSpace(newIdToken))
			{
				expiresAt = GetTokenExpirationFromIdToken(newIdToken);
			}

			PT.Print("CreatorAPI: Token successfully refreshed");

			return new OpenIdAuthSession
			{
				AccessToken = NormalizeToken(newAccessToken),
				RefreshToken = newRefreshToken,
				IdToken = newIdToken,
				ExpiresAt = expiresAt,
			};
		}
		catch (Exception error)
		{
			PT.PrintErr("CreatorAPI: Exception during token refresh: ", error.Message);
			return null;
		}
	}

	private static async Task<OpenIdConfig> GetOpenIdConfig()
	{
		using HttpResponseMessage msg = await _client.GetAsync(DiscoveryUrl);
		string body = await msg.Content.ReadAsStringAsync();

		if (!msg.IsSuccessStatusCode)
			throw new InvalidOperationException(
				$"OpenID discovery failed: {msg.StatusCode} {body}"
			);

		using JsonDocument doc = JsonDocument.Parse(body);
		JsonElement root = doc.RootElement;

		string tokenEndpoint = GetString(root, "token_endpoint");
		string userInfoEndpoint = GetString(root, "userinfo_endpoint");
		Uri apiBaseUri = new(Globals.ApiEndpoint);
		Uri? discoveredUserInfoUri = Uri.TryCreate(userInfoEndpoint, UriKind.Absolute, out Uri? parsedUserInfo)
			? parsedUserInfo
			: null;

		if (string.IsNullOrWhiteSpace(tokenEndpoint))
			throw new InvalidOperationException(
				"OpenID discovery response did not include token_endpoint."
			);

		if (string.IsNullOrWhiteSpace(userInfoEndpoint))
			throw new InvalidOperationException(
				"OpenID discovery response did not include userinfo_endpoint."
			);

		string resolvedUserInfoEndpoint = Globals.ApiEndpoint.PathJoin(UserInfoPath);

		if (
			discoveredUserInfoUri != null
			&& string.Equals(
				discoveredUserInfoUri.Scheme,
				apiBaseUri.Scheme,
				StringComparison.OrdinalIgnoreCase
			)
			&& string.Equals(
				discoveredUserInfoUri.Host,
				apiBaseUri.Host,
				StringComparison.OrdinalIgnoreCase
			)
			&& discoveredUserInfoUri.Port == apiBaseUri.Port
		)
		{
			resolvedUserInfoEndpoint = userInfoEndpoint;
		}
		else
		{
			PT.Print(
				"CreatorAPI: discovery userinfo endpoint does not match local API, using local endpoint instead: "
				+ resolvedUserInfoEndpoint
			);
		}

		return new OpenIdConfig
		{
			AuthorizationEndpoint = GetString(root, "authorization_endpoint"),
			TokenEndpoint = tokenEndpoint,
			UserInfoEndpoint = resolvedUserInfoEndpoint,
		};
	}

	public static Task LoginWithToken(string token, bool saveToken)
	{
		string? idToken = PendingIdToken;
		PendingIdToken = null;

		return LoginWithOpenIdSession(
			new OpenIdAuthSession { AccessToken = NormalizeToken(token), IdToken = idToken ?? "" },
			saveToken,
			null
		);
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

		OpenIdUserInfoResponse userInfo;
		long expiresAt = session.ExpiresAt;

		if (!string.IsNullOrWhiteSpace(session.IdToken))
		{
			userInfo = GetUserInfoFromIdToken(session.IdToken);
			// Extract expiration from ID token if not already set
			if (expiresAt == 0)
			{
				expiresAt = GetTokenExpirationFromIdToken(session.IdToken);
			}
		}
		else
		{
			oidc ??= await GetOpenIdConfig();
			userInfo = await GetUserInfo(accessToken, oidc);
		}

		if (!IsValidUserInfo(userInfo))
			throw new InvalidOperationException(
				"OpenID response did not include a valid subject and username."
			);

		SetToken(accessToken);

		if (saveToken)
			SaveStoredSession(
				new OpenIdAuthSession
				{
					AccessToken = accessToken,
					RefreshToken = session.RefreshToken,
					IdToken = session.IdToken,
					ExpiresAt = expiresAt,
				}
			);

		CurrentUserInfo = userInfo;
		UserID = GetUserId(userInfo);
		Username = userInfo.PreferredUsername;
		IsUserAuthenticated = true;

		PT.Print($"CreatorAPI: User authenticated as {Username} ({UserID})");

		UserAuthenticated?.Invoke(userInfo);
		await RefreshAuthenticatedProfile();
		await RefreshToolbarIdentity();
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

		return new OpenIdUserInfoResponse { Sub = sub, PreferredUsername = preferredUsername };
	}

	private static long GetTokenExpirationFromIdToken(string idToken)
	{
		try
		{
			string[] parts = idToken.Split('.');

			if (parts.Length < 2)
				return 0;

			byte[] payloadBytes = Base64UrlDecode(parts[1]);
			using JsonDocument doc = JsonDocument.Parse(payloadBytes);
			JsonElement root = doc.RootElement;

			if (root.TryGetProperty("exp", out JsonElement expNode))
			{
				if (expNode.ValueKind == JsonValueKind.Number && expNode.TryGetInt64(out long expValue))
					return expValue;

				if (expNode.ValueKind == JsonValueKind.String && long.TryParse(expNode.GetString(), out long parsedExp))
					return parsedExp;
			}

			return 0;
		}
		catch
		{
			return 0;
		}
	}

	private static async Task<OpenIdUserInfoResponse> GetUserInfo(
		string accessToken,
		OpenIdConfig oidc
	)
	{
		if (string.IsNullOrWhiteSpace(oidc.UserInfoEndpoint))
			throw new InvalidOperationException(
				"OpenID discovery response did not include userinfo_endpoint."
			);

		using HttpRequestMessage req = new(HttpMethod.Get, oidc.UserInfoEndpoint);
		req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
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

		return new OpenIdUserInfoResponse { Sub = sub, PreferredUsername = preferredUsername };
	}

	private static async Task EnsureTokenValid()
	{
		if (!IsUserAuthenticated || string.IsNullOrWhiteSpace(Token))
			return;

		OpenIdAuthSession? storedSession = LoadStoredSession();
		if (storedSession != null && IsTokenExpired(storedSession))
		{
			if (!string.IsNullOrWhiteSpace(storedSession.RefreshToken))
			{
				OpenIdAuthSession? refreshedSession = await RefreshAccessToken(storedSession);
				if (refreshedSession != null)
				{
					SetToken(refreshedSession.AccessToken);
					SaveStoredSession(refreshedSession);
					PT.Print("CreatorAPI: Token automatically refreshed before API call");
				}
			}
		}
	}

	private static async Task RefreshAuthenticatedProfile()
	{
		try
		{
			await EnsureTokenValid();
			string authMeUrl = Globals.ApiEndpoint.PathJoin("/v3/auth/me");

			using HttpRequestMessage req = new(HttpMethod.Get, authMeUrl);
			using HttpResponseMessage msg = await _client.SendAsync(req);

			string body = await msg.Content.ReadAsStringAsync();

			if (!msg.IsSuccessStatusCode)
			{
				throw new InvalidOperationException(
					$"Auth profile request failed: {msg.StatusCode} {body}"
				);
			}

			using JsonDocument doc = JsonDocument.Parse(body);
			JsonElement root = doc.RootElement;

			if (
				!root.TryGetProperty("success", out JsonElement successNode)
				|| successNode.ValueKind != JsonValueKind.True
					&& successNode.ValueKind != JsonValueKind.False
				|| !successNode.GetBoolean()
			)
			{
				throw new InvalidOperationException(
					"Auth profile request did not return a successful response."
				);
			}

			if (
				!root.TryGetProperty("user", out JsonElement userNode)
				|| userNode.ValueKind != JsonValueKind.Object
			)
			{
				throw new InvalidOperationException(
					"Auth profile request did not include a user object."
				);
			}

			AuthenticatedUserProfile profile = new()
			{
				Username = GetString(userNode, "username", Username),
				HeadshotUrl = GetString(userNode, "headshotUrl"),
				IsModerator = GetBool(userNode, "isModerator"),
				IsVerified = GetBool(userNode, "isVerified"),
			};

			CurrentAuthenticatedProfile = profile;
			AuthenticatedProfileUpdated?.Invoke(profile);
		}
		catch (Exception error)
		{
			CurrentAuthenticatedProfile = null;
			AuthenticatedProfileUpdated?.Invoke(null);
			PT.PrintErr("CreatorAPI: Failed to load authenticated profile: ", error.Message);
		}
	}

	private static async Task RefreshToolbarIdentity()
	{
		try
		{
			await EnsureTokenValid();
			if (string.IsNullOrWhiteSpace(Username))
			{
				CurrentToolbarIdentity = null;
				ToolbarIdentityUpdated?.Invoke(null);
				return;
			}

			string lookupUrl =
				Globals.ApiEndpoint.PathJoin("/v3/users/lookup")
				+ "?username="
				+ Uri.EscapeDataString(Username);
			using HttpResponseMessage msg = await _client.GetAsync(lookupUrl);
			string body = await msg.Content.ReadAsStringAsync();

			if (!msg.IsSuccessStatusCode)
			{
				throw new InvalidOperationException($"User lookup failed: {msg.StatusCode} {body}");
			}

			using JsonDocument doc = JsonDocument.Parse(body);
			JsonElement root = doc.RootElement;

			if (
				!root.TryGetProperty("success", out JsonElement successNode)
				|| (
					successNode.ValueKind != JsonValueKind.True
					&& successNode.ValueKind != JsonValueKind.False
				)
				|| !successNode.GetBoolean()
			)
			{
				throw new InvalidOperationException(
					"User lookup did not return a successful response."
				);
			}

			if (
				!root.TryGetProperty("user", out JsonElement userNode)
				|| userNode.ValueKind != JsonValueKind.Object
			)
			{
				throw new InvalidOperationException("User lookup did not include a user object.");
			}

			ToolbarIdentity identity = new()
			{
				Username = GetString(userNode, "username", Username),
				HeadshotUrl = GetString(userNode, "headshotUrl"),
			};

			ResolveToolbarBadge(userNode, ref identity);

			CurrentToolbarIdentity = identity;
			ToolbarIdentityUpdated?.Invoke(identity);
		}
		catch (Exception error)
		{
			CurrentToolbarIdentity = null;
			ToolbarIdentityUpdated?.Invoke(null);
			PT.PrintErr("CreatorAPI: Failed to load toolbar identity: ", error.Message);
		}
	}

	private static void ResolveToolbarBadge(JsonElement userNode, ref ToolbarIdentity identity)
	{
		if (
			!userNode.TryGetProperty("nameplate", out JsonElement nameplateNode)
			|| nameplateNode.ValueKind != JsonValueKind.Array
		)
		{
			return;
		}

		foreach (JsonElement plate in nameplateNode.EnumerateArray())
		{
			string name = GetString(plate, "name");
			string iconName = GetString(plate, "iconName");

			if (
				string.Equals(name, "Administrator", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(iconName, "Admin.png", StringComparison.OrdinalIgnoreCase)
			)
			{
				identity.BadgeIconPath = "res://assets/textures/client/ui/AdminBadge.png";
				identity.BadgeTooltip = "Administrator";
				return;
			}

			if (
				string.Equals(name, "Verified", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(name, "Verified Account", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(iconName, "Verified.png", StringComparison.OrdinalIgnoreCase)
			)
			{
				identity.BadgeIconPath = "res://assets/textures/client/ui/VerifiedBadge.png";
				identity.BadgeTooltip = "Verified Account (Checkmark)";
			}
		}
	}

	private static string GetUserId(OpenIdUserInfoResponse userInfo)
	{
		if (!string.IsNullOrWhiteSpace(userInfo.Sub))
			return userInfo.Sub;

		return "0";
	}

	private static string GetString(JsonElement root, string propertyName, string fallback = "")
	{
		if (
			!root.TryGetProperty(propertyName, out JsonElement node)
			|| node.ValueKind == JsonValueKind.Null
			|| node.ValueKind == JsonValueKind.Undefined
		)
		{
			return fallback;
		}

		if (node.ValueKind == JsonValueKind.String)
			return node.GetString() ?? fallback;

		return node.ToString();
	}

	private static bool GetBool(JsonElement root, string propertyName, bool fallback = false)
	{
		if (!root.TryGetProperty(propertyName, out JsonElement node))
			return fallback;

		return node.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.String when bool.TryParse(node.GetString(), out bool value) => value,
			_ => fallback,
		};
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

	private static long GetLong(JsonElement root, string propertyName, long fallback = 0)
	{
		if (!root.TryGetProperty(propertyName, out JsonElement node))
			return fallback;

		if (node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out long value))
			return value;

		if (node.ValueKind == JsonValueKind.String && long.TryParse(node.GetString(), out value))
			return value;

		return fallback;
	}

	public static void ClearAuth()
	{
		Token = "";
		UserID = "0";
		Username = "";
		CurrentUserInfo = null;
		CurrentAuthenticatedProfile = null;
		CurrentToolbarIdentity = null;
		IsUserAuthenticated = false;

		_client.DefaultRequestHeaders.Remove("Authorization");
		DeleteStoredToken();
		AuthenticatedProfileUpdated?.Invoke(null);
		ToolbarIdentityUpdated?.Invoke(null);
	}

	public static async Task<CreatorGuildItem[]> GetUserGuilds(bool limitToEditable = true)
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		if (Globals.UseNoHttp)
			throw new HttpRequestException("Http is disabled via feature flag");

		await EnsureTokenValid();

		const int limit = 25;

		List<CreatorGuildItem> allGuilds = [];

		int page = 1;
		int pages = 1;

		do
		{
			List<string> query = [
				$"page={page}",
			$"limit={limit}"
			];

			if (limitToEditable)
				query.Add("editableOnly=true");

			string guildsUrl =
				Globals.ApiEndpoint.PathJoin($"/v3/social/guilds/user/{UserID}") +
				"?" +
				string.Join("&", query);

			using HttpResponseMessage msg = await _client.GetAsync(guildsUrl);
			msg.EnsureSuccessStatusCode();

			using JsonDocument doc = JsonDocument.Parse(await msg.Content.ReadAsStringAsync());

			bool success =
				doc.RootElement.TryGetProperty("success", out JsonElement successNode)
				&& successNode.ValueKind == JsonValueKind.True;

			if (!success)
				break;

			if (
				doc.RootElement.TryGetProperty("guilds", out JsonElement guildsNode)
				&& guildsNode.ValueKind == JsonValueKind.Array
			)
			{
				foreach (JsonElement guild in guildsNode.EnumerateArray())
				{
					allGuilds.Add(new CreatorGuildItem
					{
						Id = GetString(guild, "id"),
						Name = GetString(guild, "name"),
						CanEditWorlds = GetBool(guild, "canEditWorlds"),
					});
				}
			}

			if (
				doc.RootElement.TryGetProperty("pagination", out JsonElement paginationNode)
				&& paginationNode.ValueKind == JsonValueKind.Object
			)
			{
				int nextPages = GetInt(paginationNode, "pages");

				if (nextPages > 0)
					pages = nextPages;
			}

			page++;
		}
		while (page <= pages);

		return [.. allGuilds];
	}
	public static async Task<CreatorPlaceItem[]> GetPublishedWorlds()
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		using HttpResponseMessage msg = await _client.GetAsync(
			Globals.ApiEndpoint.PathJoin("/v3/created-worlds")
		);
		msg.EnsureSuccessStatusCode();

		using JsonDocument doc = JsonDocument.Parse(await msg.Content.ReadAsStringAsync());

		if (
			!doc.RootElement.TryGetProperty("universes", out JsonElement universes)
			|| universes.ValueKind != JsonValueKind.Array
		)
		{
			return [];
		}

		List<CreatorPlaceItem> worlds = [];

		foreach (JsonElement universe in universes.EnumerateArray())
		{
			if (
				!universe.TryGetProperty("worlds", out JsonElement worldArray)
				|| worldArray.ValueKind != JsonValueKind.Array
			)
			{
				continue;
			}

			foreach (JsonElement world in worldArray.EnumerateArray())
			{
				worlds.Add(
					new CreatorPlaceItem
					{
						Id =
							world.TryGetProperty("id", out JsonElement idNode)
							&& int.TryParse(idNode.GetString(), out int id)
								? id
								: 0,

						UniverseId =
							universe.TryGetProperty("id", out JsonElement idNode2)
							&& int.TryParse(idNode2.GetString(), out int UniverseId)
								? UniverseId
								: 0,

						WorldId =
							world.TryGetProperty("id", out JsonElement idNode3)
							&& int.TryParse(idNode3.GetString(), out int WorldId)
								? WorldId
								: 0,

						Name = world.TryGetProperty("name", out JsonElement nameNode)
							? nameNode.GetString() ?? ""
							: "",

						CreatedAt =
							world.TryGetProperty("createdAt", out JsonElement createdNode)
							&& createdNode.ValueKind == JsonValueKind.String
							&& DateTime.TryParse(createdNode.GetString(), out DateTime createdAt)
								? createdAt
								: DateTime.UtcNow,

						UpdatedAt =
							world.TryGetProperty("updatedAt", out JsonElement updatedNode)
							&& updatedNode.ValueKind == JsonValueKind.String
							&& DateTime.TryParse(updatedNode.GetString(), out DateTime updatedAt)
								? updatedAt
								: null,

						IconUrl = "",
					}
				);
			}
		}

		return [.. worlds];
	}

	private static StringContent FormString(string name, string value)
	{
		StringContent content = new(value, Encoding.UTF8, "text/plain");
		content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
		{
			Name = $"\"{name}\"",
		};
		return content;
	}

	private static ByteArrayContent FormFile(string name, string fileName, byte[] data)
	{
		ByteArrayContent content = new(data);
		content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
		{
			Name = $"\"{name}\"",
			FileName = $"\"{fileName}\"",
		};
		return content;
	}

	public static async Task<CreatorPublishResponse> UploadWorld(
		byte[] placeData,
		long? universeId = 0,
		long? worldId = 0,
		bool publish = true,
		string? creationOwnerId = null,
		string? creationOwnerType = "USER"
	)
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		long resolvedUniverseId = universeId ?? 0;
		long resolvedWorldId = worldId ?? 0;
		bool isNewUniverse = resolvedUniverseId == 0;

		using MultipartContent form = new("form-data", Guid.NewGuid().ToString());

		form.Add(FormString("universeId", resolvedUniverseId.ToString()));
		form.Add(FormString("worldId", resolvedWorldId.ToString()));
		form.Add(FormString("publish", publish ? "true" : "false"));

		if (isNewUniverse)
		{
			if (string.IsNullOrWhiteSpace(creationOwnerId))
			{
				throw new ArgumentException("ownerId is required when creating a new universe.", nameof(creationOwnerId));
			}

			if (string.IsNullOrWhiteSpace(creationOwnerType))
			{
				throw new ArgumentException("ownerType is required when creating a new universe.", nameof(creationOwnerType));
			}

			string ownerId = creationOwnerId.Trim();
			string ownerType = creationOwnerType.Trim().ToLowerInvariant();

			if (ownerType != "user" && ownerType != "guild")
			{
				throw new ArgumentOutOfRangeException(
					nameof(creationOwnerType),
					creationOwnerType,
					"ownerType must be USER or GUILD when creating a new universe."
				);
			}

			form.Add(FormString("ownerId", ownerId));
			form.Add(FormString("ownerType", ownerType));
		}

		form.Add(FormFile("file", "level.packed", placeData));

		string url = Globals.ApiEndpoint.PathJoin("/v3/world/editor/tree");

		using HttpRequestMessage request = new(HttpMethod.Post, url)
		{
			Content = form,
		};

		request.Headers.TryAddWithoutValidation("User-Agent", $"BrickVerse Client {Globals.AppVersion}");
		request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Token);
		request.Headers.TryAddWithoutValidation("Cookie", "auth_token=" + Uri.EscapeDataString(Token));
		request.Headers.TryAddWithoutValidation("Accept", "application/json");

		PT.Print($"CreatorAPI UploadWorld Content-Type: {form.Headers.ContentType}");
		PT.Print($"CreatorAPI UploadWorld Raw File Length: {placeData.Length}");

		using HttpResponseMessage msg = await _uploadClient.SendAsync(request);
		string responseText = await msg.Content.ReadAsStringAsync();

		PT.Print($"CreatorAPI UploadWorld Response Status: {(int)msg.StatusCode} {msg.StatusCode}");
		PT.Print($"CreatorAPI UploadWorld Response Body: {responseText}");

		if (!msg.IsSuccessStatusCode)
		{
			string message = responseText;

			try
			{
				using JsonDocument errorDoc = JsonDocument.Parse(responseText);
				message = GetString(errorDoc.RootElement, "message");
			}
			catch
			{
				// keep raw responseText
			}

			throw new HttpRequestException(
				$"CreatorAPI: Upload world failed: {(int)msg.StatusCode} {msg.StatusCode}: {message}"
			);
		}

		using JsonDocument doc = JsonDocument.Parse(responseText);
		JsonElement root = doc.RootElement;

		bool success =
			root.TryGetProperty("success", out JsonElement successNode)
			&& successNode.ValueKind == JsonValueKind.True;

		string nextWorldId = GetString(root, "worldId");
		string nextUniverseId = GetString(root, "universeId");

		if (string.IsNullOrWhiteSpace(nextWorldId))
			nextWorldId = resolvedWorldId.ToString();

		if (string.IsNullOrWhiteSpace(nextUniverseId))
			nextUniverseId = resolvedUniverseId.ToString();

		return new CreatorPublishResponse
		{
			Success = success,
			WorldId = long.Parse(nextWorldId),
			UniverseId = long.Parse(nextUniverseId),
			Link = Globals.MainEndpoint.PathJoin("/worlds/" + nextWorldId),
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
		form.Add(fileContent, "file", "model.bvxm");

		using HttpResponseMessage msg = await _client.PostAsync(
			Globals.ApiEndpoint.PathJoin("/v3/asset/create"),
			form
		);

		msg.EnsureSuccessStatusCode();

		using JsonDocument createdDoc = JsonDocument.Parse(await msg.Content.ReadAsStringAsync());

		string assetId = createdDoc.RootElement.TryGetProperty(
			"assetId",
			out JsonElement assetIdNode
		)
			? assetIdNode.GetString() ?? ""
			: "";

		return new CreatorPublishResponse
		{
			Link =
				assetId.Length == 0
					? Globals.MainEndpoint.PathJoin("/creator")
					: Globals.MainEndpoint.PathJoin("/asset/" + assetId),
		};
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
		return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	private static byte[] Base64UrlDecode(string value)
	{
		string normalized = value.Replace('-', '+').Replace('_', '/');

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
			return new OpenIdAuthSession { AccessToken = NormalizeToken(raw) };
		}

		using JsonDocument doc = JsonDocument.Parse(raw);
		JsonElement root = doc.RootElement;

		string accessToken = GetString(root, "access_token");

		if (string.IsNullOrWhiteSpace(accessToken))
			accessToken = GetString(root, "accessToken");

		long expiresAt = GetLong(root, "expires_at");

		PT.Print($"CreatorAPI: Loaded stored session - AccessToken valid: {!string.IsNullOrWhiteSpace(accessToken)}, HasRefreshToken: {!string.IsNullOrWhiteSpace(GetString(root, "refresh_token"))}, ExpiresAt: {expiresAt}");

		return new OpenIdAuthSession
		{
			AccessToken = NormalizeToken(accessToken),
			RefreshToken = GetString(root, "refresh_token"),
			IdToken = GetString(root, "id_token"),
			ExpiresAt = expiresAt,
		};
	}

	private static void SaveStoredSession(OpenIdAuthSession session)
	{
		using FileAccess f = FileAccess.Open(StoredTokenPath, FileAccess.ModeFlags.Write);

		string json =
			"{"
			+ $"\"access_token\":\"{EscapeJson(NormalizeToken(session.AccessToken))}\","
			+ $"\"refresh_token\":\"{EscapeJson(session.RefreshToken)}\","
			+ $"\"id_token\":\"{EscapeJson(session.IdToken)}\","
			+ $"\"expires_at\":{session.ExpiresAt}"
			+ "}";

		f.StoreString(json);
	}

	private static string EscapeJson(string value)
	{
		return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}

	private static void DeleteStoredToken()
	{
		if (FileAccess.FileExists(StoredTokenPath))
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(StoredTokenPath));
	}
}

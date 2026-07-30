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
using System.Threading;
using System.Threading.Tasks;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Utils;
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
			//BV.Print("CreatorAPI: Attempting to restore auth session from storage...");

			try
			{
				// Check if token is expired and needs refresh
				if (IsTokenExpired(storedSession))
				{
					//BV.Print("CreatorAPI: Stored token is expired, attempting refresh...");

					if (!string.IsNullOrWhiteSpace(storedSession.RefreshToken))
					{
						storedSession = await RefreshAccessToken(storedSession);
						if (storedSession == null)
						{
							BV.PrintErr(
								"CreatorAPI: Failed to refresh expired token, prompting login"
							);
							ClearAuth();
							await PromptLogin();
							return;
						}
					}
					else
					{
						// No refresh token and access token expired
						BV.PrintErr(
							"CreatorAPI: Token expired and no refresh token available, prompting login"
						);
						ClearAuth();
						await PromptLogin();
						return;
					}
				}

				//BV.Print("CreatorAPI: Restoring session from stored token");
				await LoginWithOpenIdSession(storedSession, saveToken: false);

				return;
			}
			catch (Exception error)
			{
				BV.PrintErr("CreatorAPI: Failed to restore auth session: ", error.Message);
				ClearAuth();
			}
		}
		else
		{
			BV.Print("CreatorAPI: No stored session found or session is invalid");
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
			_client.DefaultRequestHeaders["Cookie"] = "auth_token=" + Token;
		}

		BVAPI.SetAuthToken(Token);
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

			var content = new FormUrlEncodedContent(
				new[]
				{
					new KeyValuePair<string, string>("grant_type", "refresh_token"),
					new KeyValuePair<string, string>("refresh_token", session.RefreshToken),
					new KeyValuePair<string, string>("client_id", OpenIDClientId),
				}
			);

			using HttpResponseMessage msg = await _client.PostAsync(oidc.TokenEndpoint, content);
			string body = await msg.Content.ReadAsStringAsync();

			if (!msg.IsSuccessStatusCode)
			{
				BV.PrintErr($"CreatorAPI: Token refresh failed: {msg.StatusCode} {body}");
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

			//BV.Print("CreatorAPI: Token successfully refreshed");

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
			BV.PrintErr("CreatorAPI: Exception during token refresh: ", error.Message);
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
		Uri? discoveredUserInfoUri = Uri.TryCreate(
			userInfoEndpoint,
			UriKind.Absolute,
			out Uri? parsedUserInfo
		)
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
			BV.Print(
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

		PendingIdToken = null;
		CurrentUserInfo = userInfo;

		(string userId, string username) = await GetAuthenticatedUserIdentity(userInfo);
		UserID = userId;
		Username = username;
		IsUserAuthenticated = true;

		//BV.Print($"CreatorAPI: User authenticated as {Username} ({UserID})");

		UserAuthenticated?.Invoke(userInfo);
		UpdateAuthenticatedProfile(userInfo);
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
				if (
					expNode.ValueKind == JsonValueKind.Number
					&& expNode.TryGetInt64(out long expValue)
				)
					return expValue;

				if (
					expNode.ValueKind == JsonValueKind.String
					&& long.TryParse(expNode.GetString(), out long parsedExp)
				)
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

		//BV.Print($"OpenID userinfo response: {body}");

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
					//BV.Print("CreatorAPI: Token automatically refreshed before API call");
				}
			}
		}
	}

	private static void UpdateAuthenticatedProfile(OpenIdUserInfoResponse userInfo)
	{
		if (!IsValidUserInfo(userInfo))
		{
			CurrentAuthenticatedProfile = null;
			AuthenticatedProfileUpdated?.Invoke(null);
			return;
		}

		string resolvedUsername = !string.IsNullOrWhiteSpace(userInfo.PreferredUsername)
			? userInfo.PreferredUsername
			: userInfo.Name;

		string? headshot = !string.IsNullOrWhiteSpace(userInfo.HeadshotUrl)
			? userInfo.HeadshotUrl
			: userInfo.Picture;

		AuthenticatedUserProfile profile = new()
		{
			Username = resolvedUsername,
			HeadshotUrl = headshot,
			IsModerator = false,
			IsVerified = false,
		};

		CurrentAuthenticatedProfile = profile;
		AuthenticatedProfileUpdated?.Invoke(profile);
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
			BV.PrintErr("CreatorAPI: Failed to load toolbar identity: ", error.Message);
		}
	}

	public static async Task RefreshToolbarIdentityAsync()
	{
		await RefreshToolbarIdentity();
	}

	public static async Task SwitchAccount()
	{
		ClearAuth();
		await PromptLogin();
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

	private static async Task<(string UserId, string Username)> GetAuthenticatedUserIdentity(
		OpenIdUserInfoResponse userInfo
	)
	{
		string username = !string.IsNullOrWhiteSpace(userInfo.PreferredUsername)
			? userInfo.PreferredUsername
			: userInfo.Name;

		if (string.IsNullOrWhiteSpace(username))
			throw new InvalidOperationException(
				"CreatorAPI: OpenID session did not include a usable username."
			);

		//BV.Print($"CreatorAPI: Resolving creator identity for username '{username}'");

		string lookupUrl =
			Globals.ApiEndpoint.PathJoin("/v3/users/lookup")
			+ "?username="
			+ Uri.EscapeDataString(username);

		using HttpResponseMessage msg = await _client.GetAsync(lookupUrl);
		string body = await msg.Content.ReadAsStringAsync();

		if (!msg.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(
				$"CreatorAPI: Failed to resolve authenticated user: {(int)msg.StatusCode} {msg.StatusCode}: {body}"
			);
		}

		using JsonDocument doc = JsonDocument.Parse(body);

		if (
			!doc.RootElement.TryGetProperty("success", out JsonElement successNode)
			|| successNode.ValueKind != JsonValueKind.True
		)
		{
			throw new InvalidOperationException(
				"CreatorAPI: user lookup did not return a successful response."
			);
		}

		if (
			!doc.RootElement.TryGetProperty("user", out JsonElement user)
			|| user.ValueKind != JsonValueKind.Object
		)
		{
			throw new InvalidOperationException(
				"CreatorAPI: user lookup did not include a user object."
			);
		}

		string userId = GetString(user, "id");
		string resolvedUsername = GetString(user, "username", username);

		if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(resolvedUsername))
		{
			throw new InvalidOperationException(
				"CreatorAPI: user lookup did not include a valid user id or username."
			);
		}

		//BV.Print($"CreatorAPI: Resolved creator identity '{resolvedUsername}' -> {userId}");

		return (userId, resolvedUsername);
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
		_client.DefaultRequestHeaders.Remove("Cookie");
		BVAPI.SetAuthToken("");
		DeleteStoredToken();
		AuthenticatedProfileUpdated?.Invoke(null);
		ToolbarIdentityUpdated?.Invoke(null);
	}

	public static async Task<CreatorGuildItem[]> GetUserGuilds(
		bool limitToEditable = false,
		CancellationToken cancellationToken = default
	)
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		if (Globals.UseNoHttp)
			throw new HttpRequestException("HTTP is disabled via feature flag");

		await EnsureTokenValid();

		const int limit = 25;

		List<CreatorGuildItem> allGuilds = [];
		int page = 1;

		while (true)
		{
			string guildsUrl =
				Globals.ApiEndpoint.PathJoin($"/v3/social/guilds/user/{UserID}")
				+ $"?page={page}&limit={limit}"
				+ (limitToEditable ? "&editableOnly=true" : string.Empty);

			using HttpResponseMessage response = await _client.GetAsync(guildsUrl);

			string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				throw new HttpRequestException(
					$"Failed to retrieve user guilds. "
						+ $"Status: {(int)response.StatusCode} {response.StatusCode}. "
						+ $"Response: {responseBody}",
					null,
					response.StatusCode
				);
			}

			CreatorGuildResponse? result;

			try
			{
				result = JsonSerializer.Deserialize(
					responseBody,
					CreatorAPIGenerationContext.Default.CreatorGuildResponse
				);
			}
			catch (JsonException ex)
			{
				throw new HttpRequestException("The guild response contained invalid JSON.", ex);
			}

			if (result is null)
				throw new HttpRequestException("The guild response was empty.");

			if (!result.Success)
				throw new HttpRequestException("The API failed to retrieve user guilds.");

			if (result.Guilds.Length == 0)
				break;

			foreach (CreatorGuildItem guild in result.Guilds)
			{
				if (string.IsNullOrWhiteSpace(guild.Id))
					continue;

				if (limitToEditable && !guild.CanEditWorlds)
					continue;

				allGuilds.Add(guild);
			}

			/*
			 * The supplied API response has no pagination object. In that case,
			 * receiving fewer than the requested limit means this is the final page.
			 *
			 * If a pagination object is later returned, use its Pages value.
			 */
			if (result.Pagination is not null)
			{
				if (page >= result.Pagination.Pages)
					break;
			}
			else if (result.Guilds.Length < limit)
			{
				break;
			}
			else
			{
				/*
				 * Prevent requesting page 2 forever if the endpoint currently ignores
				 * pagination and repeatedly returns exactly `limit` records.
				 */
				break;
			}

			page++;
		}

		BV.Print($"CreatorAPI: Retrieved {allGuilds.Count} guild(s) for user '{Username}' ({UserID})");

		return [.. allGuilds];
	}

	public static async Task<CreatorPlaceItem[]> GetPublishedWorlds()
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		BV.Print($"CreatorAPI: Loading published worlds for user '{Username}' ({UserID})");
		CreatorPlaceItem[] publishedWorlds = await GetCreatedWorldsFromCreatedWorldsEndpoint();
		BV.Print(
			$"CreatorAPI: Published worlds endpoint returned {publishedWorlds.Length} item(s)"
		);
		return publishedWorlds;
	}

	public static async Task<CreatorPlaceItem[]> GetCreatedWorlds()
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		BV.Print($"CreatorAPI: Loading publish-as worlds for user '{Username}' ({UserID})");
		CreatorPlaceItem[] createdWorlds = await GetCreatedWorldsFromUserGamesEndpoint();
		BV.Print(
			$"CreatorAPI: Publish-as user-games endpoint returned {createdWorlds.Length} world(s)"
		);
		if (createdWorlds.Length > 0)
			return createdWorlds;

		BV.Print("CreatorAPI: Falling back to /v3/created-worlds for publish-as targets");
		return await GetCreatedWorldsFromCreatedWorldsEndpoint();
	}

	private static CreatorAssetItem ParseCreatorAssetItem(JsonElement asset)
	{
		return new CreatorAssetItem
		{
			Id = GetLong(asset, "id"),
			Name = GetString(asset, "name"),
			Description = GetString(asset, "description"),
			Type = GetString(asset, "assetType"),
			CreatorType = GetString(asset, "creatorType"),
			CreatedAt = DateTime.TryParse(GetString(asset, "createdAt"), out DateTime createdAt)
				? createdAt
				: DateTime.UtcNow,
			UpdatedAt = DateTime.TryParse(GetString(asset, "updatedAt"), out DateTime updatedAt)
				? updatedAt
				: null,
			IconUrl = GetString(asset, "textureUrl"),
		};
	}

	public static async Task<CreatorAssetItem[]> GetCreatorAssets(
		UI.Popups.PublishPopup.PublishTypeEnum assetType,
		int? cursor = null
	)
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		using HttpResponseMessage msg = await _client.GetAsync(
			Globals.ApiEndpoint.PathJoin(
				"/v3/assets?limit=50&type="
					+ assetType.ToString().ToUpper()
					+ (cursor.HasValue ? "&cursor=" + cursor.Value : "")
			)
		);
		string body = await msg.Content.ReadAsStringAsync();

		if (!msg.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(
				$"CreatorAPI: Failed to load created assets: {(int)msg.StatusCode} {msg.StatusCode}: {body}"
			);
		}

		using JsonDocument doc = JsonDocument.Parse(body);

		if (
			!doc.RootElement.TryGetProperty("assets", out JsonElement assets)
			|| assets.ValueKind != JsonValueKind.Array
		)
		{
			BV.PrintErr("CreatorAPI: /v3/assets response did not include an assets array");
			return [];
		}

		List<CreatorAssetItem> assetList = [];

		foreach (JsonElement asset in assets.EnumerateArray())
		{
			assetList.Add(ParseCreatorAssetItem(asset));
		}

		return [.. assetList];
	}

	private static async Task<CreatorPlaceItem[]> GetCreatedWorldsFromCreatedWorldsEndpoint()
	{
		BV.Print("CreatorAPI: Requesting /v3/created-worlds");
		using HttpResponseMessage msg = await _client.GetAsync(
			Globals.ApiEndpoint.PathJoin("/v3/created-worlds")
		);
		string body = await msg.Content.ReadAsStringAsync();

		if (!msg.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(
				$"CreatorAPI: Failed to load created worlds: {(int)msg.StatusCode} {msg.StatusCode}: {body}"
			);
		}

		using JsonDocument doc = JsonDocument.Parse(body);

		if (
			!doc.RootElement.TryGetProperty("universes", out JsonElement universes)
			|| universes.ValueKind != JsonValueKind.Array
		)
		{
			BV.PrintErr(
				"CreatorAPI: /v3/created-worlds response did not include a universes array"
			);
			return [];
		}

		List<CreatorPlaceItem> worlds = [];

		foreach (JsonElement universe in universes.EnumerateArray())
		{
			AppendWorldsFromUniverse(worlds, universe);
		}

		return [.. worlds];
	}

	private static void AppendWorldsFromUniverse(
		List<CreatorPlaceItem> worlds,
		JsonElement universe
	)
	{
		if (
			!universe.TryGetProperty("worlds", out JsonElement worldArray)
			|| worldArray.ValueKind != JsonValueKind.Array
		)
		{
			return;
		}

		long universeId = GetLong(universe, "id");
		BV.Print(
			$"CreatorAPI: Inspecting universe {universeId} with {worldArray.GetArrayLength()} world(s)"
		);

		foreach (JsonElement world in worldArray.EnumerateArray())
		{
			long worldId = GetLong(world, "id");
			string worldName = GetString(world, "name");

			if (universeId == 0 || worldId == 0)
			{
				BV.Print(
					$"CreatorAPI: Skipping universe/world entry because ids were invalid (universeId={universeId}, worldId={worldId}, name='{worldName}')"
				);
				continue;
			}

			worlds.Add(
				new CreatorPlaceItem
				{
					Id = worldId,
					UniverseId = universeId,
					WorldId = worldId,
					Name = GetString(world, "name"),
					CreatedAt = DateTime.TryParse(
						GetString(world, "createdAt"),
						out DateTime createdAt
					)
						? createdAt
						: DateTime.UtcNow,
					UpdatedAt = DateTime.TryParse(
						GetString(world, "updatedAt"),
						out DateTime updatedAt
					)
						? updatedAt
						: null,
					IconUrl = "",
				}
			);

			BV.Print(
				$"CreatorAPI: Added published world '{worldName}' worldId={worldId} universeId={universeId}"
			);
		}
	}

	private static async Task<CreatorPlaceItem[]> GetCreatedWorldsFromUserGamesEndpoint()
	{
		string requestUrl = Globals.ApiEndpoint.PathJoin($"/v3/worlds/user/{UserID}");
		BV.Print($"CreatorAPI: Requesting {requestUrl}");

		using HttpResponseMessage msg = await _client.GetAsync(requestUrl);
		string body = await msg.Content.ReadAsStringAsync();

		if (!msg.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(
				$"CreatorAPI: Failed to load user games for Publish As: {(int)msg.StatusCode} {msg.StatusCode}: {body}"
			);
		}

		using JsonDocument doc = JsonDocument.Parse(body);

		if (
			!doc.RootElement.TryGetProperty("games", out JsonElement games)
			|| games.ValueKind != JsonValueKind.Array
		)
		{
			BV.Print("CreatorAPI: /v3/worlds/user/{userId} response did not include a games array");
			return [];
		}

		List<CreatorPlaceItem> worlds = [];
		int skippedGames = 0;

		foreach (JsonElement game in games.EnumerateArray())
		{
			if (!long.TryParse(GetString(game, "id"), out long worldId) || worldId == 0)
			{
				skippedGames++;
				continue;
			}

			try
			{
				APIPlaceInfo worldInfo = await BVAPI.GetWorldFromID(worldId);

				worlds.Add(
					new CreatorPlaceItem
					{
						Id = worldInfo.Id,
						WorldId = worldInfo.Id,
						UniverseId = worldInfo.UniverseId,
						Name = string.IsNullOrWhiteSpace(worldInfo.Name)
							? GetString(game, "name")
							: worldInfo.Name,
						CreatedAt = worldInfo.CreatedAt,
						UpdatedAt = worldInfo.UpdatedAt,
						IconUrl = GetString(game, "thumbnailUrl"),
					}
				);
			}
			catch (Exception ex)
			{
				skippedGames++;
				BV.PrintErr(
					$"CreatorAPI: Skipping world {worldId} while loading Publish As targets: {ex.Message}"
				);
			}
		}

		BV.Print(
			$"CreatorAPI: /v3/worlds/user/{UserID} produced {worlds.Count} publish-as target(s) with {skippedGames} skipped item(s)"
		);

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

	public static async Task<byte[]> DownloadWorld(string worldId)
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		string url = Globals.ApiEndpoint.PathJoin(
			$"/v3/world/editor/tree?worldId={worldId}&stream=true"
		);

		using HttpResponseMessage msg = await _client.GetAsync(url);

		if (!msg.IsSuccessStatusCode)
		{
			string responseText = await msg.Content.ReadAsStringAsync();
			throw new HttpRequestException(
				$"CreatorAPI: Download world failed: {(int)msg.StatusCode} {msg.StatusCode}: {responseText}"
			);
		}

		return await msg.Content.ReadAsByteArrayAsync();
	}

	public static async Task UploadMesh(
		byte[] meshData,
		string fileName,
		string name,
		string description,
		string ownerId,
		UI.Popups.UploadMeshPopup.MeshOwnerType ownerType
	)
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		using MultipartFormDataContent form = new(Guid.NewGuid().ToString());

		form.Add(FormString("assetType", "MESH"));
		form.Add(FormString("name", name));
		form.Add(FormString("description", description));
		form.Add(FormString("ownerType", ownerType.ToString()));
		form.Add(FormString("ownerId", ownerId.ToString()));
		form.Add(FormFile("file", fileName, meshData));

		string url = Globals.ApiEndpoint.PathJoin("/v3/asset/create");
		using HttpResponseMessage msg = await _client.PostAsync(url, form);
		string responseText = await msg.Content.ReadAsStringAsync();

		if (!msg.IsSuccessStatusCode)
		{
			throw new HttpRequestException(
				$"CreatorAPI: Upload mesh failed: {(int)msg.StatusCode} {msg.StatusCode}: {responseText}"
			);
		}

		if (responseText.Contains("error", StringComparison.OrdinalIgnoreCase))
		{
			throw new HttpRequestException(
				$"CreatorAPI: Upload mesh failed: {(int)msg.StatusCode} {msg.StatusCode}: {responseText}"
			);
		}
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

		using MultipartFormDataContent form = new(Guid.NewGuid().ToString());

		form.Add(FormString("universeId", resolvedUniverseId.ToString()));
		form.Add(FormString("worldId", resolvedWorldId.ToString()));
		form.Add(FormString("publish", publish ? "true" : "false"));

		if (isNewUniverse)
		{
			if (string.IsNullOrWhiteSpace(creationOwnerId))
			{
				throw new ArgumentException(
					"ownerId is required when creating a new universe.",
					nameof(creationOwnerId)
				);
			}

			if (string.IsNullOrWhiteSpace(creationOwnerType))
			{
				throw new ArgumentException(
					"ownerType is required when creating a new universe.",
					nameof(creationOwnerType)
				);
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

		using HttpResponseMessage msg = await _client.PostAsync(url, form);
		string responseText = await msg.Content.ReadAsStringAsync();

		//BV.Print($"CreatorAPI UploadWorld Response Status: {(int)msg.StatusCode} {msg.StatusCode}");
		//BV.Print($"CreatorAPI UploadWorld Response Body: {responseText}");

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

	public static async Task<CreatorPublishResponse> UploadAsset(
		byte[] assetData,
		long assetId = 0,
		string assetType = "PREFAB"
	)
	{
		if (!IsUserAuthenticated)
			throw new AuthenticationException("User authentication required");

		// If the assetId is 0, we are creating a new asset.
		if (assetId == 0)
		{
			using MultipartFormDataContent form = new()
			{
				{ new StringContent("studio-upload"), "captchaToken" },
				{ new StringContent(UserID), "ownerId" },
				{ new StringContent("USER"), "ownerType" },
				{ new StringContent(assetType), "assetType" },
			};

			ByteArrayContent fileContent = new(assetData);
			fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
			form.Add(fileContent, "file", "asset.bvxm");

			using HttpResponseMessage msg = await _client.PostAsync(
				Globals.ApiEndpoint.PathJoin("/v3/asset/create"),
				form
			);

			msg.EnsureSuccessStatusCode();

			using JsonDocument createdDoc = JsonDocument.Parse(
				await msg.Content.ReadAsStringAsync()
			);

			string newAssetId = createdDoc.RootElement.TryGetProperty(
				"assetId",
				out JsonElement assetIdNode
			)
				? assetIdNode.GetString() ?? ""
				: "";

			return new CreatorPublishResponse
			{
				Link =
					newAssetId.Length == 0
						? Globals.MainEndpoint.PathJoin("/creator")
						: Globals.MainEndpoint.PathJoin("/asset/" + newAssetId),
			};
		}
		else
		{
			// Otherwise, we are updating an existing asset
			using MultipartFormDataContent form = new()
			{
				{ new StringContent(assetId.ToString()), "assetId" },
				{ new StringContent("studio-upload"), "captchaToken" },
			};

			ByteArrayContent fileContent = new(assetData);
			fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
			form.Add(fileContent, "file", "asset.bvxm");

			using HttpResponseMessage msg = await _client.PostAsync(
				Globals.ApiEndpoint.PathJoin("/v3/asset/publish"),
				form
			);

			msg.EnsureSuccessStatusCode();

			using JsonDocument createdDoc = JsonDocument.Parse(
				await msg.Content.ReadAsStringAsync()
			);

			string newAssetId = createdDoc.RootElement.TryGetProperty(
				"assetId",
				out JsonElement assetIdNode
			)
				? assetIdNode.GetString() ?? ""
				: "";

			return new CreatorPublishResponse
			{
				Link =
					newAssetId.Length == 0
						? Globals.MainEndpoint.PathJoin("/creator")
						: Globals.MainEndpoint.PathJoin("/asset/" + newAssetId),
			};
		}
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

		//BV.Print($"CreatorAPI: Loaded stored session - AccessToken valid: {!string.IsNullOrWhiteSpace(accessToken)}, HasRefreshToken: {!string.IsNullOrWhiteSpace(GetString(root, "refresh_token"))}, ExpiresAt: {expiresAt}");

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

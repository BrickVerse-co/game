// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Client.WebAPI.Interfaces;
#if CREATOR
using BrickVerse.Creator.Utils;
#endif
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

/// <summary>
/// Client, server, and desktop authentication API.
///
/// Supports:
/// - Stored auth_token reuse.
/// - Browser deep-link login via brickverse.gg/auth/client -> brickverse://auth/{token}.
/// - Quick sign-in code creation and exchange through /v3/auth/quick-signin/*.
/// - Token validation against /v3/auth/me before persisting or broadcasting auth state.
/// </summary>
public static class PolyAuthAPI
{
	private static readonly PTHttpClient _client = new();
	private const string StoredTokenPath = "user://auth";

	internal static string Token = "";

	internal static IClientConnector? ClientConnector { get; set; }
	internal static IServerListener? ServerListener { get; set; }

	public static event Action<APIV3AuthMeUser>? UserAuthenticated;
	public static event Action<string>? AuthenticationFailed;
	public static event Action<string>? ShowQuickSignInCode;
	public static event Action? AskForAuthentication;

	public static APIV3AuthMeUser? CurrentUserInfo { get; private set; }
	public static string? CurrentToken { get; private set; }

	/// <summary>
	/// Applies the token to every API surface. BrickVerse accepts auth either as
	/// a Bearer token or as a Cookie named auth_token, so PolyAPI sends both.
	/// </summary>
	public static void SetAuthToken(string token)
	{
		Token = NormalizeToken(token);
		PolyAPI.SetAuthToken(Token);
		ClientConnector?.SetToken(Token);
		ServerListener?.SetToken(Token);
#if CREATOR
		PolyCreatorAPI.SetToken(Token);
#endif
	}

	public static async void Setup()
	{
		string? storedToken = LoadStoredToken();
		if (!string.IsNullOrWhiteSpace(storedToken) && await LoginWithAuthToken(storedToken))
		{
			return;
		}

		AskForAuthentication?.Invoke();
	}

	public static void StartBrowserLogin()
	{
		OS.ShellOpen(Globals.MainEndpoint.PathJoin("/auth/client"));
	}

	/// <summary>
	/// Accepts either the raw token or a brickverse://auth/{token} deep link.
	/// </summary>
	public static Task<bool> LoginWithDeepLink(string uriOrToken)
	{
		string token = ExtractTokenFromDeepLink(uriOrToken);
		return LoginWithAuthToken(token);
	}

	/// <summary>
	/// Creates a website-entered quick sign-in code and raises ShowQuickSignInCode.
	/// </summary>
	public static async Task<string?> StartQuickSignInCodeFlow()
	{
		using HttpResponseMessage res = await _client.PostAsync(
			Globals.ApiEndpoint.PathJoin("/v3/auth/quick-signin/create"),
			new StringContent("{}", Encoding.UTF8, "application/json")
		);

		string body = await res.Content.ReadAsStringAsync();
		if (!res.IsSuccessStatusCode)
		{
			AuthenticationFailed?.Invoke($"Quick sign-in create failed: {(int)res.StatusCode} {body}");
			return null;
		}

		string? code = TryReadJsonString(body, "code", "quickSignInCode", "quick_signin_code", "token");
		if (!string.IsNullOrWhiteSpace(code))
		{
			ShowQuickSignInCode?.Invoke(code);
		}
		else
		{
			AuthenticationFailed?.Invoke("Quick sign-in create response did not contain a usable code.");
		}

		return code;
	}

	/// <summary>
	/// Exchanges a quick sign-in token/code for the real auth_token, then validates it.
	/// </summary>
	public static async Task<bool> LoginWithQuickSignInToken(string token, string? state = null)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			AuthenticationFailed?.Invoke("Quick sign-in token was empty.");
			return false;
		}

		string escaped = Uri.EscapeDataString(token.Trim());
		object payload = string.IsNullOrWhiteSpace(state) ? new { } : new { state };

		using HttpResponseMessage res = await _client.PostAsJsonAsync(
			Globals.ApiEndpoint.PathJoin($"/v3/auth/quick-signin/{escaped}/login"),
			payload,
			APIGenerationContext.Default.Object
		);

		string body = await res.Content.ReadAsStringAsync();
		if (!res.IsSuccessStatusCode)
		{
			AuthenticationFailed?.Invoke($"Quick sign-in login failed: {(int)res.StatusCode} {body}");
			return false;
		}

		string? authToken = TryReadAuthTokenFromResponse(res, body);
		if (string.IsNullOrWhiteSpace(authToken))
		{
			AuthenticationFailed?.Invoke("Quick sign-in login response did not contain auth_token.");
			return false;
		}

		return await LoginWithAuthToken(authToken);
	}

	// Backward-compatible name used by older desktop auth UI code.
	public static Task<bool> LoginWithCodeAndState(string code, string state)
		=> LoginWithQuickSignInToken(code, state);

	public static async Task<bool> LoginWithAuthToken(string userToken)
	{
		string token = NormalizeToken(userToken);
		if (string.IsNullOrWhiteSpace(token))
		{
			AuthenticationFailed?.Invoke("Auth token was empty.");
			return false;
		}

		SetAuthToken(token);

		try
		{
			APIV3AuthMeUser me = await PolyAPI.GetCurrentUser();
			if (!IsValidMeResponse(me))
			{
				ClearAuthToken();
				AuthenticationFailed?.Invoke("Authentication failed: /v3/auth/me returned an invalid user response.");
				return false;
			}

			CurrentToken = token;
			CurrentUserInfo = me;
			SaveToken(token);
			UserAuthenticated?.Invoke(me);
			return true;
		}
		catch (Exception ex)
		{
			ClearAuthToken();
			AuthenticationFailed?.Invoke("Authentication failed: " + ex.Message);
			return false;
		}
	}

	public static async Task<bool> ValidateCurrentToken()
	{
		if (string.IsNullOrWhiteSpace(Token))
		{
			return false;
		}

		try
		{
			APIV3AuthMeUser me = await PolyAPI.GetCurrentUser();
			bool valid = IsValidMeResponse(me);
			if (valid)
			{
				CurrentUserInfo = me;
			}
			else
			{
				ClearAuthToken();
			}

			return valid;
		}
		catch
		{
			ClearAuthToken();
			return false;
		}
	}

	public static void ClearAuthToken()
	{
		Token = "";
		CurrentToken = null;
		CurrentUserInfo = null;
		PolyAPI.SetAuthToken("");
		ClientConnector?.SetToken("");
		ServerListener?.SetToken("");
#if CREATOR
		PolyCreatorAPI.SetToken("");
#endif
		DeleteStoredToken();
	}

	public static Task<APIServerStatus> CheckServerStatus()
	{
		if (ClientConnector == null) throw new MissingComponentException("Client Connector component missing");
		return ClientConnector.CheckServerStatus();
	}

	public static Task<APIClientAuthResponseMessage> SendClientConnect()
	{
		if (ClientConnector == null) throw new MissingComponentException("Client Connector component missing");
		return ClientConnector.Connect();
	}

	public static Task<APIServerListenResponse> SendServerListen()
	{
		if (ServerListener == null) throw new MissingComponentException("Server listener component missing");
		return ServerListener.Listen();
	}

	private static bool IsValidMeResponse(APIV3AuthMeUser? me)
	{
		if (!me.HasValue)
			return false;

		return !string.IsNullOrWhiteSpace(me.Value.Id)
			&& !string.IsNullOrWhiteSpace(me.Value.Username);
	}

	private static string NormalizeToken(string token)
	{
		if (string.IsNullOrWhiteSpace(token)) return "";

		string normalized = token.Trim();
		if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized["Bearer ".Length..].Trim();
		}

		return normalized;
	}

	private static string ExtractTokenFromDeepLink(string uriOrToken)
	{
		if (string.IsNullOrWhiteSpace(uriOrToken)) return "";
		string value = uriOrToken.Trim();

		if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme.Equals("brickverse", StringComparison.OrdinalIgnoreCase))
		{
			if (uri.Host.Equals("auth", StringComparison.OrdinalIgnoreCase))
			{
				return Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
			}

			string marker = "/auth/";
			int idx = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
			if (idx >= 0)
			{
				return Uri.UnescapeDataString(value[(idx + marker.Length)..]);
			}
		}

		return Uri.UnescapeDataString(value);
	}

	private static string? TryReadAuthTokenFromResponse(HttpResponseMessage response, string body)
	{
		string? fromJson = TryReadJsonString(body, "auth_token", "authToken", "token", "accessToken", "access_token");
		if (!string.IsNullOrWhiteSpace(fromJson)) return fromJson;

		if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
		{
			foreach (string cookie in cookies)
			{
				string? parsed = TryReadCookie(cookie, "auth_token");
				if (!string.IsNullOrWhiteSpace(parsed)) return parsed;
			}
		}

		return null;
	}

	private static string? TryReadCookie(string setCookieHeader, string cookieName)
	{
		foreach (string part in setCookieHeader.Split(';'))
		{
			string trimmed = part.Trim();
			if (!trimmed.StartsWith(cookieName + "=", StringComparison.OrdinalIgnoreCase)) continue;

			return Uri.UnescapeDataString(trimmed[(cookieName.Length + 1)..]);
		}

		return null;
	}

	private static string? TryReadJsonString(string json, params string[] propertyNames)
	{
		if (string.IsNullOrWhiteSpace(json)) return null;

		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			return TryReadJsonString(doc.RootElement, propertyNames);
		}
		catch
		{
			return null;
		}
	}

	private static string? TryReadJsonString(JsonElement element, params string[] propertyNames)
	{
		foreach (string propertyName in propertyNames)
		{
			if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out JsonElement value))
			{
				if (value.ValueKind == JsonValueKind.String) return value.GetString();
				if (value.ValueKind == JsonValueKind.Number) return value.GetRawText();
			}
		}

		if (element.ValueKind != JsonValueKind.Object) return null;

		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
			{
				string? nested = TryReadJsonString(property.Value, propertyNames);
				if (!string.IsNullOrWhiteSpace(nested)) return nested;
			}
		}

		return null;
	}

	private static string? LoadStoredToken()
	{
		if (!FileAccess.FileExists(StoredTokenPath)) return null;

		using FileAccess access = FileAccess.Open(StoredTokenPath, FileAccess.ModeFlags.Read);
		return access.GetAsText().Trim();
	}

	private static void SaveToken(string token)
	{
		using FileAccess f = FileAccess.Open(StoredTokenPath, FileAccess.ModeFlags.Write);
		f.StoreString(token);
	}

	private static void DeleteStoredToken()
	{
		if (FileAccess.FileExists(StoredTokenPath))
		{
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(StoredTokenPath));
		}
	}
}

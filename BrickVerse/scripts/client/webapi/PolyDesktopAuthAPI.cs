// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
#if CREATOR
using BrickVerse.Creator.Utils;
#endif
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

/// <summary>
/// Desktop (Client + Creator/Workshop) authentication helper.
/// Supports browser login and quick-signin codes per V3 routes.
/// </summary>
public static class PolyDesktopAuthAPI
{
    private static readonly PTHttpClient _client = new();
    private static string _authState = "";

    public static event Action<APIMeResponse>? UserAuthenticated;
    public static event Action<string>? ShowQuickSignInCode; // code to display to user
    public static event Action? AskForAuthentication;

    public static APIMeResponse? CurrentUserInfo { get; private set; }
    public static string? CurrentToken { get; private set; }

    private const string StoredTokenPath = "user://auth_desktop";

    public static async void Setup()
    {
        if (FileAccess.FileExists(StoredTokenPath))
        {
            using FileAccess access = FileAccess.Open(StoredTokenPath, FileAccess.ModeFlags.Read);
            string token = access.GetAsText().Trim();
            access.Close();
            if (!string.IsNullOrWhiteSpace(token))
            {
                await LoginWithAuthToken(token);
                return;
            }
        }

        AskForAuthentication?.Invoke();
    }

    private static void SaveToken(string token)
    {
        using FileAccess f = FileAccess.Open(StoredTokenPath, FileAccess.ModeFlags.Write);
        f.StoreString(token);
    }

    /// <summary>
	/// Opens browser to /auth/client which lets a logged-in web user approve and send the token via brickverse://auth/&lt;token&gt; deep link.
	/// </summary>
	public static void StartBrowserLogin()
    {
        OS.ShellOpen(Globals.MainEndpoint.PathJoin("/auth/client"));
    }

    /// <summary>
    /// Creates a quick-signin code via V3 and emits ShowQuickSignInCode for UI to display.
    /// User enters the code on the website while logged in.
    /// </summary>
    public static async Task StartQuickSignInCodeFlow()
    {
        using HttpResponseMessage res = await _client.PostAsync(
            Globals.ApiEndpoint.PathJoin("/v3/auth/quick-signin/create"),
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        );
        res.EnsureSuccessStatusCode();

        using JsonDocument doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        string? code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
        if (!string.IsNullOrWhiteSpace(code))
        {
            ShowQuickSignInCode?.Invoke(code);
        }
    }

    /// <summary>
    /// Exchange quick-signin code for real auth token (used by mobile and desktop quick flows).
    /// </summary>
    public static async Task LoginWithCodeAndState(string code, string state)
    {
        string escaped = Uri.EscapeDataString(code);
        using HttpResponseMessage quickRes = await _client.PostAsJsonAsync(
            Globals.ApiEndpoint.PathJoin($"/v3/auth/quick-signin/{escaped}/login"),
            new { state },
            APIGenerationContext.Default.Object
        );
        if (quickRes.IsSuccessStatusCode)
        {
            using JsonDocument doc = JsonDocument.Parse(await quickRes.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("token", out JsonElement tokenNode))
            {
                string? token = tokenNode.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await LoginWithAuthToken(token);
                    return;
                }
            }
        }

        // Fallback: treat code as token
        await LoginWithAuthToken(code);
    }

    public static async Task LoginWithAuthToken(string userToken)
    {
        PolyAPI.SetAuthToken(userToken);
#if CREATOR
        PolyCreatorAPI.SetToken(userToken); // also feed creator if in workshop
#endif

        APIMeResponse me = await PolyAPI.GetCurrentUser();

        CurrentToken = userToken;
        CurrentUserInfo = me;
        SaveToken(userToken);

        UserAuthenticated?.Invoke(me);
    }
}

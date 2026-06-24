// Optional compatibility shim. Delete this file once all call sites use PolyAuthAPI directly.

using BrickVerse.Schemas.API;
using System;
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

[Obsolete("PolyDesktopAuthAPI has been merged into PolyAuthAPI. Use PolyAuthAPI directly.")]
public static class PolyDesktopAuthAPI
{
	public static event Action<APIV3AuthMeUser>? UserAuthenticated
	{
		add => PolyAuthAPI.UserAuthenticated += value;
		remove => PolyAuthAPI.UserAuthenticated -= value;
	}

	public static event Action<string>? ShowQuickSignInCode
	{
		add => PolyAuthAPI.ShowQuickSignInCode += value;
		remove => PolyAuthAPI.ShowQuickSignInCode -= value;
	}

	public static event Action? AskForAuthentication
	{
		add => PolyAuthAPI.AskForAuthentication += value;
		remove => PolyAuthAPI.AskForAuthentication -= value;
	}

	public static APIV3AuthMeUser? CurrentUserInfo => PolyAuthAPI.CurrentUserInfo;
	public static string? CurrentToken => PolyAuthAPI.CurrentToken;

	public static void Setup() => PolyAuthAPI.Setup();
	public static void StartBrowserLogin() => PolyAuthAPI.StartBrowserLogin();
	public static Task<string?> StartQuickSignInCodeFlow() => PolyAuthAPI.StartQuickSignInCodeFlow();
	public static Task<bool> LoginWithCodeAndState(string code, string state) => PolyAuthAPI.LoginWithCodeAndState(code, state);
	public static Task<bool> LoginWithAuthToken(string userToken) => PolyAuthAPI.LoginWithAuthToken(userToken);
	public static Task<bool> LoginWithDeepLink(string uriOrToken) => PolyAuthAPI.LoginWithDeepLink(uriOrToken);
}

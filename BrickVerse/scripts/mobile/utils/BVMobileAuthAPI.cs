// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.Utils;

public static class BVMobileAuthAPI
{
	private static readonly BVHttpClient _client = new();
	private static string _authState = "";
	private static CancellationTokenSource? _mobileAuthCancellation;
	private static int _completingQuickSignIn;
	private static int _startingMobileAuth;
	private const int DeviceCodePollIntervalMs = 2_000;

	public static event Action<APIV3AuthMeUser>? UserAuthenticated;
	public static event Action? AskForAuthentication;
	public static Func<string, bool>? InAppBrowserLauncher { private get; set; }

	public static APIV3AuthMeUser CurrentUserInfo { get; private set; }
	public static bool IsAuthenticated { get; private set; }

	private static MobileAuthData _authData;
	private const string AuthDataPath = "user://auth2";

	public static async Task SetupClient(bool authenticate = true)
	{
		try
		{
			IsAuthenticated = false;
			CurrentUserInfo = default;
			_authState = "";
			_authData = new();

			if (FileAccess.FileExists(AuthDataPath))
			{
				using FileAccess access = FileAccess.Open(AuthDataPath, FileAccess.ModeFlags.Read);
				string data = access.GetAsText();
				access.Close();
				MobileAuthData? auth = JsonSerializer.Deserialize(
					data,
					MobileAuthDataGenerationContext.Default.MobileAuthData
				);
				if (auth != null)
				{
					BV.Print("Existing mobile authentication data found.");
					_authData = auth.Value;
					_authState = _authData.PendingState ?? "";
				}
			}

			if (!authenticate)
				return;

			if (string.IsNullOrWhiteSpace(_authData.Token))
				AskForAuthentication?.Invoke();
			else
				await LoginWithAuthToken(_authData.Token!);
		}
		catch (Exception exception)
		{
			// Corrupt storage and platform I/O failures must never escape an
			// async-void startup callback on Android NativeAOT.
			BV.PrintErr("Mobile authentication initialization failed: ", exception);
			OS.Alert(exception.Message, "Unable to verify session");
			BVAPI.SetAuthToken("");
			AskForAuthentication?.Invoke();
		}
	}

	private static void SaveAuthData()
	{
		using FileAccess authData = FileAccess.Open(AuthDataPath, FileAccess.ModeFlags.Write)
			?? throw new System.IO.IOException("Could not open mobile authentication storage.");
		authData.StoreString(
			JsonSerializer.Serialize(
				_authData,
				MobileAuthDataGenerationContext.Default.MobileAuthData
			)
		);
		authData.Close();
	}

	public static void StartMobileAuth() => _ = StartMobileAuthAsync();

	public static void StartMobileAuth(bool register) => _ = StartMobileAuthAsync(register);

	private static async Task StartMobileAuthAsync(bool register = false)
	{
		if (Interlocked.Exchange(ref _startingMobileAuth, 1) != 0)
			return;

		_mobileAuthCancellation?.Cancel();
		_mobileAuthCancellation?.Dispose();
		_mobileAuthCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
		CancellationToken cancellationToken = _mobileAuthCancellation.Token;
		_authState = Guid.NewGuid().ToString();
		_authData.PendingState = _authState;

		try
		{
			SaveAuthData();
			string createUrl = Globals.ApiEndpoint.PathJoin("/v3/auth/quick-signin/create");
			BV.Print("Starting mobile authentication request: ", createUrl);
			using HttpResponseMessage createResponse = await _client.PostAsync(
				createUrl,
				new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
				cancellationToken
			);
			BV.Print(
				"Mobile authentication request returned HTTP ",
				(int)createResponse.StatusCode
			);
			createResponse.EnsureSuccessStatusCode();
			string createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);

			using JsonDocument document = JsonDocument.Parse(createBody);
			if (
				!document.RootElement.TryGetProperty("success", out JsonElement success)
				|| !success.GetBoolean()
				|| !document.RootElement.TryGetProperty("token", out JsonElement tokenNode)
				|| string.IsNullOrWhiteSpace(tokenNode.GetString())
			)
				throw new AuthenticationException("Could not create a mobile sign-in code.");

			string code = tokenNode.GetString()!;
			string authUrl = Globals.MainEndpoint.PathJoin(
				$"/auth/mobile?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(_authState)}&mode={(register ? "signup" : "login")}"
			);
			if (InAppBrowserLauncher?.Invoke(authUrl) != true)
			{
				BV.Print("Opening authentication in the system browser.");
				Error shellOpenResult = OS.ShellOpen(authUrl);
				BV.Print("System browser open result: ", shellOpenResult);
				if (shellOpenResult != Error.Ok)
					BV.PrintWarn(
						"Device reported a non-success browser result even though the URL may have been dispatched: ",
						shellOpenResult
					);
			}
			await PollForQuickSignInAsync(code, DateTime.UtcNow.AddMinutes(5), cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			BV.PrintWarn("Mobile authentication expired or was cancelled.");
			if (!IsAuthenticated)
				AskForAuthentication?.Invoke();
		}
		catch (TaskCanceledException exception)
		{
			BV.PrintErr("Mobile authentication HTTP request timed out: ", exception);
			OS.Alert(
				"BrickVerse could not contact the authentication service within 30 seconds.",
				"Unable to open sign in"
			);
			AskForAuthentication?.Invoke();
		}
		catch (Exception exception)
		{
			// Preserve the inner HttpRequestException/SocketException details in device
			// logs; the top-level message alone only reports "connection abort".
			BV.PrintErr("Mobile authentication could not be started: ", exception);
			OS.Alert(
				$"BrickVerse could not contact the authentication service.\n\n{exception.Message}",
				"Unable to open sign in"
			);
			AskForAuthentication?.Invoke();
		}
		finally
		{
			Interlocked.Exchange(ref _startingMobileAuth, 0);
		}
	}

	public static async void Logout()
	{
		_mobileAuthCancellation?.Cancel();
		try
		{
			using JsonDocument _ = await BVAPI.SendJson(HttpMethod.Post, "/v3/auth/logout");
		}
		catch (Exception exception)
		{
			// Always clear the local credential; an expired/revoked server session is
			// already effectively logged out.
			BV.PrintWarn("Server logout did not complete: ", exception.Message);
		}
		IsAuthenticated = false;
		CurrentUserInfo = default;
		_authData = new();
		if (FileAccess.FileExists(AuthDataPath))
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(AuthDataPath));
		BVAPI.SetAuthToken("");
		AskForAuthentication?.Invoke();
	}

	public static async Task LoginWithCodeAndState(string code, string state)
	{
		if (string.IsNullOrWhiteSpace(code))
		{
			throw new AuthenticationException("Authentication code is required");
		}

		if (
			string.IsNullOrWhiteSpace(_authState)
			|| !string.Equals(state, _authState, StringComparison.Ordinal)
		)
			throw new AuthenticationException(
				"This sign-in link belongs to a different mobile session."
			);

		await CompleteQuickSignInAsync(
			code,
			DateTime.UtcNow.AddMinutes(5),
			_mobileAuthCancellation?.Token ?? CancellationToken.None
		);
	}

	private static async Task PollForQuickSignInAsync(
		string code,
		DateTime expiresAt,
		CancellationToken cancellationToken
	)
	{
		string validateUrl = Globals.ApiEndpoint.PathJoin(
			$"/v3/auth/quick-signin/{Uri.EscapeDataString(code)}/validate"
		);

		while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow < expiresAt)
		{
			if (IsAuthenticated)
				return;
			bool canLogin = false;
			try
			{
				using HttpResponseMessage response = await _client.PostAsync(
					validateUrl,
					new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
					cancellationToken
				);

				if (IsAuthenticated)
					return;

				if (response.IsSuccessStatusCode)
				{
					string body = await response.Content.ReadAsStringAsync(cancellationToken);
					using JsonDocument document = JsonDocument.Parse(body);

					bool expired =
						document.RootElement.TryGetProperty("expired", out JsonElement expiredNode)
						&& expiredNode.GetBoolean();

					canLogin =
						document.RootElement.TryGetProperty(
							"canLogin",
							out JsonElement canLoginNode
						) && canLoginNode.GetBoolean();

					if (expired)
						throw new AuthenticationException(
							"The mobile sign-in code expired. Please try again."
						);

				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (AuthenticationException)
			{
				throw;
			}
			catch (Exception exception)
			{
				// Polling is best-effort. Android may temporarily lose the
				// connection while switching to/from the authentication browser.
				BV.PrintWarn("Quick sign-in poll failed; retrying: ", exception.Message);
			}

			// Once approved, exchange/validation failures belong to the sign-in
			// flow, not the best-effort polling retry loop.
			if (canLogin)
			{
				await CompleteQuickSignInAsync(code, expiresAt, cancellationToken);
				return;
			}

			await Task.Delay(DeviceCodePollIntervalMs, cancellationToken);
		}

		if (!cancellationToken.IsCancellationRequested)
			throw new AuthenticationException("The mobile sign-in code expired. Please try again.");
	}

	private static async Task CompleteQuickSignInAsync(
		string code,
		DateTime expiresAt,
		CancellationToken cancellationToken
	)
	{
		if (Interlocked.Exchange(ref _completingQuickSignIn, 1) != 0)
			return;

		try
		{
			bool tokenExchanged = false;
			string escapedCode = Uri.EscapeDataString(code);
			string loginUrl = Globals.ApiEndpoint.PathJoin(
				$"/v3/auth/quick-signin/{escapedCode}/login"
			);

			while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow < expiresAt)
			{
				try
				{
					using HttpResponseMessage quickSignInResponse = await _client.PostAsync(
						loginUrl,
						new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
						cancellationToken
					);

					if (!quickSignInResponse.IsSuccessStatusCode)
						throw new AuthenticationException(
							$"The mobile sign-in code could not be exchanged (HTTP {(int)quickSignInResponse.StatusCode})."
						);

					using JsonDocument doc = JsonDocument.Parse(
						await quickSignInResponse.Content.ReadAsStringAsync(cancellationToken)
					);

					if (
						doc.RootElement.TryGetProperty("token", out JsonElement tokenNode)
						&& !string.IsNullOrWhiteSpace(tokenNode.GetString())
					)
					{
						tokenExchanged = true;
						await LoginWithAuthToken(tokenNode.GetString()!);
						_authState = "";
						return;
					}

					throw new AuthenticationException(
						"The mobile sign-in code could not be exchanged."
					);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (AuthenticationException)
				{
					throw;
				}
				catch (HttpRequestException exception) when (!tokenExchanged)
				{
					BV.PrintWarn("Quick sign-in exchange failed; retrying: ", exception.Message);
				}
				catch (InvalidOperationException exception) when (!tokenExchanged)
				{
					// Godot/native Android transport can report CantConnect this way.
					BV.PrintWarn("Quick sign-in exchange failed; retrying: ", exception.Message);
				}

				await Task.Delay(DeviceCodePollIntervalMs, cancellationToken);
			}

			throw new AuthenticationException("The mobile sign-in code expired. Please try again.");
		}
		finally
		{
			Interlocked.Exchange(ref _completingQuickSignIn, 0);
		}
	}

	internal static bool IsSessionRejected(Exception exception) =>
		exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized };

	public static async Task LoginWithAuthToken(string userToken)
	{
		IsAuthenticated = false;
		CurrentUserInfo = default;
		BVAPI.SetAuthToken(userToken);
		// A quick-sign-in code can only be exchanged once. Keep its credential
		// even if /auth/me is temporarily unavailable, so startup can retry it.
		_authData.Token = userToken;
		_authData.PendingState = null;
		TrySaveAuthData();

		APIV3AuthMeUser me;
		try
		{
			me = await BVAPI.GetCurrentUser();
		}
		catch (Exception ex)
		{
			BVAPI.SetAuthToken("");
			if (IsSessionRejected(ex))
			{
				_authData = new();
				if (FileAccess.FileExists(AuthDataPath))
					DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(AuthDataPath));
				throw new AuthenticationException("Your session has expired, please log back in again.", ex);
			}

			// Network, server, response parsing, and storage failures do not prove
			// that the session expired. Never delete a credential for these errors.
			throw new InvalidOperationException(
				"Could not verify your session. Please check your connection and reopen the app to retry.", ex);
		}

		_authData.Username = me.Username;
		_authData.UserID = me.Id;
		TrySaveAuthData();
		CurrentUserInfo = me;
		IsAuthenticated = true;
		UserAuthenticated?.Invoke(me);
	}

	private static void TrySaveAuthData()
	{
		try
		{
			SaveAuthData();
		}
		catch (Exception exception)
		{
			BV.PrintWarn("Could not persist mobile authentication: ", exception.Message);
		}
	}
}

[JsonSerializable(typeof(MobileAuthData))]
internal partial class MobileAuthDataGenerationContext : JsonSerializerContext { }

public struct MobileAuthData
{
	[JsonInclude]
	public string Token { get; set; }

	[JsonInclude]
	public string UserID { get; set; }

	[JsonInclude]
	public string Username { get; set; }

	[JsonInclude]
	public string? PendingState { get; set; }
}

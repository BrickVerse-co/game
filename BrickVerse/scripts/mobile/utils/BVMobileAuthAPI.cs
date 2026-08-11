// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;
using System.Net.Http.Json;
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
	private const int DeviceCodePollIntervalMs = 2_000;

	public static event Action<APIV3AuthMeUser>? UserAuthenticated;
	public static event Action? AskForAuthentication;

	public static APIV3AuthMeUser CurrentUserInfo { get; private set; }

	private static MobileAuthData _authData;
	private const string AuthDataPath = "user://auth2";

	public static async void SetupClient()
	{
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
			}
		}

		if (_authData.Token == null)
		{
			AskForAuthentication?.Invoke();
		}
		else
		{
			await LoginWithAuthToken(_authData.Token!);
		}
	}

	private static void SaveAuthData()
	{
		FileAccess authData = FileAccess.Open(AuthDataPath, FileAccess.ModeFlags.Write);
		authData.StoreString(
			JsonSerializer.Serialize(
				_authData,
				MobileAuthDataGenerationContext.Default.MobileAuthData
			)
		);
		authData.Close();
	}

	public static void StartMobileAuth() => _ = StartMobileAuthAsync();

	private static async Task StartMobileAuthAsync()
	{
		_mobileAuthCancellation?.Cancel();
		_mobileAuthCancellation?.Dispose();
		_mobileAuthCancellation = new CancellationTokenSource();
		CancellationToken cancellationToken = _mobileAuthCancellation.Token;
		_authState = Guid.NewGuid().ToString();

		try
		{
			using HttpResponseMessage createResponse = await _client.PostAsync(
				Globals.ApiEndpoint.PathJoin("/v3/auth/quick-signin/create"),
				new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
			);
			createResponse.EnsureSuccessStatusCode();

			using JsonDocument document = JsonDocument.Parse(
				await createResponse.Content.ReadAsStringAsync(cancellationToken)
			);
			if (
				!document.RootElement.TryGetProperty("success", out JsonElement success)
				|| !success.GetBoolean()
				|| !document.RootElement.TryGetProperty("token", out JsonElement tokenNode)
				|| string.IsNullOrWhiteSpace(tokenNode.GetString())
			)
				throw new AuthenticationException("Could not create a mobile sign-in code.");

			string code = tokenNode.GetString()!;
			string authUrl = Globals.MainEndpoint.PathJoin(
				$"/auth/mobile?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(_authState)}"
			);
			OS.ShellOpen(authUrl);
			await PollForQuickSignInAsync(code, DateTime.UtcNow.AddMinutes(5), cancellationToken);
		}
		catch (OperationCanceledException)
		{
			// A newer mobile sign-in attempt superseded this one.
		}
		catch (Exception exception)
		{
			BV.PrintErr("Mobile authentication could not be started: ", exception.Message);
			AskForAuthentication?.Invoke();
		}
	}

	public static async Task LoginWithCodeAndState(string code, string state)
	{
		if (string.IsNullOrWhiteSpace(code))
		{
			throw new AuthenticationException("Authentication code is required");
		}

		if (!string.IsNullOrWhiteSpace(_authState) && !string.Equals(state, _authState, StringComparison.Ordinal))
			throw new AuthenticationException("This sign-in link belongs to a different mobile session.");

		await CompleteQuickSignInAsync(code);
	}

	private static async Task PollForQuickSignInAsync(
		string code,
		DateTime expiresAt,
		CancellationToken cancellationToken
	)
	{
		while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow < expiresAt)
		{
			try
			{
				using HttpResponseMessage validateResponse = await _client.PostAsync(
					Globals.ApiEndpoint.PathJoin(
						$"/v3/auth/quick-signin/{Uri.EscapeDataString(code)}/validate"
					),
					new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
				);

				if (validateResponse.IsSuccessStatusCode)
				{
					using JsonDocument document = JsonDocument.Parse(
						await validateResponse.Content.ReadAsStringAsync(cancellationToken)
					);
					bool expired = document.RootElement.TryGetProperty("expired", out JsonElement expiredNode)
						&& expiredNode.GetBoolean();
					bool canLogin = document.RootElement.TryGetProperty("canLogin", out JsonElement canLoginNode)
						&& canLoginNode.GetBoolean();

					if (expired)
						throw new AuthenticationException("The mobile sign-in code expired. Please try again.");
					if (canLogin)
					{
						await CompleteQuickSignInAsync(code);
						return;
					}
				}
			}
			catch (HttpRequestException)
			{
				// Brief network failures should not cancel an otherwise valid sign-in request.
			}

			await Task.Delay(DeviceCodePollIntervalMs, cancellationToken);
		}

		if (!cancellationToken.IsCancellationRequested)
			throw new AuthenticationException("The mobile sign-in code expired. Please try again.");
	}

	private static async Task CompleteQuickSignInAsync(string code)
	{
		if (Interlocked.Exchange(ref _completingQuickSignIn, 1) != 0)
			return;

		try
		{
			string escapedCode = Uri.EscapeDataString(code);
			using HttpResponseMessage quickSignInResponse = await _client.PostAsync(
				Globals.ApiEndpoint.PathJoin($"/v3/auth/quick-signin/{escapedCode}/login"),
				new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
			);

		if (quickSignInResponse.IsSuccessStatusCode)
		{
			using JsonDocument doc = JsonDocument.Parse(
				await quickSignInResponse.Content.ReadAsStringAsync()
			);
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

			throw new AuthenticationException("The mobile sign-in code could not be exchanged.");
		}
		finally
		{
			Interlocked.Exchange(ref _completingQuickSignIn, 0);
		}
	}

	public static async Task LoginWithAuthToken(string userToken)
	{
		BVAPI.SetAuthToken(userToken);
		try
		{
			APIV3AuthMeUser me = await BVAPI.GetCurrentUser();

			_authData.Username = me.Username;
			_authData.Token = userToken;
			_authData.UserID = me.Id;
			SaveAuthData();
			BV.Print("Hello!! ", me.Username);

			CurrentUserInfo = me;
			UserAuthenticated?.Invoke(me);
		}
		catch (Exception ex)
		{
			_authData = new();
			if (FileAccess.FileExists(AuthDataPath))
				DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(AuthDataPath));
			BVAPI.SetAuthToken("");
			if (OS.IsDebugBuild())
			{
				OS.Alert($"{ex}", "Authentication Failure");
			}
			else
			{
				OS.Alert(
					"Your session has expired, please log back in again.",
					"Authentication Failure"
				);
			}
			AskForAuthentication?.Invoke();
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
}

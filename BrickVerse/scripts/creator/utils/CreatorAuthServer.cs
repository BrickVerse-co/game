// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Creator.Utils;

public static class CreatorAuthServer
{
	private const string CallbackHost = "127.0.0.1";
	private const int CallbackPort = 42424;
	private const string CallbackPath = "/openid/callback/";

	public static string RedirectUri => $"http://{CallbackHost}:{CallbackPort}{CallbackPath}";

	private static readonly object AuthLock = new();

	private static HttpListener? _listener;
	private static CancellationTokenSource? _cts;
	private static bool _running;

	private static string? _expectedState;
	private static string? _codeVerifier;

	public static void StartServer()
	{
		if (_running)
			return;

		_cts = new CancellationTokenSource();
		_listener = new HttpListener();

		_listener.Prefixes.Add($"http://{CallbackHost}:{CallbackPort}{CallbackPath}");

		_running = true;

		_ = Task.Run(() => RunListenerAsync(_cts.Token));

		//BV.Print($"CreatorAuthServer listening on {RedirectUri}");
	}

	public static void BeginAuthAttempt(string expectedState, string codeVerifier)
	{
		if (string.IsNullOrWhiteSpace(expectedState))
			throw new ArgumentException("Expected state cannot be empty.", nameof(expectedState));

		if (string.IsNullOrWhiteSpace(codeVerifier))
			throw new ArgumentException("Code verifier cannot be empty.", nameof(codeVerifier));

		StartServer();

		lock (AuthLock)
		{
			_expectedState = expectedState;
			_codeVerifier = codeVerifier;
		}
	}

	private static async Task RunListenerAsync(CancellationToken cancellationToken)
	{
		try
		{
			_listener!.Start();

			while (_running && !cancellationToken.IsCancellationRequested)
			{
				HttpListenerContext ctx = await _listener.GetContextAsync();

				_ = Task.Run(
					async () =>
					{
						await HandleCallbackAsync(ctx);
					},
					cancellationToken
				);
			}
		}
		catch (HttpListenerException)
		{
			// Expected when listener is stopped.
		}
		catch (ObjectDisposedException)
		{
			// Expected when listener is disposed.
		}
		catch (Exception ex)
		{
			if (_running)
				BV.PrintErr($"CreatorAuthServer error: {ex.Message}", ex);
		}
	}

	private static async Task HandleCallbackAsync(HttpListenerContext ctx)
	{
		try
		{
			SetCorsHeaders(ctx.Response);

			if (ctx.Request.HttpMethod == "OPTIONS")
			{
				ctx.Response.StatusCode = 204;
				ctx.Response.Close();
				return;
			}

			string? code = ctx.Request.QueryString["code"];
			string? token = ctx.Request.QueryString["token"];
			string? idToken = ctx.Request.QueryString["id_token"];
			string? state = ctx.Request.QueryString["state"];
			string? error = ctx.Request.QueryString["error"];
			string? errorDescription = ctx.Request.QueryString["error_description"];

			if (!string.IsNullOrWhiteSpace(error))
			{
				ClearAuthAttempt();

				await WriteHtmlAsync(
					ctx,
					400,
					"BrickVerse Login Failed",
					!string.IsNullOrWhiteSpace(errorDescription)
						? errorDescription
						: "The login request was cancelled or denied."
				);

				return;
			}

			if (string.IsNullOrWhiteSpace(code))
			{
				if (string.IsNullOrWhiteSpace(token))
				{
					await WriteHtmlAsync(
						ctx,
						400,
						"BrickVerse Login Failed",
						"Missing authorization code."
					);
					return;
				}
			}

			string? expectedState;
			string? codeVerifier;

			lock (AuthLock)
			{
				expectedState = _expectedState;
				codeVerifier = _codeVerifier;
			}

			if (string.IsNullOrWhiteSpace(expectedState) || string.IsNullOrWhiteSpace(codeVerifier))
			{
				await WriteHtmlAsync(
					ctx,
					400,
					"BrickVerse Login Failed",
					"No active login request was found."
				);
				return;
			}

			if (string.IsNullOrWhiteSpace(state) || state != expectedState)
			{
				await WriteHtmlAsync(ctx, 400, "BrickVerse Login Failed", "Invalid login state.");
				return;
			}

			if (!string.IsNullOrWhiteSpace(token))
			{
				CreatorAPI.PendingIdToken = idToken;
				await CreatorAPI.LoginWithToken(token, true);
				ClearAuthAttempt();

				//BV.Print("OpenID callback handled successfully - user should be authenticated now.");

				await WriteHtmlAsync(
					ctx,
					200,
					"BrickVerse Login Complete",
					"You may now close this window and return to BrickVerse Studio."
				);
				return;
			}

			string browserCallbackUrl =
				Globals.MainEndpoint.PathJoin("/auth/creator/callback") +
				"?desktop_redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
				"&code=" + Uri.EscapeDataString(code!) +
				"&state=" + Uri.EscapeDataString(state);

			await WriteRedirectAsync(ctx, browserCallbackUrl);
			return;
		}
		catch (Exception ex)
		{
			BV.PrintErr($"OpenID callback error: {ex.Message}", ex);
			ClearAuthAttempt();

			try
			{
				await WriteHtmlAsync(
					ctx,
					500,
					"BrickVerse Login Failed",
					"An internal error occurred."
				);
			}
			catch
			{
				// Ignore response write failures.
			}
		}
	}

	private static void ClearAuthAttempt()
	{
		lock (AuthLock)
		{
			_expectedState = null;
			_codeVerifier = null;
		}
	}

	private static async Task WriteHtmlAsync(
		HttpListenerContext ctx,
		int statusCode,
		string title,
		string message
	)
	{
		SetCorsHeaders(ctx.Response);

		string html = $"""
			<!doctype html>
			<html>
				<head>
					<meta charset="utf-8">
					<meta name="viewport" content="width=device-width, initial-scale=1">
					<title>{WebUtility.HtmlEncode(title)}</title>
				</head>
				<body style="font-family: system-ui, sans-serif; padding: 32px; line-height: 1.5;">
					<h1>{WebUtility.HtmlEncode(title)}</h1>
					<p>{WebUtility.HtmlEncode(message)}</p>
				</body>
			</html>
			""";

		byte[] buffer = Encoding.UTF8.GetBytes(html);

		ctx.Response.StatusCode = statusCode;
		ctx.Response.ContentType = "text/html; charset=utf-8";
		ctx.Response.ContentLength64 = buffer.Length;

		await ctx.Response.OutputStream.WriteAsync(buffer);
		ctx.Response.Close();
	}

	private static async Task WriteRedirectAsync(HttpListenerContext ctx, string location)
	{
		SetCorsHeaders(ctx.Response);
		ctx.Response.StatusCode = 302;
		ctx.Response.RedirectLocation = location;
		ctx.Response.ContentType = "text/html; charset=utf-8";

		byte[] buffer = Encoding.UTF8.GetBytes(
			"""
			<!doctype html>
			<html>
				<head>
					<meta charset="utf-8">
					<meta name="viewport" content="width=device-width, initial-scale=1">
					<title>Redirecting</title>
				</head>
				<body style="font-family: system-ui, sans-serif; padding: 32px; line-height: 1.5;">
					<p>Redirecting...</p>
				</body>
			</html>
			"""
		);

		ctx.Response.ContentLength64 = buffer.Length;
		await ctx.Response.OutputStream.WriteAsync(buffer);
		ctx.Response.Close();
	}

	private static void SetCorsHeaders(HttpListenerResponse response)
	{
		response.AddHeader("Access-Control-Allow-Origin", "*");
		response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
		response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");
	}

	public static void Stop()
	{
		if (!_running)
			return;

		_running = false;

		_cts?.Cancel();
		_listener?.Stop();
		_listener?.Close();

		_cts?.Dispose();

		_listener = null;
		_cts = null;

		ClearAuthAttempt();
	}
}

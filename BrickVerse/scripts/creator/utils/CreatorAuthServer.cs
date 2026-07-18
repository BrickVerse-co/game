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

		bool isSuccess = statusCode is >= 200 and < 300;
		string encodedTitle = WebUtility.HtmlEncode(title);
		string encodedMessage = WebUtility.HtmlEncode(message);
		string pageClass = isSuccess ? "success" : "error";
		string statusLabel = isSuccess ? "Authentication complete" : "Authentication failed";
		string icon = isSuccess
			? """
				<svg viewBox="0 0 24 24" aria-hidden="true">
					<path d="M20 6 9 17l-5-5" />
				</svg>
				"""
			: """
				<svg viewBox="0 0 24 24" aria-hidden="true">
					<path d="M12 8v5" />
					<path d="M12 17h.01" />
					<path d="M10.3 3.6 2.4 17.3A2 2 0 0 0 4.1 20h15.8a2 2 0 0 0 1.7-2.7L13.7 3.6a2 2 0 0 0-3.4 0Z" />
				</svg>
				""";

		string html = $$"""
			<!doctype html>
			<html lang="en">
				<head>
					<meta charset="utf-8">
					<meta name="viewport" content="width=device-width, initial-scale=1">
					<meta name="color-scheme" content="dark">
					<title>{{encodedTitle}}</title>
					<style>
						:root {
							font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
							color: #f8fafc;
							background: #07111f;
						}

						* {
							box-sizing: border-box;
						}

						body {
							min-height: 100vh;
							margin: 0;
							display: grid;
							place-items: center;
							padding: 24px;
							overflow: hidden;
						}

						body::before {
							content: "";
							position: fixed;
							inset: 0;
							background:
								radial-gradient(circle at 50% 35%, rgba(1, 135, 248, .18), transparent 38%),
								linear-gradient(145deg, #07111f 0%, #0b1728 100%);
							z-index: -2;
						}

						body.success::before {
							background:
								radial-gradient(circle at 50% 35%, rgba(34, 197, 94, .32), transparent 42%),
								linear-gradient(145deg, #06160d 0%, #0b2a18 100%);
						}

						body.error::before {
							background:
								radial-gradient(circle at 50% 35%, rgba(239, 68, 68, .28), transparent 42%),
								linear-gradient(145deg, #1a090b 0%, #2b1014 100%);
						}

						.card {
							width: min(100%, 520px);
							padding: 42px 38px 36px;
							text-align: center;
							background: rgba(10, 20, 35, .82);
							border: 1px solid rgba(255, 255, 255, .1);
							border-radius: 24px;
							box-shadow: 0 24px 80px rgba(0, 0, 0, .38);
							backdrop-filter: blur(18px);
						}

						.icon {
							width: 78px;
							height: 78px;
							margin: 0 auto 24px;
							display: grid;
							place-items: center;
							border-radius: 999px;
							background: rgba(255, 255, 255, .08);
							border: 1px solid rgba(255, 255, 255, .12);
						}

						.success .icon {
							color: #4ade80;
							background: rgba(34, 197, 94, .14);
							border-color: rgba(74, 222, 128, .32);
							box-shadow: 0 0 42px rgba(34, 197, 94, .18);
						}

						.error .icon {
							color: #fb7185;
							background: rgba(239, 68, 68, .14);
							border-color: rgba(251, 113, 133, .32);
							box-shadow: 0 0 42px rgba(239, 68, 68, .18);
						}

						.icon svg {
							width: 38px;
							height: 38px;
							fill: none;
							stroke: currentColor;
							stroke-width: 2.2;
							stroke-linecap: round;
							stroke-linejoin: round;
						}

						.eyebrow {
							margin: 0 0 10px;
							font-size: 12px;
							font-weight: 800;
							letter-spacing: .14em;
							text-transform: uppercase;
							color: rgba(226, 232, 240, .62);
						}

						h1 {
							margin: 0;
							font-size: clamp(28px, 7vw, 38px);
							line-height: 1.12;
							letter-spacing: -.035em;
						}

						.message {
							margin: 16px auto 0;
							max-width: 390px;
							color: #cbd5e1;
							font-size: 16px;
							line-height: 1.65;
						}

						.hint {
							margin: 28px 0 0;
							padding-top: 22px;
							border-top: 1px solid rgba(255, 255, 255, .08);
							color: rgba(203, 213, 225, .62);
							font-size: 13px;
						}

						@media (max-width: 520px) {
							.card {
								padding: 34px 24px 30px;
								border-radius: 20px;
							}
						}
					</style>
				</head>
				<body class="{{pageClass}}">
					<main class="card" role="status" aria-live="polite">
						<div class="icon">{{icon}}</div>
						<p class="eyebrow">{{statusLabel}}</p>
						<h1>{{encodedTitle}}</h1>
						<p class="message">{{encodedMessage}}</p>
						<p class="hint">You can safely close this browser window.</p>
					</main>
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
			<html lang="en">
				<head>
					<meta charset="utf-8">
					<meta name="viewport" content="width=device-width, initial-scale=1">
					<meta name="color-scheme" content="dark">
					<title>Signing in to BrickVerse</title>
					<style>
						:root {
							font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
							color: #f8fafc;
							background: #07111f;
						}

						* {
							box-sizing: border-box;
						}

						body {
							min-height: 100vh;
							margin: 0;
							display: grid;
							place-items: center;
							padding: 24px;
							background:
								radial-gradient(circle at 50% 35%, rgba(1, 135, 248, .28), transparent 42%),
								linear-gradient(145deg, #07111f 0%, #0b1728 100%);
						}

						.card {
							width: min(100%, 500px);
							padding: 42px 36px 36px;
							text-align: center;
							background: rgba(10, 20, 35, .82);
							border: 1px solid rgba(255, 255, 255, .1);
							border-radius: 24px;
							box-shadow: 0 24px 80px rgba(0, 0, 0, .38);
							backdrop-filter: blur(18px);
						}

						.spinner-wrap {
							width: 78px;
							height: 78px;
							margin: 0 auto 24px;
							display: grid;
							place-items: center;
							border-radius: 999px;
							background: rgba(1, 135, 248, .12);
							border: 1px solid rgba(56, 189, 248, .25);
							box-shadow: 0 0 44px rgba(1, 135, 248, .17);
						}

						.spinner {
							width: 38px;
							height: 38px;
							border: 4px solid rgba(255, 255, 255, .18);
							border-top-color: #38bdf8;
							border-radius: 999px;
							animation: spin .85s linear infinite;
						}

						.eyebrow {
							margin: 0 0 10px;
							font-size: 12px;
							font-weight: 800;
							letter-spacing: .14em;
							text-transform: uppercase;
							color: rgba(125, 211, 252, .82);
						}

						h1 {
							margin: 0;
							font-size: clamp(28px, 7vw, 38px);
							line-height: 1.12;
							letter-spacing: -.035em;
						}

						p:last-child {
							margin: 16px auto 0;
							max-width: 360px;
							color: #cbd5e1;
							font-size: 16px;
							line-height: 1.65;
						}

						@keyframes spin {
							to {
								transform: rotate(360deg);
							}
						}

						@media (prefers-reduced-motion: reduce) {
							.spinner {
								animation-duration: 1.8s;
							}
						}
					</style>
				</head>
				<body>
					<main class="card" role="status" aria-live="polite">
						<div class="spinner-wrap">
							<div class="spinner" aria-hidden="true"></div>
						</div>
						<p class="eyebrow">Secure authentication</p>
						<h1>Signing you in</h1>
						<p>Please keep this window open while BrickVerse finishes connecting your account.</p>
					</main>
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

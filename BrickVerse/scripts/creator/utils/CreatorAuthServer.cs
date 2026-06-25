// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Shared;
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BrickVerse.Creator.Utils;

public static class CreatorAuthServer
{
	private const string CallbackHost = "127.0.0.1";
	private const int CallbackPort = 42424;
	private const string CallbackPath = "/openid/callback/";

	public static string RedirectUri =>
		$"http://{CallbackHost}:{CallbackPort}{CallbackPath}";

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

		PT.Print($"CreatorAuthServer listening on {RedirectUri}");
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

				_ = Task.Run(async () =>
				{
					await HandleCallbackAsync(ctx);
				}, cancellationToken);
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
				GD.PushError($"CreatorAuthServer error: {ex.Message}");
		}
	}

	private static async Task HandleCallbackAsync(HttpListenerContext ctx)
	{
		try
		{
			string? code = ctx.Request.QueryString["code"];
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
				await WriteHtmlAsync(ctx, 400, "BrickVerse Login Failed", "Missing authorization code.");
				return;
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
				await WriteHtmlAsync(ctx, 400, "BrickVerse Login Failed", "No active login request was found.");
				return;
			}

			if (string.IsNullOrWhiteSpace(state) || state != expectedState)
			{
				await WriteHtmlAsync(ctx, 400, "BrickVerse Login Failed", "Invalid login state.");
				return;
			}

			ClearAuthAttempt();

			Callable.From(async () =>
			{
				await CreatorAPI.HandleOpenIdCallback(
					code,
					RedirectUri,
					codeVerifier
				);
			}).CallDeferred();

			PT.Print("OpenID callback handled successfully - user should be authenticated now.");

			await WriteHtmlAsync(
				ctx,
				200,
				"BrickVerse Login Complete",
				"You may now close this window and return to BrickVerse Studio."
			);
		}
		catch (Exception ex)
		{
			GD.PushError($"OpenID callback error: {ex.Message}");

			try
			{
				await WriteHtmlAsync(ctx, 500, "BrickVerse Login Failed", "An internal error occurred.");
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
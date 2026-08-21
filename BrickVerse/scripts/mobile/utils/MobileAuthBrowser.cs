// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Mobile.Utils;

/// <summary>
/// Bridges mobile HTTP requests to Godot's HTTPRequest implementation and
/// optionally opens the native/in-app authentication browser.
/// </summary>
public partial class MobileAuthBrowser : Node
{
	private GodotObject? _plugin;

	public event Action<string>? CallbackReceived;

	public override void _Ready()
	{
		if (!Engine.HasSingleton("BrickVerseWebView"))
		{
			BV.PrintWarn("In-app authentication browser plugin is unavailable.");
			return;
		}

		_plugin = Engine.GetSingleton("BrickVerseWebView");
		_plugin.Connect("url_received", Callable.From<string>(OnUrlReceived));
	}

	/// <summary>
	/// Sends an HttpRequestMessage through Godot's HTTPRequest transport.
	/// BVHttpClient can use this as its Android transport so callers do not
	/// need any platform-specific networking logic.
	/// </summary>
	public async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken
	)
	{
		if (request.RequestUri == null)
			throw new InvalidOperationException("HTTP request URI is missing.");

		List<string> headers = [];

		foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
		{
			foreach (string value in header.Value)
				headers.Add($"{header.Key}: {value}");
		}

		string body = "";

		if (request.Content != null)
		{
			foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
			{
				foreach (string value in header.Value)
					headers.Add($"{header.Key}: {value}");
			}

			body = await request.Content.ReadAsStringAsync(cancellationToken);
		}

		Godot.HttpClient.Method method = ToGodotMethod(request.Method);
		HttpRequest requestNode = new() { Timeout = 30 };
		TaskCompletionSource<HttpResponseMessage> completion = new(
			TaskCreationOptions.RunContinuationsAsynchronously
		);
		TaskCompletionSource<Error> started = new(
			TaskCreationOptions.RunContinuationsAsynchronously
		);

		void OnCompleted(long result, long responseCode, string[] responseHeaders, byte[] responseBody)
		{
			HttpRequest.Result godotResult = (HttpRequest.Result)result;
			if (godotResult != HttpRequest.Result.Success)
			{
				completion.TrySetException(
					new HttpRequestException($"Godot HTTP request failed ({godotResult}).")
				);
				return;
			}

			HttpResponseMessage response = new((HttpStatusCode)responseCode)
			{
				Content = new ByteArrayContent(responseBody),
			};

			foreach (string rawHeader in responseHeaders)
			{
				int separator = rawHeader.IndexOf(':');
				if (separator <= 0)
					continue;

				string name = rawHeader[..separator].Trim();
				string value = rawHeader[(separator + 1)..].Trim();
				if (!response.Headers.TryAddWithoutValidation(name, value))
					response.Content.Headers.TryAddWithoutValidation(name, value);
			}

			completion.TrySetResult(response);
		}

		// Asset loads may call this transport from a thread-pool worker. Godot
		// objects must enter the scene tree and start requests on the main thread.
		Callable.From(() =>
		{
			try
			{
				requestNode.RequestCompleted += OnCompleted;
				AddChild(requestNode);
				started.TrySetResult(
					requestNode.Request(
						request.RequestUri.ToString(),
						[.. headers],
						method,
						body
					)
				);
			}
			catch (Exception exception)
			{
				started.TrySetException(exception);
			}
		}).CallDeferred();

		Error requestResult = await started.Task.WaitAsync(cancellationToken);

		if (requestResult != Error.Ok)
		{
			Callable.From(() =>
			{
				requestNode.RequestCompleted -= OnCompleted;
				if (GodotObject.IsInstanceValid(requestNode))
					requestNode.QueueFree();
			}).CallDeferred();
			throw new HttpRequestException(
				$"Godot could not start the HTTP request ({requestResult})."
			);
		}

		try
		{
			return await completion.Task.WaitAsync(cancellationToken);
		}
		finally
		{
			Callable.From(() =>
			{
				if (!GodotObject.IsInstanceValid(requestNode))
					return;
				requestNode.RequestCompleted -= OnCompleted;
				requestNode.CancelRequest();
				requestNode.QueueFree();
			}).CallDeferred();
		}
	}

	private static Godot.HttpClient.Method ToGodotMethod(HttpMethod method)
	{
		if (method == HttpMethod.Get)
			return Godot.HttpClient.Method.Get;
		if (method == HttpMethod.Post)
			return Godot.HttpClient.Method.Post;
		if (method == HttpMethod.Put)
			return Godot.HttpClient.Method.Put;
		if (method == HttpMethod.Delete)
			return Godot.HttpClient.Method.Delete;
		if (method == HttpMethod.Patch)
			return Godot.HttpClient.Method.Patch;
		if (method == HttpMethod.Head)
			return Godot.HttpClient.Method.Head;
		if (method == HttpMethod.Options)
			return Godot.HttpClient.Method.Options;

		throw new NotSupportedException($"HTTP method '{method}' is not supported by Godot HTTPRequest.");
	}

	public bool Open(string url)
	{
		if (_plugin == null || !GodotObject.IsInstanceValid(_plugin))
			return false;

		_plugin.Call("open_auth_url", url);
		return true;
	}

	private void OnUrlReceived(string url) => CallbackReceived?.Invoke(url);

	public override void _ExitTree()
	{
		if (_plugin != null && GodotObject.IsInstanceValid(_plugin))
		{
			_plugin.Call("close");
			_plugin.Disconnect("url_received", Callable.From<string>(OnUrlReceived));
		}

		base._ExitTree();
	}
}

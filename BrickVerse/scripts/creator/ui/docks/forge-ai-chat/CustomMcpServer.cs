using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace BrickVerse.Creator.UI;

/// <summary>Loopback Streamable HTTP MCP endpoint for external developer tools.</summary>
public sealed partial class CustomMcpServer : Control
{
	private sealed record PendingRequest(string Body, TaskCompletionSource<string?> Completion);
	private static CustomMcpServer? _instance;
	private readonly ConcurrentQueue<PendingRequest> _requests = new();
	private readonly ForgeMcpServer _mcp = new();
	private readonly string _token = Guid.NewGuid().ToString("N");
	private HttpListener? _listener;
	private CancellationTokenSource? _lifetime;
	private Task? _listenTask;
	private int _port;
	private Label? _status;
	private LineEdit? _url;
	private TextEdit? _config;
	private OptionButton? _clientFormat;

	public static void Open()
	{
		if (_instance == null || !IsInstanceValid(_instance))
		{
			_instance = new CustomMcpServer { Name = "CustomMcpServer" };
			Menu.Singleton.AddChild(_instance);
		}
		_instance.ShowWindow();
	}

	public override void _Ready()
	{
		Visible = false;
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		while (_requests.TryDequeue(out var request))
			HandleOnMainThread(request);
	}

	private async void HandleOnMainThread(PendingRequest request)
	{
		try { request.Completion.TrySetResult(await _mcp.HandleAsync(request.Body, this, _lifetime?.Token ?? CancellationToken.None)); }
		catch (Exception error) { request.Completion.TrySetException(error); }
	}

	private void ShowWindow()
	{
		Window window = new() { Title = "Custom MCP Server", Size = new Vector2I(650, 440), MinSize = new Vector2I(520, 360), Transient = true };
		window.CloseRequested += window.QueueFree;
		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 18);
		window.AddChild(margin);
		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 10);
		margin.AddChild(layout);
		layout.AddChild(new Label { Text = "Connect Cursor, Codex, or another MCP client", ThemeTypeVariation = "HeaderLarge" });
		layout.AddChild(new Label { Text = $"The server listens only on this computer. It uses the same {ForgeToolCatalog.Definitions.Count} Creator tools and approval rules as Forge. Keep Creator open with a world loaded; shell commands always require approval here.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
		_status = new Label { Text = _listener == null ? "Stopped" : "Running" };
		layout.AddChild(_status);
		_url = new LineEdit { Editable = false, PlaceholderText = "Start the server to create an endpoint" };
		layout.AddChild(_url);
		HBoxContainer actions = new();
		Button toggle = new() { Text = _listener == null ? "Start server" : "Stop server" };
		toggle.Pressed += () => { if (_listener == null) StartServer(); else StopServer(); toggle.Text = _listener == null ? "Start server" : "Stop server"; RefreshWindow(); };
		Button copyUrl = new() { Text = "Copy URL" };
		copyUrl.Pressed += () => { if (!string.IsNullOrWhiteSpace(_url.Text)) DisplayServer.ClipboardSet(_url.Text); };
		_clientFormat = new OptionButton();
		_clientFormat.AddItem("Codex CLI");
		_clientFormat.AddItem("Codex TOML");
		_clientFormat.AddItem("Cursor / JSON");
		_clientFormat.ItemSelected += _ => RefreshWindow();
		Button copyConfig = new() { Text = "Copy config" };
		copyConfig.Pressed += () => { if (_config != null) DisplayServer.ClipboardSet(_config.Text); };
		actions.AddChild(toggle); actions.AddChild(copyUrl); actions.AddChild(_clientFormat); actions.AddChild(copyConfig); layout.AddChild(actions);
		_config = new TextEdit { Editable = false, SizeFlagsVertical = SizeFlags.ExpandFill, WrapMode = TextEdit.LineWrappingMode.Boundary };
		layout.AddChild(_config);
		AddChild(window);
		RefreshWindow();
		window.PopupCentered();
	}

	private void RefreshWindow()
	{
		if (_status == null || _url == null || _config == null || _clientFormat == null) return;
		var endpoint = _listener == null ? "" : $"http://127.0.0.1:{_port}/mcp?token={_token}";
		_status.Text = _listener == null ? "Stopped" : $"Running on port {_port}";
		_url.Text = endpoint;
		_config.Text = string.IsNullOrEmpty(endpoint)
			? "Start the server, then copy this client configuration."
			: _clientFormat.Selected switch
			{
				0 => $"codex mcp add brickverse-creator --url \"{endpoint}\"",
				1 => $"[mcp_servers.brickverse-creator]\nurl = \"{endpoint}\"",
				_ => $"{{\n  \"mcpServers\": {{\n    \"brickverse-creator\": {{\n      \"url\": \"{endpoint}\"\n    }}\n  }}\n}}",
			};
	}

	private void StartServer()
	{
		if (_listener != null) return;
		TcpListener probe = new(IPAddress.Loopback, 0); probe.Start(); _port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
		_lifetime = new CancellationTokenSource();
		_listener = new HttpListener();
		_listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
		try { _listener.Start(); _listenTask = ListenAsync(_listener, _lifetime.Token); }
		catch (Exception error) { GD.PrintErr($"Custom MCP server failed: {error.Message}"); StopServer(); if (_status != null) _status.Text = error.Message; }
	}

	private async Task ListenAsync(HttpListener listener, CancellationToken cancellation)
	{
		while (!cancellation.IsCancellationRequested)
		{
			try { _ = HandleHttpAsync(await listener.GetContextAsync(), cancellation); }
			catch (Exception) when (cancellation.IsCancellationRequested || !listener.IsListening) { break; }
			catch (Exception error) { GD.PrintErr($"Custom MCP listener error: {error.Message}"); }
		}
	}

	private async Task HandleHttpAsync(HttpListenerContext context, CancellationToken cancellation)
	{
		try
		{
			context.Response.Headers["MCP-Protocol-Version"] = "2025-06-18";
			if (context.Request.HttpMethod != "POST" || context.Request.Url?.AbsolutePath != "/mcp" || context.Request.QueryString["token"] != _token) { context.Response.StatusCode = 404; return; }
			using StreamReader reader = new(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
			string body = await reader.ReadToEndAsync(cancellation);
			TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
			_requests.Enqueue(new PendingRequest(body, completion));
			string? response = await completion.Task.WaitAsync(TimeSpan.FromMinutes(3), cancellation);
			if (response == null) { context.Response.StatusCode = 202; return; }
			byte[] bytes = Encoding.UTF8.GetBytes(response);
			context.Response.StatusCode = 200; context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
			await context.Response.OutputStream.WriteAsync(bytes, cancellation);
		}
		catch (Exception error) { context.Response.StatusCode = 500; GD.PrintErr($"Custom MCP request failed: {error.Message}"); }
		finally { context.Response.Close(); }
	}

	private void StopServer()
	{
		_lifetime?.Cancel();
		try { _listener?.Stop(); _listener?.Close(); } catch { }
		_listener = null; _lifetime?.Dispose(); _lifetime = null; _listenTask = null;
	}

	public override void _ExitTree() { StopServer(); if (ReferenceEquals(_instance, this)) _instance = null; base._ExitTree(); }
}

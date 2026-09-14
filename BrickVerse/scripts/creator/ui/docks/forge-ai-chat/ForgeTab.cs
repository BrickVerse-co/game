// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Text.Json;
using BrickVerse.Datamodel;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Creator.UI;

/// <summary>The shared Forge web application hosted through gdCEF.</summary>
public partial class ForgeTab : VBoxContainer
{
	public static string ForgeUrl =>
		Uri.TryCreate(Globals.ApiEndpoint, UriKind.Absolute, out var api)
		&& api.Scheme == "https"
		&& api.Host == "api.brickverse.gg"
			? "https://forge.brickverse.gg?creator_landing=1"
			: "http://localhost:3000/forge-ai-chat?creator_landing=1";

	private readonly System.Threading.CancellationTokenSource _lifetime = new();
	private readonly string _bridgeToken = Guid.NewGuid().ToString("N");
	private readonly ForgeMcpServer _mcp = new();
	private string _forgeOrigin = "";
	private Node? _cef;
	private Node? _browser;
	private TextureRect? _surface;
	private string _lastExternalUrl = "";
	private bool _shuttingDown;
	private bool _cefInitialized;
	public World? Root => World.Current;

	public override void _Ready()
	{
		try
		{
			InitializeBrowser();
		}
		catch (Exception ex)
		{
			ReportRecoverableError("Forge browser initialization failed", ex);
			ShowBrowserError("Forge could not start its embedded browser. Check the Creator console for details.");
		}
	}

	private void InitializeBrowser()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		ClipContents = true;
		if (!ClassDB.ClassExists("GdCEF"))
		{
			AddChild(
				new Label
				{
					Text =
						"Forge requires gdCEF. Run bin_scripts/install-gdcef.ps1 and restart Creator.",
					AutowrapMode = TextServer.AutowrapMode.WordSmart,
				}
			);
			return;
		}
		_surface = new TextureRect
		{
			Name = "ForgeBrowserSurface",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = MouseFilterEnum.Stop,
			FocusMode = FocusModeEnum.All,
		};
		_surface.GuiInput += ForwardBrowserInput;
		_surface.Resized += ResizeBrowser;
		AddChild(_surface);

		_cef = ClassDB.Instantiate("GdCEF").AsGodotObject() as Node;
		if (_cef == null)
			return;
		AddChild(_cef);
		var config = new Godot.Collections.Dictionary
		{
			["enable_media_stream"] = true,
			["remote_debugging_port"] = 0,
			["log_severity"] = "warning",
		};
		if (!_cef.Call("initialize", config).AsBool())
		{
			ShowBrowserError("gdCEF could not initialize: " + _cef.Call("get_error").AsString());
			return;
		}
		_cefInitialized = true;
		_forgeOrigin = new Uri(ForgeUrl).GetLeftPart(UriPartial.Authority);
		var settings = new Godot.Collections.Dictionary
		{
			["javascript"] = true,
			["javascript_access_clipboard"] = true,
			["frame_rate"] = 60,
		};
		_browser =
			_cef.Call("create_browser", ForgeUrl, _surface, settings).AsGodotObject() as Node;
		if (_browser == null)
		{
			ShowBrowserError("gdCEF could not create the Forge browser.");
			return;
		}
		_browser.Name = "ForgeBrowser";
		if (!_browser.Call("register_method", this, nameof(ReceiveForgeMessage)).AsBool())
		{
			ShowBrowserError("Forge could not register the Creator tooling bridge.");
			return;
		}
		if (!_browser.Call("register_method", this, nameof(ReceiveForgeConsole)).AsBool())
			GD.PrintErr("Forge could not register CEF console forwarding.");
		_browser.Connect("on_page_loaded", Callable.From<long, Node>(OnPageLoaded));
		_browser.Connect("on_page_failed_loading", Callable.From<long, string, Node>(OnPageFailed));
		Callable.From(ResizeBrowser).CallDeferred();
	}

	private void OnPageLoaded(long status, Node browser)
	{
		if (_shuttingDown || browser != _browser || !IsInstanceValid(browser))
			return;
		try
		{
			HandlePageLoaded(status, browser);
		}
		catch (Exception ex)
		{
			ReportRecoverableError("Forge browser navigation failed", ex);
		}
	}

	private void HandlePageLoaded(long status, Node browser)
	{
		var currentUrl = browser.Call("get_url").AsString();
		if (!IsAllowedBrowserLocation(currentUrl))
		{
			OpenInSystemBrowser(currentUrl);
			browser.Call("load_url", ForgeUrl);
			return;
		}
		_lastExternalUrl = "";
		if (status < 200 || status >= 400)
			return;
		var script =
			"(()=>{if(location.origin!=="
			+ JsonSerializer.Serialize(_forgeOrigin)
			+ ")return;"
			+ "const bridge=window.__brickverseCreatorBridge??={listeners:new Set()};"
			+ "if(!window.brickverseCreatorToken)Object.defineProperty(window,'brickverseCreatorToken',{value:"
			+ JsonSerializer.Serialize(_bridgeToken)
			+ ",configurable:false});"
			+ "window.ipcMessage={addListener:(listener)=>bridge.listeners.add(listener),removeListener:(listener)=>bridge.listeners.delete(listener)};"
			+ "window.onIpcMessage=(message)=>bridge.listeners.forEach((listener)=>listener(String(message)));"
			+ "window.sendIpcMessage=(message)=>window.godotMethods.ReceiveForgeMessage(String(message));"
			+ "if(!window.__brickverseConsoleForwarded){window.__brickverseConsoleForwarded=true;const fmt=(args)=>args.map((v)=>{if(typeof v==='string')return v;try{return JSON.stringify(v)}catch{return String(v)}}).join(' ');for(const level of ['warn','error']){const original=console[level].bind(console);console[level]=(...args)=>{original(...args);try{window.godotMethods.ReceiveForgeConsole(level,fmt(args))}catch{}}}window.addEventListener('error',(e)=>{try{window.godotMethods.ReceiveForgeConsole('error',String(e.message||e.error||'Unhandled page error'))}catch{}});window.addEventListener('unhandledrejection',(e)=>{try{window.godotMethods.ReceiveForgeConsole('error','Unhandled promise rejection: '+fmt([e.reason]))}catch{}});}"
			+ "window.dispatchEvent(new Event('brickverseCreatorReady'));})();";
		browser.Call("execute_javascript", script);
	}

	private static readonly string[] AllowedBrowserPathPrefixes = ["/auth", "/forge-ai-chat"];

	private static bool IsAllowedBrowserLocation(string location)
	{
		if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
			return false;

		// Allow any path on Forge.
		if (
			uri.Scheme == Uri.UriSchemeHttps
			&& uri.Host.Equals("forge.brickverse.gg", StringComparison.OrdinalIgnoreCase)
		)
			return true;

		foreach (var path in AllowedBrowserPathPrefixes)
		{
			if (uri.AbsolutePath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	private void OpenInSystemBrowser(string location)
	{
		if (
			!Uri.TryCreate(location, UriKind.Absolute, out var uri)
			|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
			|| string.Equals(location, _lastExternalUrl, StringComparison.Ordinal)
		)
			return;

		_lastExternalUrl = location;
		var error = OS.ShellOpen(location);
		if (error != Error.Ok)
			GD.PrintErr($"Could not open external Forge link ({error}): {location}");
	}

	private void OnPageFailed(long code, string message, Node browser)
	{
		if (_shuttingDown || browser != _browser || !IsInstanceValid(browser) || code == -3)
			return; // CEF reports redirected/cancelled navigations as ERR_ABORTED.
		try
		{
			var currentUrl = browser.Call("get_url").AsString();
			if (!IsAllowedBrowserLocation(currentUrl))
			{
				OpenInSystemBrowser(currentUrl);
				browser.Call("load_url", ForgeUrl);
				return;
			}
			ShowBrowserError($"Forge failed to load ({code}): {message}");
		}
		catch (Exception ex)
		{
			ReportRecoverableError("Forge browser error handling failed", ex);
		}
	}

	public async void ReceiveForgeMessage(string message)
	{
		if (_shuttingDown || _browser == null || !IsInstanceValid(_browser))
			return;
		try
		{
			if (message.Length > 262144)
				return;
			using var request = JsonDocument.Parse(message);
			if (
				!request.RootElement.TryGetProperty("_creatorToken", out var token)
				|| token.GetString() != _bridgeToken
			)
				return;
			var reply = await _mcp.HandleAsync(message, this, _lifetime.Token);
			if (!_lifetime.IsCancellationRequested && reply != null)
				Callable.From<string>(SendMcpReply).CallDeferred(reply);
		}
		catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
		catch (Exception ex)
		{
			ReportRecoverableError("Forge MCP request failed", ex);
		}
	}

	public void ReceiveForgeConsole(string level, string message)
	{
		if (_shuttingDown || string.IsNullOrWhiteSpace(message)) return;
		string text = message.Length > 4000 ? message[..4000] + "…" : message;
		if (level.Equals("error", StringComparison.OrdinalIgnoreCase))
			BV.PrintErr("Forge Web: ", text);
		else if (level.Equals("warn", StringComparison.OrdinalIgnoreCase))
			BV.PrintWarn("Forge Web: ", text);
	}

	private void SendMcpReply(string reply)
	{
		if (_shuttingDown || _browser == null || !IsInstanceValid(_browser))
			return;
		try
		{
			_browser.Call(
				"execute_javascript",
				"window.onIpcMessage?.(" + JsonSerializer.Serialize(reply) + ");"
			);
		}
		catch (Exception ex)
		{
			ReportRecoverableError("Forge MCP reply failed", ex);
		}
	}

	private void ForwardBrowserInput(InputEvent input)
	{
		if (_shuttingDown || _browser == null || !IsInstanceValid(_browser) || _surface == null || !IsVisibleInTree())
			return;
		try
		{
			ForwardBrowserInputCore(input, _browser, _surface);
		}
		catch (Exception ex)
		{
			ReportRecoverableError("Forge browser input failed", ex);
		}
	}

	private void ForwardBrowserInputCore(InputEvent input, Node browser, TextureRect surface)
	{
		if (input is InputEventMouseMotion motion)
		{
			browser.Call("set_mouse_moved", (long)motion.Position.X, (long)motion.Position.Y);
		}
		else if (input is InputEventMouseButton mouse)
		{
			surface.GrabFocus();
			if (
				mouse.ButtonIndex == MouseButton.WheelUp
				|| mouse.ButtonIndex == MouseButton.WheelDown
			)
				browser.Call(
					"set_mouse_wheel_vertical",
					mouse.ButtonIndex == MouseButton.WheelUp ? 2L : -2L,
					mouse.ShiftPressed,
					mouse.CtrlPressed,
					mouse.AltPressed
				);
			else if (mouse.ButtonIndex == MouseButton.Left)
				browser.Call(mouse.Pressed ? "set_mouse_left_down" : "set_mouse_left_up");
			else if (mouse.ButtonIndex == MouseButton.Right)
				browser.Call(mouse.Pressed ? "set_mouse_right_down" : "set_mouse_right_up");
			else if (mouse.ButtonIndex == MouseButton.Middle)
				browser.Call(mouse.Pressed ? "set_mouse_middle_down" : "set_mouse_middle_up");
		}
		else if (input is InputEventKey key && surface.HasFocus())
		{
			var code = key.Unicode != 0 ? key.Unicode : (uint)key.Keycode;
			browser.Call(
				"set_key_pressed",
				(long)code,
				key.Pressed,
				key.ShiftPressed,
				key.AltPressed,
				key.CtrlPressed || key.MetaPressed
			);
		}
	}

	private void ResizeBrowser()
	{
		if (_shuttingDown || _browser == null || !IsInstanceValid(_browser) || _surface == null || _surface.Size.X <= 0 || _surface.Size.Y <= 0)
			return;
		try
		{
			_browser.Call("resize", _surface.Size);
		}
		catch (Exception ex)
		{
			ReportRecoverableError("Forge browser resize failed", ex);
		}
	}

	private void ShowBrowserError(string text)
	{
		if (_surface != null)
			_surface.Visible = false;
		var existing = GetNodeOrNull<Label>("BrowserError");
		if (existing != null)
			existing.Text = text;
		else
			AddChild(
				new Label
				{
					Name = "BrowserError",
					Text = text,
					AutowrapMode = TextServer.AutowrapMode.WordSmart,
				}
			);
	}

	public override void _ExitTree()
	{
		if (_shuttingDown)
			return;
		_shuttingDown = true;
		_lifetime.Cancel();
		if (_surface != null)
		{
			_surface.GuiInput -= ForwardBrowserInput;
			_surface.Resized -= ResizeBrowser;
		}
		_browser = null;
		try
		{
			// GdCEF.shutdown closes every browser and drains its native destruction queue.
			if (_cefInitialized && _cef != null && IsInstanceValid(_cef))
				_cef.Call("shutdown");
		}
		catch (Exception ex)
		{
			ReportRecoverableError("Forge browser shutdown failed", ex);
		}
		_cefInitialized = false;
		_cef = null;
		base._ExitTree();
	}

	private static void ReportRecoverableError(string context, Exception exception)
	{
		GD.PrintErr($"{context}: {exception.GetType().Name}: {exception.Message}");
	}

	public static string GetConsoleSnippet(int maxChars = 2000)
	{
		var label = DebugConsole.Singleton?.GetNodeOrNull<RichTextLabel>(
			"VBoxContainer/RichTextLabel"
		);
		var text = label?.Text?.Trim() ?? "";
		return text.Length <= maxChars ? text : text[^maxChars..];
	}
}

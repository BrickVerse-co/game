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
	private bool _mousePressed;
	private string _lastExternalUrl = "";
	public World? Root => World.Current;

	public override void _Ready()
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
		_browser.Connect("on_page_loaded", Callable.From<long, Node>(OnPageLoaded));
		_browser.Connect("on_page_failed_loading", Callable.From<long, string, Node>(OnPageFailed));
		Callable.From(ResizeBrowser).CallDeferred();
	}

	private void OnPageLoaded(long status, Node browser)
	{
		if (browser != _browser)
			return;
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
		browser.Call("register_method", this, nameof(ReceiveForgeMessage));
		var script =
			"if(location.origin==="
			+ JsonSerializer.Serialize(_forgeOrigin)
			+ "){"
			+ "const listeners=new Set();"
			+ "Object.defineProperty(window,'brickverseCreatorToken',{value:"
			+ JsonSerializer.Serialize(_bridgeToken)
			+ ",configurable:false});"
			+ "window.ipcMessage={addListener:(listener)=>listeners.add(listener),removeListener:(listener)=>listeners.delete(listener)};"
			+ "window.onIpcMessage=(message)=>listeners.forEach((listener)=>listener(String(message)));"
			+ "window.sendIpcMessage=(message)=>window.godotMethods.ReceiveForgeMessage(String(message));"
			+ "window.dispatchEvent(new Event('brickverseCreatorReady'));}";
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
		if (browser != _browser || code == -3)
			return; // CEF reports redirected/cancelled navigations as ERR_ABORTED.
		var currentUrl = browser.Call("get_url").AsString();
		if (!IsAllowedBrowserLocation(currentUrl))
		{
			OpenInSystemBrowser(currentUrl);
			browser.Call("load_url", ForgeUrl);
			return;
		}
		ShowBrowserError($"Forge failed to load ({code}): {message}");
	}

	public async void ReceiveForgeMessage(string message)
	{
		if (_browser == null || !IsInstanceValid(_browser))
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
		}
		catch (Exception)
		{
			return;
		}
		var reply = await _mcp.HandleAsync(message, this, _lifetime.Token);
		if (
			!_lifetime.IsCancellationRequested
			&& reply != null
			&& _browser != null
			&& IsInstanceValid(_browser)
		)
			_browser.Call(
				"execute_javascript",
				"window.onIpcMessage?.(" + JsonSerializer.Serialize(reply) + ");"
			);
	}

	private void ForwardBrowserInput(InputEvent input)
	{
		if (_browser == null || _surface == null || !IsVisibleInTree())
			return;
		if (input is InputEventMouseMotion motion)
		{
			_browser.Call("set_mouse_moved", (long)motion.Position.X, (long)motion.Position.Y);
			if (_mousePressed)
				_browser.Call("set_mouse_left_down");
		}
		else if (input is InputEventMouseButton mouse)
		{
			_surface.GrabFocus();
			if (
				mouse.ButtonIndex == MouseButton.WheelUp
				|| mouse.ButtonIndex == MouseButton.WheelDown
			)
				_browser.Call(
					"set_mouse_wheel_vertical",
					mouse.ButtonIndex == MouseButton.WheelUp ? 2L : -2L,
					mouse.ShiftPressed,
					mouse.CtrlPressed,
					mouse.AltPressed
				);
			else if (mouse.ButtonIndex == MouseButton.Left)
			{
				_mousePressed = mouse.Pressed;
				_browser.Call(mouse.Pressed ? "set_mouse_left_down" : "set_mouse_left_up");
			}
			else if (mouse.ButtonIndex == MouseButton.Right)
				_browser.Call(mouse.Pressed ? "set_mouse_right_down" : "set_mouse_right_up");
			else if (mouse.ButtonIndex == MouseButton.Middle)
				_browser.Call(mouse.Pressed ? "set_mouse_middle_down" : "set_mouse_middle_up");
		}
		else if (input is InputEventKey key && _surface.HasFocus())
		{
			var code = key.Unicode != 0 ? key.Unicode : (uint)key.Keycode;
			_browser.Call(
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
		if (_browser != null && _surface != null && _surface.Size.X > 0 && _surface.Size.Y > 0)
			_browser.Call("resize", _surface.Size);
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
		_lifetime.Cancel();
		if (_browser != null && IsInstanceValid(_browser))
			_browser.Call("close");
		if (_cef != null && IsInstanceValid(_cef))
			_cef.Call("shutdown");
		base._ExitTree();
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

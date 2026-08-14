// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Mobile.Utils;

/// Bridges the mobile auth flow to a native in-app browser surface.
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

	public bool Open(string url)
	{
		if (_plugin == null || !GodotObject.IsInstanceValid(_plugin)) return false;
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

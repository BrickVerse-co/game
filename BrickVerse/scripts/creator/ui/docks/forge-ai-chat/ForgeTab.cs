// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Shared;
using System;
using System.Text.Json;

namespace BrickVerse.Creator.UI;

/// <summary>The shared Forge web application; Creator tools stay in the editor process.</summary>
public partial class ForgeTab : VBoxContainer
{
    public static string ForgeUrl => Uri.TryCreate(Globals.ApiEndpoint, UriKind.Absolute, out var api)
        && api.Scheme == "https" && api.Host == "api.brickverse.gg"
        ? "https://forge.brickverse.gg?creator_landing=1"
        : "http://localhost:3000/forge-ai-chat?creator_landing=1";
    private string _forgeOrigin = "";
    private readonly System.Threading.CancellationTokenSource _lifetime = new();
    private Control? _browser;
    private readonly string _bridgeToken = Guid.NewGuid().ToString("N");
    private readonly ForgeMcpServer _mcp = new();
    public World? Root => World.Current;

    public override void _Ready()
    {
        if (!ClassDB.ClassExists("CefTexture"))
        {
            AddChild(new Label { Text = "Forge requires Godot CEF. Install the Creator browser runtime and restart.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            return;
        }
        _browser = ClassDB.Instantiate("CefTexture").AsGodotObject() as Control;
        if (_browser == null) return;
        _browser.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _browser.SizeFlagsVertical = SizeFlags.ExpandFill;
        _forgeOrigin = new Uri(ForgeUrl).GetLeftPart(UriPartial.Authority);
        _browser.Set("preload_script", "if (window.top === window && location.origin === " + JsonSerializer.Serialize(_forgeOrigin) + ") Object.defineProperty(window, 'brickverseCreatorToken', { value: " + JsonSerializer.Serialize(_bridgeToken) + " });");
        _browser.Set("url", ForgeUrl);
        _browser.Connect("ipc_message", Callable.From<string>(OnMessage));
        AddChild(_browser);
    }

    private async void OnMessage(string message)
    {
        // IPC is delivered on Godot's main thread. Never run world tools on a network thread.
        if (_browser == null || !Uri.TryCreate(_browser.Get("url").AsString(), UriKind.Absolute, out var uri)
            || uri.GetLeftPart(UriPartial.Authority) != _forgeOrigin)
            return;
        try
        {
            if (message.Length > 262144) return;
            using var request = JsonDocument.Parse(message);
            if (!request.RootElement.TryGetProperty("_creatorToken", out var token) || token.GetString() != _bridgeToken) return;
        }
        catch (Exception) { return; }
        var reply = await _mcp.HandleAsync(message, this, _lifetime.Token);
        if (!_lifetime.IsCancellationRequested && reply != null && IsInstanceValid(_browser))
            _browser.Call("send_ipc_message", reply);
    }

    public override void _ExitTree() { _lifetime.Cancel(); base._ExitTree(); }

    public static string GetConsoleSnippet(int maxChars = 2000)
    {
        var label = DebugConsole.Singleton?.GetNodeOrNull<RichTextLabel>("VBoxContainer/RichTextLabel");
        var text = label?.Text?.Trim() ?? "";
        return text.Length <= maxChars ? text : text[^maxChars..];
    }
}

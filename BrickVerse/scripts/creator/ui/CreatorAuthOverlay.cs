// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Client.WebAPI;
using BrickVerse.Schemas.API;

namespace BrickVerse.Creator.UI;

/// <summary>
/// Blocking auth overlay shown on startup when the user is not authenticated.
/// Prevents use of Workshop until login via browser or quick code succeeds.
/// </summary>
public partial class CreatorAuthOverlay : Control
{
    [Export] private Button _browserLoginBtn = null!;
    [Export] private Button _quickCodeBtn = null!;
    [Export] private Label _statusLabel = null!;

    private string? _pendingQuickCode;

    public override void _Ready()
    {
        _browserLoginBtn = GetNode<Button>("%BrowserBtn");
        _quickCodeBtn = GetNode<Button>("%QuickBtn");
        _statusLabel = GetNode<Label>("%Status");

        _browserLoginBtn.Pressed += OnBrowserLogin;
        _quickCodeBtn.Pressed += OnQuickCode;

        PolyAuthAPI.UserAuthenticated += OnAuthenticated;
        PolyAuthAPI.ShowQuickSignInCode += OnShowQuickCode;
        PolyAuthAPI.AskForAuthentication += ShowOverlay;
    }

    private void ShowOverlay()
    {
        Visible = true;
        _statusLabel.Text = "Sign in to continue";
        _browserLoginBtn.Disabled = false;
        _quickCodeBtn.Disabled = false;
    }

    private void OnBrowserLogin()
    {
        _statusLabel.Text = "Opening browser...";
        _browserLoginBtn.Disabled = true;
        PolyAuthAPI.StartBrowserLogin();
    }

    private async void OnQuickCode()
    {
        _statusLabel.Text = "Generating code...";
        _quickCodeBtn.Disabled = true;
        await PolyAuthAPI.StartQuickSignInCodeFlow();
    }

    private void OnShowQuickCode(string code)
    {
        _pendingQuickCode = code;
        _statusLabel.Text = $"Enter code on brickverse.gg:\n{code}";
        _quickCodeBtn.Disabled = false;
    }

    private void OnAuthenticated(APIV3AuthMeUser me)
    {
        Visible = false;
        GD.Print($"Workshop authenticated as {me.Username}");
    }
    // auth overlay ready
}

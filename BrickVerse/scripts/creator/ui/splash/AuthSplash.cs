// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Threading.Tasks;
using BrickVerse.Creator.Utils;
using BrickVerse.Schemas.API;
using Godot;

namespace BrickVerse.Creator.UI;

public partial class AuthSplash : Panel
{
	[Export] private Button _signInButton = null!;
	[Export] private Button _createAccountButton = null!;
	[Export] private Button _termsButton = null!;
	[Export] private Button _privacyButton = null!;
	[Export] private TextureRect _qrCode = null!;
	[Export] private Button _deviceCode = null!;
	[Export] private Label _status = null!;
	private Panel _card = null!;
	private BoxContainer _layout = null!;
	private Control _left = null!;
	private Control _right = null!;
	private string _quickToken = string.Empty;
	private DateTimeOffset _expiresAt;
	private double _pollElapsed;
	private bool _requestInFlight;
	private bool _pollInFlight;
	private const string TermsUrl = "https://resources.brickverse.gg/legal/terms/terms-of-service";
	private const string PrivacyUrl = "https://resources.brickverse.gg/legal/privacy/privacy-policy";
	private const float DesktopCardMaxWidth = 1500f;
	private const float DesktopCardMaxHeight = 900f;
	private const float DesktopDevicePanelWidth = 520f;

	public override void _Ready()
	{
		_signInButton ??= GetNode<Button>("Card/Layout/Left/SignIn");
		_createAccountButton ??= GetNode<Button>("Card/Layout/Left/AccountActions/CreateAccount");
		_termsButton ??= GetNode<Button>("Card/Layout/Left/Legal/Terms");
		_privacyButton ??= GetNode<Button>("Card/Layout/Left/Legal/Privacy");
		_qrCode ??= GetNode<TextureRect>("Card/Layout/Right/Content/QrPanel/Qr");
		_deviceCode ??= GetNode<Button>("Card/Layout/Right/Content/QrPanel/Code");
		_status ??= GetNode<Label>("Card/Layout/Right/Content/Status");
		_card = GetNode<Panel>("Card");
		_layout = GetNode<BoxContainer>("Card/Layout");
		_left = GetNode<Control>("Card/Layout/Left");
		_right = GetNode<Control>("Card/Layout/Right");
		Visible = !CreatorAPI.IsUserAuthenticated;
		MouseFilter = MouseFilterEnum.Stop;
		ZIndex = 9999;
		_signInButton.Pressed += OpenBrowserLogin;
		_createAccountButton.Pressed += OpenCreateAccount;
		_termsButton.Pressed += OpenTerms;
		_privacyButton.Pressed += OpenPrivacy;
		_deviceCode.Pressed += CopyDeviceCode;
		GetViewport().SizeChanged += UpdateResponsiveLayout;
		CreatorAPI.UserAuthenticated += OnUserAuthenticated;
		CreatorAPI.AuthenticationFailed += OnAuthenticationFailed;
		CreatorAPI.AuthenticationCleared += OnAuthenticationCleared;
		if (Visible) _ = StartQuickSignInAsync();
		CallDeferred(MethodName.UpdateResponsiveLayout);
	}

	public override void _Process(double delta)
	{
		if (!Visible || CreatorAPI.IsUserAuthenticated || string.IsNullOrWhiteSpace(_quickToken) || _pollInFlight) return;
		if (DateTimeOffset.UtcNow >= _expiresAt)
		{
			_status.Text = "Code expired. Restart Creator to generate a new code.";
			_quickToken = string.Empty;
			return;
		}
		_pollElapsed += delta;
		if (_pollElapsed >= 2.5) { _pollElapsed = 0; _ = PollQuickSignInAsync(); }
	}

	public override void _ExitTree()
	{
		_signInButton.Pressed -= OpenBrowserLogin;
		_createAccountButton.Pressed -= OpenCreateAccount;
		_termsButton.Pressed -= OpenTerms;
		_privacyButton.Pressed -= OpenPrivacy;
		_deviceCode.Pressed -= CopyDeviceCode;
		GetViewport().SizeChanged -= UpdateResponsiveLayout;
		CreatorAPI.UserAuthenticated -= OnUserAuthenticated;
		CreatorAPI.AuthenticationFailed -= OnAuthenticationFailed;
		CreatorAPI.AuthenticationCleared -= OnAuthenticationCleared;
	}

	private async Task StartQuickSignInAsync()
	{
		if (_requestInFlight) return;
		_requestInFlight = true;
		_status.Text = "Generating a secure one-time code…";
		try
		{
			CreatorAPI.QuickSignInRequest request = await CreatorAPI.CreateQuickSignInAsync();
			_quickToken = request.Token;
			_expiresAt = request.ExpiresAt;
			_deviceCode.Text = request.Token.ToUpperInvariant();
			Image image = new();
			Error result = image.LoadPngFromBuffer(request.QrPng);
			if (result != Error.Ok) throw new InvalidOperationException($"Could not decode QR image ({result}).");
			_qrCode.Texture = ImageTexture.CreateFromImage(image);
			_status.Text = "Waiting for approval on your signed-in device";
		}
		catch (Exception error)
		{
			GD.PushError($"Could not start Creator QR login: {error}");
			_status.Text = "QR sign-in unavailable. Use the sign-in button.";
		}
		finally { _requestInFlight = false; }
	}

	private async Task PollQuickSignInAsync()
	{
		_pollInFlight = true;
		try { if (await CreatorAPI.TryCompleteQuickSignInAsync(_quickToken)) Hide(); }
		catch (Exception error)
		{
			GD.PushError($"Creator QR login failed: {error}");
			_status.Text = "Could not finish sign-in. Restart to generate a new code.";
			_quickToken = string.Empty;
		}
		finally { _pollInFlight = false; }
	}

	private void OpenBrowserLogin() => _ = CreatorAPI.PromptLogin();
	private void OpenCreateAccount() => OpenExternalUrl(BrickVerse.Shared.Globals.MainEndpoint.PathJoin("/auth/register"));
	private void OpenTerms() => OpenExternalUrl(TermsUrl);
	private void OpenPrivacy() => OpenExternalUrl(PrivacyUrl);
	private void CopyDeviceCode()
	{
		if (string.IsNullOrWhiteSpace(_quickToken)) return;
		DisplayServer.ClipboardSet(_quickToken.ToUpperInvariant());
		_status.Text = "One-time code copied to clipboard";
	}

	private void OpenExternalUrl(string url)
	{
		Error error = OS.ShellOpen(url);
		if (error == Error.Ok) return;
		GD.PushError($"Could not open URL '{url}': {error}");
		_status.Text = "Could not open your browser. Please try again.";
	}

	private void UpdateResponsiveLayout()
	{
		float width = GetViewportRect().Size.X;
		float height = GetViewportRect().Size.Y;
		bool stacked = width < 900f;

		_layout.Vertical = stacked;
		_layout.AddThemeConstantOverride("separation", stacked ? 18 : 38);
		_layout.OffsetLeft = stacked ? 20 : 46;
		_layout.OffsetTop = stacked ? 18 : 42;
		_layout.OffsetRight = stacked ? -20 : -46;
		_layout.OffsetBottom = stacked ? -18 : -42;
		_left.CustomMinimumSize = stacked ? new Vector2(0, 300) : new Vector2(330, 0);
		_right.CustomMinimumSize = stacked ? new Vector2(0, 335) : new Vector2(DesktopDevicePanelWidth, 0);
		_right.SizeFlagsHorizontal = stacked ? Control.SizeFlags.ExpandFill : Control.SizeFlags.ShrinkCenter;

		if (stacked)
		{
			float horizontalMargin = width < 620f ? 12f : 28f;
			float verticalMargin = height < 760f ? 12f : 24f;
			_card.AnchorLeft = 0;
			_card.AnchorRight = 1;
			_card.AnchorTop = 0;
			_card.AnchorBottom = 1;
			_card.OffsetLeft = horizontalMargin;
			_card.OffsetRight = -horizontalMargin;
			_card.OffsetTop = verticalMargin;
			_card.OffsetBottom = -verticalMargin;
		}
		else
		{
			float cardWidth = Math.Min(width * 0.89f, DesktopCardMaxWidth);
			float cardHeight = Math.Min(height * 0.87f, DesktopCardMaxHeight);
			_card.AnchorLeft = 0.5f;
			_card.AnchorRight = 0.5f;
			_card.AnchorTop = 0.5f;
			_card.AnchorBottom = 0.5f;
			_card.OffsetLeft = -cardWidth / 2f;
			_card.OffsetRight = cardWidth / 2f;
			_card.OffsetTop = -cardHeight / 2f;
			_card.OffsetBottom = cardHeight / 2f;
		}
	}
	private void OnUserAuthenticated(OpenIdUserInfoResponse _) => CallDeferred(MethodName.Hide);
	private void OnAuthenticationFailed(string reason) => _status.Text = reason;
	private void OnAuthenticationCleared()
	{
		CallDeferred(MethodName.Show);
		_quickToken = string.Empty;
		_pollElapsed = 0;
		_ = StartQuickSignInAsync();
	}
}

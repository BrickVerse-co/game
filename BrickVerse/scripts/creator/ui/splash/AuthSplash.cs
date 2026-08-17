// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0.

using Godot;
using BrickVerse.Creator.Utils;
using BrickVerse.Schemas.API;
using System;

namespace BrickVerse.Creator.UI;

public partial class AuthSplash : Panel
{
	[Export] private Button _closeButton = null!;
	[Export] private Button _signInButton = null!;
	[Export] private Button _cancelButton = null!;
	[Export] private Label _titleLabel = null!;
	[Export] private Label _descriptionLabel = null!;
	[Export] private Label _infoTextLabel = null!;
	[Export] private Label _stageLabel = null!;
	[Export] private Control _loadingView = null!;
	[Export] private Control _infoBox = null!;
	[Export] private Control _actions = null!;

	private bool _reopeningLogin;
	private bool _showingChecking;

	public override void _Ready()
	{
		_closeButton ??= GetNodeOrNull<Button>("Card/Hero/Close")!;
		_signInButton ??= GetNodeOrNull<Button>("Card/Body/Content/Actions/SignIn")!;
		_cancelButton ??= GetNodeOrNull<Button>("Card/Body/Content/Actions/Cancel")!;
		_titleLabel ??= GetNodeOrNull<Label>("Card/Body/Content/Title")!;
		_descriptionLabel ??= GetNodeOrNull<Label>("Card/Body/Content/Description")!;
		_infoTextLabel ??= GetNodeOrNull<Label>("Card/Body/Content/InfoBox/InfoText")!;
		_stageLabel ??= GetNodeOrNull<Label>("Card/Body/Content/Stage")!;
		_loadingView ??= GetNodeOrNull<Control>("Card/Body/Content/Loading")!;
		_infoBox ??= GetNodeOrNull<Control>("Card/Body/Content/InfoBox")!;
		_actions ??= GetNodeOrNull<Control>("Card/Body/Content/Actions")!;

		Visible = !CreatorAPI.IsUserAuthenticated;
		MouseFilter = MouseFilterEnum.Stop;
		ZIndex = 9999;

		_closeButton.Pressed += ExitCreator;
		_cancelButton.Pressed += ExitCreator;
		_signInButton.Pressed += ReopenLogin;

		CreatorAPI.UserAuthenticated += OnUserAuthenticated;
		CreatorAPI.AuthenticationFailed += OnAuthenticationFailed;

		if (CreatorAPI.IsUserAuthenticated)
			HideSplash();
		else if (CreatorAPI.IsAuthenticationChecking)
			SetCheckingState();
		else
			SetWaitingState();
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;
		if (CreatorAPI.IsUserAuthenticated) { HideSplash(); return; }
		if (_showingChecking && !CreatorAPI.IsAuthenticationChecking) { SetWaitingState(); return; }
	}

	public override void _ExitTree()
	{
		_closeButton.Pressed -= ExitCreator;
		_cancelButton.Pressed -= ExitCreator;
		_signInButton.Pressed -= ReopenLogin;

		CreatorAPI.UserAuthenticated -= OnUserAuthenticated;
		CreatorAPI.AuthenticationFailed -= OnAuthenticationFailed;
	}

	public void ShowSplash()
	{
		if (CreatorAPI.IsUserAuthenticated)
		{
			HideSplash();
			return;
		}

		Visible = true;
		if (CreatorAPI.IsAuthenticationChecking) SetCheckingState(); else SetWaitingState();
	}

	public void HideSplash()
	{
		Visible = false;
	}

	private void ExitCreator()
	{
		GetTree().Quit();
	}

	private void ReopenLogin()
	{
		_ = ReopenLoginAsync();
	}

	private async System.Threading.Tasks.Task ReopenLoginAsync()
	{
		if (_reopeningLogin || CreatorAPI.IsUserAuthenticated)
			return;

		_reopeningLogin = true;
		_signInButton.Disabled = true;
		_signInButton.Text = "Opening...";

		try
		{
			await CreatorAPI.PromptLogin();
			SetWaitingState();
		}
		catch (Exception ex)
		{
			GD.PushError($"Failed to reopen BrickVerse login: {ex}");
			SetErrorState("Unable to open the login page. Try again.");
		}
		finally
		{
			_reopeningLogin = false;

			if (!CreatorAPI.IsUserAuthenticated)
				_signInButton.Disabled = false;
		}
	}

	private void SetWaitingState()
	{
		_showingChecking = false;
		_closeButton.Visible = true;
		_loadingView.Visible = false;
		_infoBox.Visible = true;
		_actions.Visible = true;
		_stageLabel.Text = "SIGN IN REQUIRED";
		_titleLabel.Text = "Finish signing in";
		_descriptionLabel.Text = "Continue in the secure BrickVerse browser page, then return to Creator.";
		_infoTextLabel.Text = "Creator will continue automatically when your account is ready.";

		_signInButton.Visible = true;
		_signInButton.Text = "Open login again";
		_signInButton.Disabled = _reopeningLogin;
		_cancelButton.Text = "Exit Creator";
	}

	private void SetCheckingState()
	{
		_showingChecking = true;
		_closeButton.Visible = false;
		_loadingView.Visible = true;
		_infoBox.Visible = false;
		_actions.Visible = false;
		_stageLabel.Text = "SECURE SESSION";
		_titleLabel.Text = "Signing you in";
		_descriptionLabel.Text = "Checking your saved session and refreshing it securely when needed.";
	}

	private void SetErrorState(string message)
	{
		Visible = true;
		_showingChecking = false;
		_closeButton.Visible = true;
		_loadingView.Visible = false;
		_infoBox.Visible = true;
		_actions.Visible = true;
		_stageLabel.Text = "AUTHENTICATION PROBLEM";
		_signInButton.Visible = true;

		_titleLabel.Text = "Sign in required";
		_descriptionLabel.Text = string.IsNullOrWhiteSpace(message)
			? "Unable to authenticate your BrickVerse account."
			: message;
		_infoTextLabel.Text = "Creator requires a BrickVerse account before you can create, open, or publish projects.";

		_signInButton.Text = "Try again";
		_signInButton.Disabled = false;
		_cancelButton.Text = "Exit Creator";
	}

	private void OnUserAuthenticated(OpenIdUserInfoResponse userInfo)
	{
		CallDeferred(nameof(HideSplash));
	}

	private void OnAuthenticationFailed(string reason)
	{
		CallDeferred(nameof(SetErrorState), reason);
	}
}

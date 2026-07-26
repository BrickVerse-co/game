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

	private bool _reopeningLogin;

	public override void _Ready()
	{
		_closeButton ??= GetNodeOrNull<Button>("Card/Hero/Close")!;
		_signInButton ??= GetNodeOrNull<Button>("Card/Body/Content/Actions/SignIn")!;
		_cancelButton ??= GetNodeOrNull<Button>("Card/Body/Content/Actions/Cancel")!;
		_titleLabel ??= GetNodeOrNull<Label>("Card/Body/Content/Title")!;
		_descriptionLabel ??= GetNodeOrNull<Label>("Card/Body/Content/Description")!;
		_infoTextLabel ??= GetNodeOrNull<Label>("Card/Body/Content/InfoBox/InfoText")!;

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
		else
			SetWaitingState();
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
		SetWaitingState();
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
		_titleLabel.Text = "Waiting for sign in";
		_descriptionLabel.Text = "Your browser should open the secure BrickVerse login page. After signing in, return to Creator.";
		_infoTextLabel.Text = "This window will close automatically once your account is authenticated.";

		_signInButton.Text = "Open login again";
		_signInButton.Disabled = _reopeningLogin;
		_cancelButton.Text = "Exit Creator";
	}

	private void SetErrorState(string message)
	{
		Visible = true;

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

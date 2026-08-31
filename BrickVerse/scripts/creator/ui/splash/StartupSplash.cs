// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.UI.Wizards;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using BrickVerse.Utils;

namespace BrickVerse.Creator.UI.Splashes;

public partial class StartupSplash : Control
{
	private const string LearnUrl = "https://developers.brickverse.gg";
	private const string CommunityUrl = "https://brickverse.gg/forum";
	private const string SupportUrl = "https://brickverse.gg/help";

	[Export] private Button _newButton = null!;
	[Export] private Button _openButton = null!;
	[Export] private Button _recentsButton = null!;
	[Export] private Button _settingsButton = null!;
	[Export] private Button _tutorialButton = null!;
	[Export] private Button _closeButton = null!;
	[Export] private Button _homeButton = null!;
	[Export] private Button _learnButton = null!;
	[Export] private Button _communityButton = null!;
	[Export] private Button _docsButton = null!;
	[Export] private Button _supportButton = null!;
	[Export] private ScrollContainer _recentWorldsScroll = null!;
	[Export] private Label _versionNumber = null!;
	[Export] private MarginContainer _page = null!;

	public static StartupSplash Singleton { get; private set; } = null!;

	public StartupSplash()
	{
		Singleton = this;
	}

	public override void _Ready()
	{
		_newButton.Pressed += OnNew;
		_openButton.Pressed += CreatorService.Interface.PromptOpenWorld;
		_recentsButton.Pressed += OnRecents;
		_settingsButton.Pressed += CreatorService.Interface.OpenSettings;
		_tutorialButton.Pressed += OnTutorial;
		_closeButton.Pressed += Close;

		_homeButton.Pressed += OnHome;
		_learnButton.Pressed += () => OpenExternalUrl(LearnUrl);
		_communityButton.Pressed += () => OpenExternalUrl(CommunityUrl);
		_docsButton.Pressed += () => OpenExternalUrl(LearnUrl);
		_supportButton.Pressed += () => OpenExternalUrl(SupportUrl);

		_versionNumber.Text = $"v{Globals.AppVersion.TrimStart('v')}";
		GetViewport().SizeChanged += UpdateResponsiveLayout;
		UpdateResponsiveLayout();
		base._Ready();
	}

	public override void _ExitTree()
	{
		if (GetViewport() != null)
			GetViewport().SizeChanged -= UpdateResponsiveLayout;
		base._ExitTree();
	}

	private void UpdateResponsiveLayout()
	{
		Vector2 viewport = GetViewportRect().Size;
		float width = Mathf.Round(Mathf.Min(Mathf.Max(viewport.X - 32, 320), 1800));
		float left = Mathf.Round((viewport.X - width) * .5f);
		_page.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_page.Position = new Vector2(left, 0);
		_page.Size = new Vector2(width, Mathf.Round(viewport.Y));
	}

	private static void OpenExternalUrl(string url)
	{
		Error error = OS.ShellOpen(url);
		if (error != Error.Ok)
		{
			GD.PushError($"Could not open URL '{url}': {error}");
		}
	}

	private void OnHome()
	{
		_newButton.GrabFocus();
	}

	private void OnRecents()
	{
		_recentWorldsScroll.GrabFocus();
		_recentWorldsScroll.ScrollVertical = 0;
	}

	private void OnTutorial()
	{
		Close();
		IntroductionWizard.Singleton.Open();
	}

	private void OnNew()
	{
		NewProjectWizard.Singleton.ReturnToSplash = true;
		NewProjectWizard.Singleton.Open();
		Close();
	}

	public void Open()
	{
		_closeButton.Visible = World.Current != null;
		Visible = true;
		_newButton.GrabFocus();
	}

	public void Close()
	{
		Visible = false;
	}
}

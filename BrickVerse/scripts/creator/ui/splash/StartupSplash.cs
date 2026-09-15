// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.UI.Wizards;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using BrickVerse.Utils;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.UI.Splashes.Components;
using BrickVerse.Creator.Managers;
using System;
using System.IO;
using System.Linq;

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
	[Export] private Button _cloudButton = null!;
	[Export] private Button _learnButton = null!;
	[Export] private Button _communityButton = null!;
	[Export] private Button _docsButton = null!;
	[Export] private Button _supportButton = null!;
	[Export] private ScrollContainer _recentWorldsScroll = null!;
	[Export] private Label _versionNumber = null!;
	[Export] private MarginContainer _page = null!;
	[Export] private Control _homePage = null!;
	[Export] private Control _cloudPage = null!;

	public static StartupSplash Singleton { get; private set; } = null!;
	private static bool _startupRecoveryPrompted;

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
		_cloudButton.Pressed += OnCloud;
		_learnButton.Pressed += () => OpenExternalUrl(LearnUrl);
		_communityButton.Pressed += () => OpenExternalUrl(CommunityUrl);
		_docsButton.Pressed += () => OpenExternalUrl(LearnUrl);
		_supportButton.Pressed += () => OpenExternalUrl(SupportUrl);

		_versionNumber.Text = $"v{Globals.AppVersion.TrimStart('v')}";
		GetViewport().SizeChanged += UpdateResponsiveLayout;
		UpdateResponsiveLayout();
		base._Ready();
		PromptStartupRecovery();
	}

	private async void PromptStartupRecovery()
	{
		if (_startupRecoveryPrompted) return;
		_startupRecoveryPrompted = true;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (World.Current != null || CreatorService.CurrentSession != null) return;
		try
		{
			ProjectManager.RecentData? recent = (await ProjectManager.GetRecents(false)).FirstOrDefault();
			if (!recent.HasValue || string.IsNullOrWhiteSpace(recent.Value.FolderPath) || !Directory.Exists(recent.Value.FolderPath)) return;
			string projectName = new DirectoryInfo(recent.Value.FolderPath).Name;
			bool reopen = await CreatorService.Interface.PromptConfirmation($"Reopen {projectName} where you left off?", "Resume your last Creator session", confirmText: "Reopen", cancelText: "Not now");
			if (!reopen || World.Current != null) return;

			string backups = Path.Combine(recent.Value.FolderPath, ".bvproject", "backups");
			string? newest = Directory.Exists(backups) ? Directory.GetDirectories(backups).Where(path => !Path.GetFileName(path).StartsWith(".partial-", StringComparison.Ordinal)).OrderByDescending(path => path).FirstOrDefault() : null;
			if (newest != null)
			{
				ProjectSnapshotManager.SnapshotInfo snapshot = await ProjectSnapshotManager.ValidateAsync(newest);
				if (snapshot.Valid)
				{
					bool recover = await CreatorService.Interface.PromptConfirmation($"An autosave from {snapshot.CreatedUtc.ToLocalTime():g} is available. Restore it before reopening?", "Autosave available", confirmText: "Restore autosave", cancelText: "Open saved version");
					if (recover) await ProjectSnapshotManager.RestoreAsync(newest, recent.Value.FolderPath);
				}
			}

			string target = recent.Value.FolderPath;
			if (!string.IsNullOrWhiteSpace(recent.Value.LastWorldPath))
			{
				string world = Path.GetFullPath(Path.Combine(recent.Value.FolderPath, recent.Value.LastWorldPath));
				if (File.Exists(world)) target = world;
			}
			await CreatorService.Singleton.CreateNewSession(target);
		}
		catch (Exception ex) { BV.PrintWarn($"Could not resume the previous Creator session: {ex.Message}"); }
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (Visible && World.Current != null && @event.IsActionPressed("ui_cancel"))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
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
		SetPage(false);
		_newButton.GrabFocus();
	}

	private void OnCloud()
	{
		SetPage(true);
		if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.RefreshCloudProjectsOnOpen))
			_cloudPage.GetNodeOrNull<RecentPlaceList>("CloudPanel/Margin/Area/Scroll/GridMargin/List")?.Reload();
		_cloudButton.GrabFocus();
	}

	private void SetPage(bool cloud)
	{
		_homePage.Visible = !cloud;
		_cloudPage.Visible = cloud;
		_homeButton.ButtonPressed = !cloud;
		_cloudButton.ButtonPressed = cloud;
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
		_closeButton.Text = World.Current != null ? "Close" : "";
		_closeButton.TooltipText = World.Current != null ? "Return to the open world (Esc)" : "";
		Visible = true;
		SetPage(false);
		_newButton.GrabFocus();
	}

	public void OpenCloud()
	{
		Open();
		OnCloud();
	}

	public void Close()
	{
		Visible = false;
	}
}

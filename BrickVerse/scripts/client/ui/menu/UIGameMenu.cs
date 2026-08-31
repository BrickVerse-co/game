// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;

namespace BrickVerse.Client.UI;

public partial class UIGameMenu : Control
{
	public Vector2 GameMenuSize = new(1120, 640);
	private readonly Dictionary<GameMenuViewEnum, UIMenuViewBase> _loadedViews = [];
	private UIMenuViewBase? _currentView = null;

	[Export] private AnimationPlayer _animPlay = null!;
	[Export] private Control _viewContainer = null!;
	[Export] private Control _firstFocus = null!;
	[Export] private Control _gameMenuPanel = null!;
	[Export] private Label _gameTitle = null!;
	[Export] private Label _viewTitle = null!;
	[Export] private Button _resumeButton = null!;
	[Export] private Button _leaveButton = null!;
	[Export] private Button _closeButton = null!;
	[Export] private Control _contentPanel = null!;
	[Export] private Button _inviteFriendsButton = null!;
	[Export] private Button _controlsButton = null!;
	[Export] private Button _moreButton = null!;
	[Export] private Control _navigationPanel = null!;
	[Export] private Control _contentHeader = null!;
	[Export] private Button _contentCloseButton = null!;
	[Export] private Button _screenshotButton = null!;
	[Export] private Button _respawnButton = null!;
	private readonly List<Button> _railButtons = [];

	public bool IsShowing = false;

	public CoreUIRoot CoreUI = null!;
	public event Action<bool>? IsShowingChanged;
	public event Action<GameMenuViewEnum>? ViewChanged;
	private readonly List<UIMenuTabButton> _tabButtons = [];

	public override void _Ready()
	{
		Visible = false;
		if (_resumeButton != _closeButton) _resumeButton.Pressed += HideMenu;
		_closeButton.Pressed += HideMenu;
		_leaveButton.Pressed += LeaveGame;
		_inviteFriendsButton.Pressed += CopyInviteLink;
		_controlsButton.Pressed += OpenSettings;
		_moreButton.Pressed += OpenOverview;
		_contentCloseButton.Pressed += CloseContent;
		_screenshotButton.Pressed += TakeScreenshot;
		_respawnButton.Pressed += Respawn;
		Resized += RefreshSize;
		_railButtons.AddRange([_inviteFriendsButton, _controlsButton]);
		_railButtons.AddRange(_tabButtons);
	}

	public override void _ExitTree()
	{
		if (_resumeButton != _closeButton) _resumeButton.Pressed -= HideMenu;
		_closeButton.Pressed -= HideMenu;
		_leaveButton.Pressed -= LeaveGame;
		_inviteFriendsButton.Pressed -= CopyInviteLink;
		_controlsButton.Pressed -= OpenSettings;
		_moreButton.Pressed -= OpenOverview;
		_contentCloseButton.Pressed -= CloseContent;
		_screenshotButton.Pressed -= TakeScreenshot;
		_respawnButton.Pressed -= Respawn;
		Resized -= RefreshSize;
		base._ExitTree();
	}

	private void LeaveGame() => CoreUI.Root.Entry?.LeaveGame();
	private void TakeScreenshot() { HideMenu(); CoreUI.Root.Capture.TakePhoto(); }
	private void Respawn() { if (!CoreUI.Service.CanRespawn) return; HideMenu(); CoreUI.Root.Players.LocalPlayer.Kill(); }
	private void CopyInviteLink()
	{
		if (CoreUI.Root.IsLocalTest)
		{
			CoreUI.NotificationCenter.FireMessage("Publish the experience before inviting friends.", "Invite unavailable");
			return;
		}
		DisplayServer.ClipboardSet($"https://brickverse.gg/worlds/{CoreUI.Root.WorldID}");
		CoreUI.NotificationCenter.FireMessage("The experience link was copied to your clipboard.", "Invite friends");
	}
	private void OpenSettings()
	{
		SwitchView(GameMenuViewEnum.Settings);
		if (_loadedViews.TryGetValue(GameMenuViewEnum.Settings, out UIMenuViewBase? view) && view is UIMenuSettings settings)
			settings.SwitchToGameSettings();
	}
	private void OpenOverview() => SwitchView(GameMenuViewEnum.Overview);
	private void CloseContent()
	{
		_currentView?.HideView(); _contentPanel.Visible = false; _navigationPanel.Visible = true; _firstFocus.GrabFocus();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_menu"))
		{
			ToggleMenu();
		}
		base._UnhandledInput(@event);
	}

	public void RegisterTabButton(UIMenuTabButton tabbtn)
	{
		_tabButtons.Add(tabbtn);
	}

	public void ToggleMenu()
	{
		if (IsShowing)
		{
			HideMenu();
		}
		else
		{
			ShowMenu();
		}
	}

	public void ShowMenu()
	{
		if (IsShowing) { return; }
		IsShowing = true;
		_animPlay.Play("appear");
		Visible = true;
		_firstFocus.GrabFocus();
		IsShowingChanged?.Invoke(IsShowing);
		CoreUIRoot.Singleton.Root.Input.IsMenuOpened = true;
		_gameTitle.Text = string.IsNullOrWhiteSpace(CoreUI.Root.UniverseName) ? (CoreUI.Root.IsLocalTest ? "Local Playtest" : "BrickVerse Experience") : CoreUI.Root.UniverseName;
		_respawnButton.Disabled = !CoreUI.Service.CanRespawn;
		_contentPanel.Visible = false;
		_navigationPanel.Visible = true;
		RefreshSize();
	}

	private void RefreshSize()
	{
		Vector2 viewport = GetViewportRect().Size;
		bool narrow = viewport.X < 850;
		float inset = narrow ? 8f : 24f;
		_gameMenuPanel.Position = new Vector2(inset, inset);
		_gameMenuPanel.Size = new Vector2(Mathf.Max(0, viewport.X - inset * 2), Mathf.Max(0, viewport.Y - inset * 2));
		if (_contentPanel.Visible) _navigationPanel.Visible = !narrow;
		_contentHeader.Visible = narrow && _contentPanel.Visible;
		Control header = GetNode<Control>("Shell/Navigation/Margin/Layout/Header");
		bool compact = viewport.Y < 620;
		header.CustomMinimumSize = new Vector2(0, 48);
		float buttonHeight = compact ? Mathf.Clamp((viewport.Y - 190f) / Mathf.Max(1, _railButtons.Count), 34f, 52f) : 52f;
		foreach (Button button in _railButtons) button.CustomMinimumSize = new Vector2(button.CustomMinimumSize.X, buttonHeight);
	}

	public void HideMenu()
	{
		if (!IsShowing) { return; }
		GetViewport().GuiReleaseFocus();
		IsShowing = false;
		_animPlay.Stop(true);
		_animPlay.Play("disappear");
		CoreUIRoot.Singleton.Root.Input.IsMenuOpened = false;
		IsShowingChanged?.Invoke(IsShowing);
		_currentView?.HideView();
	}

	public void SwitchView(GameMenuViewEnum switchTo)
	{
		// Hide the current view if it exists
		if (_currentView != null)
		{
			_currentView.Visible = false;
			_currentView.HideView();
		}

		// Check if the view is already loaded
		if (!_loadedViews.TryGetValue(switchTo, out UIMenuViewBase? view))
		{
			string pathToLoad = switchTo switch
			{
				GameMenuViewEnum.Overview => "res://scenes/client/ui/menu/views/overview.tscn",
				GameMenuViewEnum.Players => "res://scenes/client/ui/menu/views/players.tscn",
				GameMenuViewEnum.Report => "res://scenes/client/ui/menu/views/report.tscn",
				GameMenuViewEnum.Settings => "res://scenes/client/ui/menu/views/settings.tscn",
				_ => throw new ArgumentOutOfRangeException(nameof(switchTo), $"No scene defined for {switchTo}")
			};

			PackedScene scene = GD.Load<PackedScene>(pathToLoad);
			if (scene != null)
			{
				view = scene.Instantiate<UIMenuViewBase>();
				_viewContainer.AddChild(view);
				_loadedViews[switchTo] = view;
			}
			else
			{
				BV.PrintErr("Failed to load settings scene at: " + pathToLoad);
				return;
			}
		}

		// Update first focus
		foreach (UIMenuTabButton tabBtn in _tabButtons)
		{
			if (view.FirstFocus != null)
			{
				tabBtn.FocusNeighborBottom = tabBtn.GetPathTo(view.FirstFocus);
			}
		}

		// Show the new view
		view.Menu = this;
		_contentPanel.Visible = true;
		bool narrow = GetViewportRect().Size.X < 850;
		_navigationPanel.Visible = !narrow;
		_contentHeader.Visible = narrow;
		_viewTitle.Text = switchTo switch { GameMenuViewEnum.Overview => "Overview", GameMenuViewEnum.Players => "Players", GameMenuViewEnum.Report => "Safety & Report", GameMenuViewEnum.Settings => "Settings", _ => "Menu" };
		view.ShowView();
		view.Visible = true;
		ViewChanged?.Invoke(switchTo);
		_currentView = view;
	}

	public enum GameMenuViewEnum
	{
		Overview,
		Players,
		Report,
		Settings
	}
}

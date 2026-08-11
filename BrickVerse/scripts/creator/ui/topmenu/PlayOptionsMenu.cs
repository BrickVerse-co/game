// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Creator.TeamCreate;
using BrickVerse.Shared;
using BrickVerse.Creator.UI.Popups;

namespace BrickVerse.Creator.UI.Menus;

public partial class PlayOptionsMenu : Control
{
	[Export] private Button _playBtn = null!;
	[Export] private Button _playAtCamBtn = null!;
	[Export] private Button _stopBtn = null!;
	[Export] private Button _collaborateBtn = null!;
	[Export] private Button _sessionBtn = null!;
	[Export] private Button _betaFeaturesBtn = null!;
	[Export] private TextureRect _internetStatusIcon = null!;
	[Export] private TextureRect _teamCreateStatusIcon = null!;
	[Export] private OptionButton _playerCountOption = null!;
	private bool _wasLocalTestActive;
	private bool _lastInternetState;
	private bool _lastTeamCreateState;
	private bool _lastTeamCreateConnected;
	private string _lastTeamCreateError = "";
	private bool _statusInitialized;

	public override void _Ready()
	{
		_playBtn.Pressed += OnPlayButtonPressed;
		_playAtCamBtn.Pressed += OnPlayAtCamPressed;
		_stopBtn.Pressed += OnStopButtonPressed;
		_collaborateBtn.Pressed += OnCollaborateButtonPressed;
		_sessionBtn.Pressed += OnSessionButtonPressed;
		_betaFeaturesBtn.Pressed += OnBetaFeaturesPressed;
		_playerCountOption.ItemSelected += OnPlayerCountSelected;
		_playerCountOption.Select(0);
		_playerCountOption.Text = _playerCountOption.GetItemId(0).ToString();

		CreatorService.Singleton.LocalTestStarted.Connect(OnLocalTestStarted);
		CreatorService.Singleton.LocalTestStopped.Connect(OnLocalTestStopped);
		SetPlayTestState(CreatorService.Singleton.LocalTestActive);
		UpdateConnectionIndicators();
	}

	public override void _Process(double delta)
	{
		bool active = CreatorService.Singleton.LocalTestActive;
		if (active != _wasLocalTestActive)
			SetPlayTestState(active);
		UpdateConnectionIndicators();
	}

	private void OnPlayerCountSelected(long index)
	{
		CreatorService.Singleton.LocalTestPlayerCount = (int)_playerCountOption.GetItemId((int)index);
		_playerCountOption.Text = _playerCountOption.GetItemId((int)index).ToString();
	}

	private void OnLocalTestStarted()
	{
		SetPlayTestState(true);
	}

	private void OnLocalTestStopped()
	{
		SetPlayTestState(false);
	}

	private void SetPlayTestState(bool active)
	{
		_wasLocalTestActive = active;
		_playBtn.Disabled = active;
		_playAtCamBtn.Disabled = active;
		_stopBtn.Disabled = !active;
		_stopBtn.Visible = active;
	}

	private void OnPlayButtonPressed()
	{
		CreatorService.Singleton.StartLocalTest();
	}

	private void OnPlayAtCamPressed()
	{
		CreatorService.Singleton.StartLocalTest(true);
	}

	private void OnStopButtonPressed()
	{
		CreatorService.Singleton.StopLocalTest();
	}

	private void OnCollaborateButtonPressed()
	{
		long universeId = World.Current?.UniverseID ?? 0;
		if (universeId == 0)
		{
			CreatorService.Interface.PopupAlert(
				"Publish this world before managing Team Create collaborators.",
				"Team Create"
			);
			return;
		}

		OS.ShellOpen(
			Globals.MainEndpoint.PathJoin(
				$"/creator/worlds/edit/{universeId}?tab=collaborators"
			)
		);
	}

	private void OnSessionButtonPressed()
	{
		TeamCreateService? service = TeamCreateService.Instance;
		if (service == null)
		{
			service = new TeamCreateService();
			CreatorService.Interface.AddChild(service);
		}
		service.ShowSessionWindow();
	}

	private void OnBetaFeaturesPressed()
	{
		BetaFeaturesPopup popup = new();
		CreatorService.Interface.AddChild(popup);
		popup.PopupCentered();
	}

	private void UpdateConnectionIndicators()
	{
		TeamCreateService? service = TeamCreateService.Instance;
		bool internetAvailable = service?.ApiReachable == true;
		bool teamCreateEnabled = service?.TeamCreateEnabled == true;
		bool teamCreateConnected = service?.Connected == true;
		string teamCreateError = service?.LastConnectionError ?? "";
		if (_statusInitialized
			&& internetAvailable == _lastInternetState
			&& teamCreateEnabled == _lastTeamCreateState
			&& teamCreateConnected == _lastTeamCreateConnected
			&& teamCreateError == _lastTeamCreateError)
			return;

		_statusInitialized = true;
		_lastInternetState = internetAvailable;
		_lastTeamCreateState = teamCreateEnabled;
		_lastTeamCreateConnected = teamCreateConnected;
		_lastTeamCreateError = teamCreateError;
		_internetStatusIcon.SelfModulate = internetAvailable
			? new Color("45d483")
			: new Color("ef596f");
		_internetStatusIcon.TooltipText = internetAvailable
			? "BrickVerse services are reachable"
			: "BrickVerse services are unreachable";
		_teamCreateStatusIcon.SelfModulate = teamCreateEnabled
			? new Color("45d483")
			: new Color("ef596f");
		_teamCreateStatusIcon.TooltipText = teamCreateEnabled
			? teamCreateConnected
				? "Team Create enabled and connected"
				: "Team Create enabled; connecting"
			: !string.IsNullOrWhiteSpace(service?.LastConnectionError)
				? service.LastConnectionError
				: "Team Create disabled for this world";
	}
}

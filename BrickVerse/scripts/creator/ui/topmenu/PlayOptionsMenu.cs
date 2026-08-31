// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Creator.TeamCreate;
using BrickVerse.Shared;
using BrickVerse.Creator.UI.Popups;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BrickVerse.Creator.UI.Menus;

public partial class PlayOptionsMenu : Control
{
	private const string DefaultHeadshot = "https://f004.backblazeb2.com/file/brickverse-ugc-public/defaults/headshot.png";

	[Export] private Button _playBtn = null!;
	[Export] private Button _playAtCamBtn = null!;
	[Export] private Button _stopBtn = null!;
	[Export] private Button _collaborateBtn = null!;
	[Export] private Button _sessionBtn = null!;
	[Export] private HBoxContainer _sessionMembers = null!;
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
	private readonly System.Net.Http.HttpClient _headshotHttp = new();
	private readonly Dictionary<string, Texture2D> _headshotCache = [];
	private string _memberSignature = "";

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
		RefreshSessionMembers();
	}

	public override void _Process(double delta)
	{
		bool active = CreatorService.Singleton.LocalTestActive;
		if (active != _wasLocalTestActive)
			SetPlayTestState(active);
		UpdateConnectionIndicators();
		RefreshSessionMembers();
	}

	private void RefreshSessionMembers()
	{
		TeamCreateService? service = TeamCreateService.Instance;
		TeamCreateMember[] members = service?.Connected == true
			? service.Members.ToArray()
			: [];
		string signature = string.Join('|', members.Select(member =>
			$"{member.Id}:{member.Username}:{member.HeadshotUrl}"));
		if (signature == _memberSignature) return;
		_memberSignature = signature;

		foreach (Node child in _sessionMembers.GetChildren()) child.QueueFree();
		_sessionMembers.Visible = members.Length > 0;
		foreach (TeamCreateMember member in members)
			_sessionMembers.AddChild(CreateMemberHeadshot(member));
	}

	private Control CreateMemberHeadshot(TeamCreateMember member)
	{
		PanelContainer frame = new()
		{
			CustomMinimumSize = new Vector2(26, 26),
			TooltipText = member.Username + (member.UserId == TeamCreateService.Instance?.LocalUserId ? " (You)" : ""),
			MouseFilter = Control.MouseFilterEnum.Stop,
			ClipChildren = CanvasItem.ClipChildrenMode.Only,
		};
		StyleBoxFlat style = new()
		{
			BgColor = new Color("263342"),
			BorderColor = member.UserId == TeamCreateService.Instance?.LocalUserId
				? new Color("45d483")
				: new Color("60758a"),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 13,
			CornerRadiusTopRight = 13,
			CornerRadiusBottomLeft = 13,
			CornerRadiusBottomRight = 13,
		};
		frame.AddThemeStyleboxOverride("panel", style);

		TextureRect image = new()
		{
			CustomMinimumSize = new Vector2(24, 24),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		frame.AddChild(image);
		image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		string url = NormalizeHeadshotUrl(member.HeadshotUrl);
		if (_headshotCache.TryGetValue(url, out Texture2D? texture)) image.Texture = texture;
		else _ = LoadHeadshot(url, image);
		return frame;
	}

	private async Task LoadHeadshot(string url, TextureRect target)
	{
		try
		{
			using System.Net.Http.HttpResponseMessage response = await _headshotHttp.GetAsync(url);
			response.EnsureSuccessStatusCode();
			byte[] data = await response.Content.ReadAsByteArrayAsync();
			Image image = new();
			Error error = image.LoadPngFromBuffer(data);
			if (error != Error.Ok) error = image.LoadJpgFromBuffer(data);
			if (error != Error.Ok) error = image.LoadWebpFromBuffer(data);
			if (error != Error.Ok)
			{
				BV.PrintWarn("Could not decode Team Create toolbar headshot from ", url, ": ", error);
				return;
			}
			Texture2D texture = ImageTexture.CreateFromImage(image);
			_headshotCache[url] = texture;
			if (IsInstanceValid(target)) target.Texture = texture;
		}
		catch (Exception error)
		{
			BV.PrintWarn("Could not load Team Create toolbar headshot: ", error.Message);
		}
	}

	private static string NormalizeHeadshotUrl(string url)
	{
		if (string.IsNullOrWhiteSpace(url)) return DefaultHeadshot;
		return Uri.IsWellFormedUriString(url, UriKind.Absolute)
			? url
			: Globals.ApiEndpoint.PathJoin(url);
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

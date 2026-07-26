// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;

namespace BrickVerse.Client.UI;

public partial class UILoadingScreen : Control
{
	[Export] private Label _statusLabel = null!;
	[Export] private ProgressBar _statusProgressbar = null!;
	[Export] public Control? Loader { get; private set; }
	[Export] private TextureRect _gameThumbnailRect = null!;
	[Export] private TextureRect _gameIconRect = null!;
	[Export] private Label _gameTitleLabel = null!;
	[Export] private Label _gameCreatorLabel = null!;
	[Export] private Control _gameDetailsContainer = null!;
	[Export] private AnimationPlayer _animPlay = null!;
	[Export] private AnimationPlayer _bgAnimPlay = null!;

	private BVImageAsset _gameThumbnailImage = null!;
	private BVImageAsset _gameIconImage = null!;
	private ClientEntry _entry = null!;

	private bool _iconAppeared;
	private bool _bgAppeared;
	private bool _hasReplicationProgress;

	public override void _Ready()
	{
		if (GetNodeOrNull("../../") is not ClientEntry)
		{
			Visible = false;
			return;
		}

		_entry = GetNode<ClientEntry>("../../");

		_gameThumbnailImage = new();
		_gameIconImage = new();

		_gameDetailsContainer.Visible = false;

		_gameThumbnailImage.ResourceLoaded += OnGameThumbnailLoaded;
		_gameIconImage.ResourceLoaded += OnGameIconLoaded;

		SetProgress(0, 1);
		SetStatusText("Preparing client...");
		Visible = true;

		if (_entry.IsNetEssentialsReady)
			NetworkEssentialsReady();
		else
			_entry.NetworkEssentialsReady += NetworkEssentialsReady;
	}

	private void NetworkEssentialsReady()
	{
		_entry.NetworkEssentialsReady -= NetworkEssentialsReady;

		_entry.NetworkService.ClientConnectedToServer += OnClientConnectedToServer;
		_entry.NetworkService.ClientWorldReady += OnWorldReady;
		_entry.NetworkService.ClientReady += OnClientReady;
		_entry.NetworkService.ReplicateSync.InstanceLoadedProgress += InstanceLoadedProgress;
		_entry.TargetServerReady += OnServerReady;

		if (_entry.NetworkService.IsServer)
		{
			Visible = false;
			return;
		}

		SetStatusText("Waiting for server...");

		if (_entry.Root.WorldInfo.HasValue)
			OnWorldInfoReady(_entry.Root.WorldInfo.Value);
		else
			_entry.Root.WorldInfoReady += OnWorldInfoReady;

		if (_entry.Root.WorldMedia != null)
			OnWorldMediaReady(_entry.Root.WorldMedia);
		else
			_entry.Root.WorldMediaReady += OnWorldMediaReady;
	}

	private void SetStatusText(string text)
	{
		_statusLabel.Text = text;
		//BV.Print($"LoadingScreen: {text}");
	}

	private void SetProgress(double current, double max)
	{
		_statusProgressbar.MaxValue = Mathf.Max(max, 1);
		_statusProgressbar.Value = Mathf.Clamp(current, 0, _statusProgressbar.MaxValue);
	}

	private void OnServerReady()
	{
		SetStatusText("Server ready. Connecting...");
		SetProgress(0, 1);
	}

	private void OnClientConnectedToServer()
	{
		_hasReplicationProgress = false;
		SetStatusText("Connected. Waiting for world replication...");
		SetProgress(0, 1);
	}

	private void InstanceLoadedProgress(int current, int max)
	{
		_hasReplicationProgress = true;

		Loader?.QueueFree();
		Loader = null;

		SetProgress(current, max);
		SetStatusText($"Replicating world ({current}/{max})...");
	}

	private void OnWorldReady()
	{
		if (!_hasReplicationProgress)
			SetStatusText("World replicated. Waiting for player...");
		else
			SetStatusText("World constructed. Waiting for player...");
	}

	private void OnClientReady()
	{
		SetProgress(1, 1);
		SetStatusText("Ready!");
		_animPlay.Play("load_ready");

		CleanupEvents();
	}

	private void OnWorldInfoReady(APIPlaceInfo info)
	{
		_entry.Root.WorldInfoReady -= OnWorldInfoReady;

		_gameIconImage.ImageType = ImageTypeEnum.WorldThumbnail;
		_gameIconImage.ImageID = info.Id.ToString();
		_gameIconImage.LoadResource();

		_gameTitleLabel.Text = info.Name;
		_gameCreatorLabel.Text = "By " + info.Creator.Name;

		AppearInfo();
	}

	private void OnWorldMediaReady(APIPlaceMedia[] _)
	{
		_entry.Root.WorldMediaReady -= OnWorldMediaReady;

		_gameThumbnailImage.ImageType = ImageTypeEnum.WorldThumbnail;
		_gameThumbnailImage.ImageID = _entry.Root.FirstWorldMedia.ToString();
		_gameThumbnailImage.LoadResource();
	}

	private void OnGameIconLoaded(Resource resource)
	{
		_gameIconRect.Texture = (Texture2D)resource;
	}

	private void OnGameThumbnailLoaded(Resource resource)
	{
		if (_bgAppeared) return;

		_bgAppeared = true;
		_gameThumbnailRect.Texture = (Texture2D)resource;
		_bgAnimPlay.Play("fade_in");
	}

	private void AppearInfo()
	{
		if (_iconAppeared) return;

		_iconAppeared = true;
		_gameDetailsContainer.Visible = true;
		_animPlay.Play("info_appear");
	}

	private void CleanupEvents()
	{
		_entry.TargetServerReady -= OnServerReady;
		_entry.NetworkService.ClientConnectedToServer -= OnClientConnectedToServer;
		_entry.NetworkService.ClientWorldReady -= OnWorldReady;
		_entry.NetworkService.ClientReady -= OnClientReady;
		_entry.NetworkService.ReplicateSync.InstanceLoadedProgress -= InstanceLoadedProgress;

		_gameThumbnailImage.ResourceLoaded -= OnGameThumbnailLoaded;
		_gameIconImage.ResourceLoaded -= OnGameIconLoaded;
	}
}

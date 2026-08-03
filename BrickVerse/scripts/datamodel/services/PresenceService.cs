// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Discord;
using Godot;
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Shared;
using BrickVerse.Schemas.API;
using System;
#if CREATOR
using BrickVerse.Creator.Settings;
#endif

namespace BrickVerse.Datamodel.Services;

[Static("Presence"), ExplorerExclude, SaveIgnore]
public sealed partial class PresenceService : Instance
{
	private const long DiscordAppID = 871308379992260629;
	private string? _state;
	private BVImageAsset? _coverImage;
	private ActivityManager? _activityManager;
	private Discord.Discord? _discord;
	private bool _updateDirty = false;
	private string? _imageURL;
	private static bool _creatorActivityStarted = false;
	private static PresenceService? _creatorPresence;
	private static string _creatorDetails = "Creating a world";
	private static string _creatorState = "";

	private long _startTime = 0;

	[ScriptProperty, SyncVar]
	public string? State
	{
		get => _state;
		set
		{
			_state = value;
			QueueUpdatePresence();
			OnPropertyChanged();
		}
	}

	[ScriptProperty, SyncVar]
	public BVImageAsset? CoverImage
	{
		get => _coverImage;
		set
		{
			if (_coverImage != null && _coverImage != value)
			{
				_coverImage.ResourceLoaded -= OnCoverImageLoaded;
				_coverImage.UnlinkFrom(this);
			}
			_coverImage = value;

			if (_coverImage != null)
			{
				_coverImage.ResourceLoaded += OnCoverImageLoaded;
				_coverImage.LinkTo(this);

				if (_coverImage.IsResourceLoaded && _coverImage.Resource != null)
				{
					OnCoverImageLoaded(_coverImage.Resource);
				}
				else
				{
					_coverImage.QueueLoadResource();
				}
			}

			QueueUpdatePresence();
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		Globals.BeforeQuit += BeforeQuit;
		SetProcess(true);
		base.Init();
	}

	public override void PreDelete()
	{
		Globals.BeforeQuit -= BeforeQuit;
		Root.WorldInfoReady -= OnWorldInfoReady;
		Root.WorldMediaReady -= OnWorldMediaReady;
		base.PreDelete();
	}

	public void BeforeQuit()
	{
		DisposeDiscord();
	}

	public override void Ready()
	{
		base.Ready();
		SetupIntegrations();
		ResetTimer();

		Root.Players.PlayerAdded.Connect((_) => { QueueUpdatePresence(); });
		Root.Players.PlayerRemoved.Connect((_) => { QueueUpdatePresence(); });
		Root.WorldInfoReady += OnWorldInfoReady;
		Root.WorldMediaReady += OnWorldMediaReady;
	}

	private void OnWorldInfoReady(APIPlaceInfo _)
	{
		QueueUpdatePresence();
	}

	private void OnWorldMediaReady(APIPlaceMedia[] media)
	{
		if (CoverImage == null)
			_imageURL = media.Length > 0 ? media[0].Url : null;
		QueueUpdatePresence();
	}

	private void OnCoverImageLoaded(Resource _)
	{
		_imageURL = CoverImage?.DirectImageURL ?? null;
		UpdateIntegrations();
	}

	[ScriptMethod]
	public void ResetTimer()
	{
		_startTime = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
	}

	public override void Process(double delta)
	{
		if (!Root.IsLoaded) return;
		if (_updateDirty)
		{
			_updateDirty = false;
			UpdateIntegrations();
		}
		DiscordTick();
		base.Process(delta);
	}

	private void SetupIntegrations()
	{
		if (Root.SessionType == World.SessionTypeEnum.Creator)
		{
			// TODO: We need a separate global for managing creator presence
			if (_creatorActivityStarted) return;
			_creatorActivityStarted = true;
			_creatorPresence = this;
		}
		try
		{
			SetupDiscord();
			UpdateIntegrations();
		}
		catch
		{
			// ignore the error its lowk annoying
		}
	}

	private void QueueUpdatePresence()
	{
		_updateDirty = true;
	}

#if CREATOR
	public static void SetCreatorActivity(string detailedDetails, string? detailedState = null)
	{
		bool showDetails =
			CreatorSettingsService.Instance != null
			&& CreatorSettingsService.Instance.Get<bool>(
				CreatorSettingKeys.Creator.DetailedRichPresence
			);
		_creatorDetails = showDetails ? detailedDetails : "Creating a world";
		_creatorState = showDetails ? detailedState ?? string.Empty : string.Empty;
		_creatorPresence?.QueueUpdatePresence();
	}
#endif

	private void UpdateIntegrations()
	{
		try
		{
			UpdateDiscord();
		}
		catch { }
	}

	private void SetupDiscord()
	{
		if (!OS.HasFeature("discord-rpc")) return;
		_discord = new(DiscordAppID, (ulong)CreateFlags.NoRequireDiscord);
		_activityManager = _discord.GetActivityManager();
		_activityManager.OnActivityJoin += OnDiscordActivityJoin;
	}

	private void OnDiscordActivityJoin(string secret)
	{
		string[] values = secret.Split(':', 2);
		if (values.Length == 0 || !long.TryParse(values[0], out long worldId)) return;

		string url = Globals.MainEndpoint.PathJoin($"/worlds/{worldId}");
		if (values.Length == 2 && !string.IsNullOrWhiteSpace(values[1]))
			url += "?serverId=" + Uri.EscapeDataString(values[1]);
		OS.ShellOpen(url);
	}

	private void DisposeDiscord()
	{
		_discord = null;
	}

	private void UpdateDiscord()
	{
		if (_activityManager == null) return;

		string details;
		string largeText = "Testing...";

		if (Root.WorldInfo.HasValue)
		{
			details = $"Playing {Root.WorldInfo.Value.Name}";
			largeText = Root.WorldInfo.Value.Name;
		}
		else
		{
			details = "Testing a game";
		}

		string defaultImg = "multiplayer";
		string defaultSmallImg = "app";
		string defaultSmallText = "BrickVerse";

		if (Root.SessionType == World.SessionTypeEnum.Creator)
		{
			defaultImg = "creating";
			defaultSmallImg = "workshop";
			defaultSmallText = "BrickVerse Workshop";
			largeText = "Tinkering";
			details = _creatorDetails;
		}

		bool canJoin = Root.SessionType != World.SessionTypeEnum.Creator
			&& Root.WorldID > 0
			&& !string.IsNullOrWhiteSpace(Root.ServerID)
			&& Root.Players.MaxPlayers > Root.Players.PlayersCount;
		string partyId = canJoin ? $"{Root.WorldID}:{Root.ServerID}" : "";

		Discord.Activity activity = new()
		{
			State = Root.SessionType == World.SessionTypeEnum.Creator
				? _creatorState
				: _state != null ? FilterService.Filter(_state) : "",
			Details = details,
			Timestamps =
			{
				Start = _startTime,
			},
			Assets =
			{
				LargeImage = _imageURL ?? defaultImg,
				LargeText = largeText,
				SmallImage = defaultSmallImg,
				SmallText = defaultSmallText,
			},
			Party =
			{
				Id = partyId,
				Size =
				{
					CurrentSize = Root.Players.PlayersCount,
					MaxSize = Root.Players.MaxPlayers,
				},
				Privacy = ActivityPartyPrivacy.Public,
			},
			Secrets =
			{
				Join = canJoin ? partyId : "",
			},
			Instance = true
		};

		_activityManager.UpdateActivity(activity, (result) =>
		{
		});
	}

	private void DiscordTick()
	{
		if (_discord == null) return;
		try
		{
			_discord.RunCallbacks();
		}
		catch { }
	}
}

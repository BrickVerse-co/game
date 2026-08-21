// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Schemas.API;
using BrickVerse.Utils;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace BrickVerse.Mobile.UI;

public partial class ViewPlaceInfo : MobileViewBase
{
	[Export] private Button _playButton = null!;
	[Export] private Label _genreLabel = null!;
	[Export] private Label _placeNameLabel = null!;
	[Export] private Button _creatorNameLabel = null!;
	[Export] private TextureRect _thumbnailRect = null!;
	[Export] private Control _thumbnailGradient = null!;
	[Export] private MobileMarkdown _descriptionLabel = null!;
	[Export] private Label _playersLabel = null!;
	[Export] private Label _visitsLabel = null!;
	[Export] private Label _capacityLabel = null!;
	[Export] private Label _ratingLabel = null!;
	[Export] private Button _likeButton = null!;
	[Export] private Button _dislikeButton = null!;
	[Export] private Label _ageRatingLabel = null!;
	[Export] private Label _warningsLabel = null!;
	[Export] private Button _backButton = null!;

	private long _worldID;
	private APIPlaceInfo _placeInfo;
	private Control _contentPanel = null!;
	private Control _loadingSkeleton = null!;
	private Tween? _skeletonTween;
	private bool _closing;
	private Label _playLabel = null!;
	private Label _unavailableNotice = null!;

	public override void _Ready()
	{
		CallDeferred(MethodName.ForceInitialLayout);
		_playButton.Pressed += OnPlayButtonPressed;
		_backButton.Pressed += CloseToWorlds;
		_creatorNameLabel.Pressed += OpenCreator;
		_likeButton.Pressed += ToggleLike;
		_dislikeButton.Pressed += ToggleDislike;
		_backButton.Text = "";
		MobileMotion.Bind(_playButton);
		MobileMotion.Bind(_backButton);
		_contentPanel = GetNode<Control>("ScrollContainer/VBoxContainer/PanelContainer");
		_playLabel = _playButton.GetNode<Label>("HBoxContainer/Label");
		_playButton.Reparent(GetNode<VBoxContainer>("ScrollContainer/VBoxContainer/PanelContainer/Layout"));
		_playButton.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		_playButton.CustomMinimumSize = new Vector2(0, 56);
		GetNode<ScrollContainer>("ScrollContainer").OffsetBottom = 0;
		_loadingSkeleton = GetNode<Control>("LoadingSkeleton");
		_unavailableNotice = GetNode<Label>("ScrollContainer/VBoxContainer/PanelContainer/Layout/UnavailableNotice");
		GetNode<Button>("ScrollContainer/VBoxContainer/PanelContainer/Layout/Report").Pressed += () => MobileReportDialog.Open(this, "universe", _placeInfo.UniverseId.ToString());
	}

	private void OpenCreator()
	{
		if (_placeInfo.Creator.Id <= 0) return;
		string creatorId = _placeInfo.Creator.Id.ToString();
		if (_placeInfo.Creator.Type.Equals("GUILD", StringComparison.OrdinalIgnoreCase))
			MobileUI.Singleton.SwitchTo(MobileViewEnum.GuildDetail,
				new MobileRecordDetailArgs(_placeInfo.Creator.Name, "World creator", "View this guild and its worlds in BrickVerse.", _placeInfo.Creator.Thumbnail, MobileViewEnum.PlaceInfo, creatorId));
		else MobileUI.Singleton.SwitchTo(MobileViewEnum.Profile, creatorId);
	}

	private void OnPlayButtonPressed()
	{
		MobileUI.Singleton.LaunchGame(_worldID);
	}

	public override async void ShowView(object? args)
	{
		base.ShowView(args);
		_worldID = Convert.ToInt64(args);
		_closing = false;
		_backButton.Disabled = false;
		PlayEntranceAnimation();
		_genreLabel.Text = "";
		_placeNameLabel.Text = "Loading world...";
		_creatorNameLabel.Text = "Loading details in the background";
		_descriptionLabel.SetMarkdown("Loading...");
		_playButton.Disabled = true;
		_playLabel.Text = "Play";
		_unavailableNotice.Visible = false;
		_loadingSkeleton.Visible = true;
		PulseSkeleton();
		CallDeferred(MethodName.ForceInitialLayout);

		try
		{
			_placeInfo = await BVAPI.GetWorldFromID(_worldID);
			_genreLabel.Text = _placeInfo.Genre;
			_placeNameLabel.Text = _placeInfo.Name;
			_creatorNameLabel.Text = "By " + _placeInfo.Creator.Name;
			_descriptionLabel.SetMarkdown(string.IsNullOrWhiteSpace(_placeInfo.Description) ? "No description provided." : _placeInfo.Description);
			await LoadPlayPermission();
			HideSkeleton();
			_playersLabel.Text = $"{_placeInfo.Playing:N0} playing";
			_visitsLabel.Text = $"{_placeInfo.Visits:N0} visits";
			_capacityLabel.Text = $"{_placeInfo.MaxPlayers:N0} max";
			UpdateRatingDisplay();
			string thumbnailUrl = await BVAPI.GetUniverseThumbnailUrl(_placeInfo.UniverseId);
			if (!string.IsNullOrWhiteSpace(thumbnailUrl))
				WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = thumbnailUrl }, resource => { if (IsInstanceValid(_thumbnailRect)) _thumbnailRect.Texture = (Texture2D)resource; });
		}
		catch (Exception exception)
		{
			HideSkeleton();
			_playButton.Disabled = true;
			_descriptionLabel.SetMarkdown("This world could not be loaded. Please try again.");
			BV.PrintErr(exception);
		}
	}

	private void ForceInitialLayout()
	{
		GetNode<Control>("ScrollContainer/VBoxContainer/Control").CustomMinimumSize = new Vector2(0, 240);
		GetNode<Container>("ScrollContainer/VBoxContainer").QueueSort();
		GetNode<ScrollContainer>("ScrollContainer").QueueSort();
	}

	private void UpdateRatingDisplay()
	{
		int total = _placeInfo.Rating.Likes + _placeInfo.Rating.Dislikes;
		int percent = total == 0 ? 0 : Mathf.RoundToInt(_placeInfo.Rating.Likes * 100f / total);
		_ratingLabel.Text = total == 0 ? "No ratings" : $"{percent}% • {total:N0} ratings";
		_likeButton.Text = $"{(_placeInfo.IsLikedBy ? "▲" : "△")} {_placeInfo.Rating.Likes:N0}";
		_dislikeButton.Text = $"{(_placeInfo.IsDislikedBy ? "▼" : "▽")} {_placeInfo.Rating.Dislikes:N0}";
		_likeButton.Modulate = _placeInfo.IsLikedBy ? Color.FromHtml("#35C978") : Colors.White;
		_dislikeButton.Modulate = _placeInfo.IsDislikedBy ? Color.FromHtml("#ED5C5C") : Colors.White;
		_ageRatingLabel.Text = "Age rating: " + (_placeInfo.AgeRating switch { "ALL_AGES" => "All Ages", "AGE_9_PLUS" => "9+", "AGE_13_PLUS" => "13+", "AGE_17_PLUS" => "17+", _ => "Rating Pending" });
		_warningsLabel.Text = _placeInfo.ContentWarnings is { Length: > 0 }
			? "Content descriptors: " + string.Join(", ", _placeInfo.ContentWarnings.Select(warning => warning.Replace('_', ' ').ToLowerInvariant()))
			: "Content descriptors: None";
	}

	private async void ToggleLike()
	{
		bool removing = _placeInfo.IsLikedBy;
		try
		{
			using JsonDocument _ = await BVAPI.SendJson(removing ? HttpMethod.Delete : HttpMethod.Post, $"/v3/world/rating/{_placeInfo.UniverseId}/{_placeInfo.Id}/like");
			APIPlaceRating rating = _placeInfo.Rating;
			rating.Likes = Math.Max(0, rating.Likes + (removing ? -1 : 1));
			if (!removing && _placeInfo.IsDislikedBy) { rating.Dislikes = Math.Max(0, rating.Dislikes - 1); _placeInfo.IsDislikedBy = false; }
			_placeInfo.Rating = rating;
			_placeInfo.IsLikedBy = !removing;
			UpdateRatingDisplay();
		}
		catch (Exception exception) { OS.Alert(exception.Message, "Rating failed"); }
	}

	private async void ToggleDislike()
	{
		bool removing = _placeInfo.IsDislikedBy;
		try
		{
			using JsonDocument _ = await BVAPI.SendJson(removing ? HttpMethod.Delete : HttpMethod.Post, $"/v3/world/rating/{_placeInfo.UniverseId}/{_placeInfo.Id}/dislike");
			APIPlaceRating rating = _placeInfo.Rating;
			rating.Dislikes = Math.Max(0, rating.Dislikes + (removing ? -1 : 1));
			if (!removing && _placeInfo.IsLikedBy) { rating.Likes = Math.Max(0, rating.Likes - 1); _placeInfo.IsLikedBy = false; }
			_placeInfo.Rating = rating;
			_placeInfo.IsDislikedBy = !removing;
			UpdateRatingDisplay();
		}
		catch (Exception exception) { OS.Alert(exception.Message, "Rating failed"); }
	}

	private async System.Threading.Tasks.Task LoadPlayPermission()
	{
		using JsonDocument document = await BVAPI.GetJson($"/v3/universe/{_placeInfo.UniverseId}/permissions");
		JsonElement root = document.RootElement;
		bool canPlay = root.TryGetProperty("canPlay", out JsonElement allowed) && allowed.ValueKind == JsonValueKind.True;
		string reason = root.TryGetProperty("playDeniedReason", out JsonElement reasonNode) && reasonNode.ValueKind == JsonValueKind.String
			? reasonNode.GetString() ?? "" : "";
		_playButton.Disabled = !canPlay;
		_playLabel.Text = canPlay ? "Play" : "Unavailable";
		_playLabel.Visible = !canPlay;
		_unavailableNotice.Visible = !canPlay && !string.IsNullOrWhiteSpace(reason);
		_unavailableNotice.Text = reason;
	}

	private void PulseSkeleton()
	{
		_loadingSkeleton.Modulate = Colors.White;
		_skeletonTween?.Kill();
		_skeletonTween = CreateTween().SetLoops().SetTrans(Tween.TransitionType.Sine);
		_skeletonTween.TweenProperty(_loadingSkeleton, "modulate:a", 0.92f, 0.7);
		_skeletonTween.TweenProperty(_loadingSkeleton, "modulate:a", 1f, 0.7);
	}

	private void HideSkeleton()
	{
		if (!IsInstanceValid(_loadingSkeleton)) return;
		_skeletonTween?.Kill();
		Tween tween = CreateTween();
		tween.TweenProperty(_loadingSkeleton, "modulate:a", 0f, 0.18);
		tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(_loadingSkeleton)) _loadingSkeleton.Visible = false; }));
	}

	private void PlayEntranceAnimation()
	{
		_thumbnailRect.PivotOffset = _thumbnailRect.Size / 2f;
		_thumbnailRect.Scale = new Vector2(1.1f, 1.1f);
		_thumbnailRect.Modulate = new Color(1, 1, 1, 0);
		_contentPanel.Modulate = new Color(1, 1, 1, 0);
		_playButton.Modulate = new Color(1, 1, 1, 0);
		Tween tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_thumbnailRect, "scale", Vector2.One, 0.42);
		tween.TweenProperty(_thumbnailRect, "modulate:a", 1f, 0.3);
		tween.TweenProperty(_contentPanel, "modulate:a", 1f, 0.28).SetDelay(0.06);
		tween.TweenProperty(_playButton, "modulate:a", 1f, 0.25).SetDelay(0.12);
	}

	private void CloseToWorlds()
	{
		if (_closing) return;
		_closing = true;
		_backButton.Disabled = true;
		Tween tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
		tween.TweenProperty(_thumbnailRect, "scale", new Vector2(1.06f, 1.06f), 0.18);
		tween.TweenProperty(_contentPanel, "modulate:a", 0f, 0.16);
		tween.Chain().TweenCallback(Callable.From(() => MobileUI.Singleton.SwitchTo(MobileViewEnum.Worlds)));
	}
}

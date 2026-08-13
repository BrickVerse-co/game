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

namespace BrickVerse.Mobile.UI;

public partial class ViewPlaceInfo : MobileViewBase
{
	[Export] private Button _playButton = null!;
	[Export] private Label _genreLabel = null!;
	[Export] private Label _placeNameLabel = null!;
	[Export] private Label _creatorNameLabel = null!;
	[Export] private TextureRect _thumbnailRect = null!;
	[Export] private Control _thumbnailGradient = null!;
	[Export] private Label _descriptionLabel = null!;
	[Export] private Label _statsLabel = null!;
	[Export] private Button _backButton = null!;

	private long _worldID;
	private APIPlaceInfo _placeInfo;
	private Control _contentPanel = null!;
	private bool _closing;

	public override void _Ready()
	{
		_playButton.Pressed += OnPlayButtonPressed;
		_backButton.Pressed += CloseToWorlds;
		_backButton.Text = "";
		MobileMotion.Bind(_playButton);
		MobileMotion.Bind(_backButton);
		_contentPanel = GetNode<Control>("ScrollContainer/VBoxContainer/PanelContainer");
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
		_descriptionLabel.Text = "Loading...";
		_playButton.Disabled = true;

		try
		{
			_placeInfo = await BVAPI.GetWorldFromID(_worldID);
			_genreLabel.Text = _placeInfo.Genre;
			_placeNameLabel.Text = _placeInfo.Name;
			_creatorNameLabel.Text = "By " + _placeInfo.Creator.Name;
			_descriptionLabel.Text = string.IsNullOrWhiteSpace(_placeInfo.Description) ? "No description provided." : _placeInfo.Description;
			_playButton.Disabled = false;
			_statsLabel.Text = $"{_placeInfo.Playing:N0} playing  •  {_placeInfo.Visits:N0} visits  •  {_placeInfo.MaxPlayers:N0} max players";
			string thumbnailUrl = await BVAPI.GetUniverseThumbnailUrl(_placeInfo.UniverseId);
			if (!string.IsNullOrWhiteSpace(thumbnailUrl))
				WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = thumbnailUrl }, resource => { if (IsInstanceValid(_thumbnailRect)) _thumbnailRect.Texture = (Texture2D)resource; });
		}
		catch (Exception exception)
		{
			_playButton.Disabled = true;
			_descriptionLabel.Text = "This world could not be loaded. Please try again.";
			BV.PrintErr(exception);
		}
	}

	private void PlayEntranceAnimation()
	{
		_thumbnailRect.PivotOffset = _thumbnailRect.Size / 2f;
		_thumbnailRect.Scale = new Vector2(1.1f, 1.1f);
		_thumbnailRect.Modulate = new Color(1, 1, 1, 0);
		_contentPanel.Position = new Vector2(0, 44);
		_contentPanel.Modulate = new Color(1, 1, 1, 0);
		Vector2 playTarget = _playButton.Position;
		_playButton.Position = playTarget + new Vector2(0, 28);
		_playButton.Modulate = new Color(1, 1, 1, 0);
		Tween tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_thumbnailRect, "scale", Vector2.One, 0.42);
		tween.TweenProperty(_thumbnailRect, "modulate:a", 1f, 0.3);
		tween.TweenProperty(_contentPanel, "position:y", 0f, 0.36).SetDelay(0.06);
		tween.TweenProperty(_contentPanel, "modulate:a", 1f, 0.28).SetDelay(0.06);
		tween.TweenProperty(_playButton, "position", playTarget, 0.34).SetDelay(0.12);
		tween.TweenProperty(_playButton, "modulate:a", 1f, 0.25).SetDelay(0.12);
	}

	private void CloseToWorlds()
	{
		if (_closing) return;
		_closing = true;
		_backButton.Disabled = true;
		Tween tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
		tween.TweenProperty(_thumbnailRect, "scale", new Vector2(1.06f, 1.06f), 0.18);
		tween.TweenProperty(_contentPanel, "position:y", 32f, 0.18);
		tween.TweenProperty(_contentPanel, "modulate:a", 0f, 0.16);
		tween.Chain().TweenCallback(Callable.From(() => MobileUI.Singleton.SwitchTo(MobileViewEnum.Worlds)));
	}
}

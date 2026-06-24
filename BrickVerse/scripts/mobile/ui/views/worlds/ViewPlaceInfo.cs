// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Utils;

namespace Polytoria.Mobile.UI;

public partial class ViewPlaceInfo : MobileViewBase
{
	[Export] private Button _playButton = null!;
	[Export] private Label _genreLabel = null!;
	[Export] private Label _placeNameLabel = null!;
	[Export] private Label _creatorNameLabel = null!;
	[Export] private TextureRect _thumbnailRect = null!;
	[Export] private Control _thumbnailGradient = null!;

	private int _worldID;
	private APIPlaceInfo _placeInfo;

	public override void _Ready()
	{
		_playButton.Pressed += OnPlayButtonPressed;
	}

	private void OnPlayButtonPressed()
	{
		MobileUI.Singleton.LaunchGame(_worldID);
	}

	public override async void ShowView(object? args)
	{
		base.ShowView(args);
		_worldID = (int)args!;
		_genreLabel.Text = "";
		_placeNameLabel.Text = "";
		_creatorNameLabel.Text = "";

		MobileUI.Singleton.LoadingScreen.ShowScreen();

		_placeInfo = await PolyAPI.GetWorldFromID(_worldID);
		_genreLabel.Text = _placeInfo.Genre;
		_placeNameLabel.Text = _placeInfo.Name;
		_creatorNameLabel.Text = "By " + _placeInfo.Creator.Name;

		MobileUI.Singleton.LoadingScreen.HideScreen();
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Schemas.API;

namespace BrickVerse.Mobile.UI;

public partial class PlaceCard : Button
{
	[Export] private TextureRect _iconRect = null!;
	[Export] private Label _gameTitleLabel = null!;
	[Export] private Label _playingLabel = null!;
	[Export] private Label _ratingLabel = null!;

	public APIWorldsData PlaceData;

	private readonly PTImageAsset _iconAsset = new();


	public override void _Ready()
	{
		_iconAsset.ResourceLoaded += OnIconLoaded;

		_gameTitleLabel.Text = PlaceData.Name;
		_playingLabel.Text = PlaceData.Playing.ToString();
		if (PlaceData.Rating != null)
		{
			_ratingLabel.Text = Mathf.Round((double)(PlaceData.Rating * 100)).ToString() + "%";
		}
		else
		{
			_ratingLabel.Text = "--";
		}

		_iconAsset.ImageType = ImageTypeEnum.PlaceIcon;
		_iconAsset.ImageID = PlaceData.Id.ToString();
		_iconAsset.LoadResource();

		Pressed += OnPressed;
	}

	private void OnPressed()
	{
		MobileUI.Singleton.SwitchTo(MobileViewEnum.PlaceInfo, PlaceData.Id);
	}

	private void OnIconLoaded(Resource tex)
	{
		_iconRect.Texture = (Texture2D)tex;
	}
}

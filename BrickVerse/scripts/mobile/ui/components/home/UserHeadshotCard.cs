// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;

namespace BrickVerse.Mobile.UI;

public partial class UserHeadshotCard : Node
{
	[Export] public string UserID = string.Empty;

	[Export] private TextureRect _imageRect = null!;
	[Export] private Label _usernameLabel = null!;

	private readonly BVImageAsset _iconAsset = new();
	private bool _disposed;

	public override void _Ready()
	{
		_imageRect.Texture = null;
		_usernameLabel.Text = "";
		_iconAsset.ResourceLoaded += OnIconLoaded;
		LoadUserCard();
	}

	private void OnIconLoaded(Resource resource)
	{
		if (!_disposed && IsInstanceValid(_imageRect)) _imageRect.Texture = (Texture2D)resource;
	}

	public override void _ExitTree()
	{
		_disposed = true;
		_iconAsset.ResourceLoaded -= OnIconLoaded;
		base._ExitTree();
	}

	public async void LoadUserCard()
	{
		if (string.IsNullOrWhiteSpace(UserID))
		{
			QueueFree();
			return;
		}
		_iconAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
		_iconAsset.ImageID = UserID.ToString();
		_iconAsset.LoadResource();

		try
		{
			APIUserInfo userData = await BVAPI.GetUserFromID(UserID.ToString());

			if (!_disposed && IsInstanceValid(_usernameLabel)) _usernameLabel.Text = userData.Username;
		}
		catch (Exception ex)
		{
			BV.PrintErr(ex);
		}
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Utils;

namespace BrickVerse.Client.UI;

public sealed partial class UIMenuOverview : UIMenuViewBase
{
	[Export] private Label _placeTypeLabel = null!;
	[Export] private Label _placeNameLabel = null!;
	[Export] private Label _placeCreatorLabel = null!;
	[Export] private TextureRect _placeThumbnailRect = null!;
	[Export] private TextureRect _statPlayerImage = null!;
	[Export] private Label _statPlayerNameLabel = null!;
	[Export] private Label _statTimePlayedLabel = null!;
	[Export] private Label _statPlayerCountLabel = null!;
	[Export] private Label _statInstanceCountLabel = null!;
	[Export] private Button _reportButton = null!;
	private TextureRect _hero = null!;

	private BVImageAsset? _userAvatarImage;
	private BVImageAsset? _placeThumbnailImage;

	public override void _Ready()
	{
		_hero = GetNode<TextureRect>("Content/Hero");
		_reportButton.Pressed += OnReport;
		Resized += RefreshResponsiveLayout;
		RefreshResponsiveLayout();
		base._Ready();
	}

	public override void _ExitTree()
	{
		_reportButton.Pressed -= OnReport;
		Resized -= RefreshResponsiveLayout;
		base._ExitTree();
	}

	private void OnReport()
	{
		if (Menu.CoreUI.Root.IsLocalTest) return;
		OS.ShellOpen("https://brickverse.gg/report?type=universe&id=" + Menu.CoreUI.Root.WorldID);
	}

	private void RefreshResponsiveLayout()
	{
		float width = Size.X;
		_hero.CustomMinimumSize = new Vector2(0, width < 520 ? 150 : 190);
	}

	public override void ShowView()
	{
		SetProcess(true);

		World root = Menu.CoreUI.Root;
		if (root.WorldInfo.HasValue)
		{
			_placeTypeLabel.Visible = true;
			_placeCreatorLabel.Visible = true;
			_placeNameLabel.Text = root.WorldInfo.Value.Name;
			_placeTypeLabel.Text = root.WorldInfo.Value.Genre.Capitalize();
			_placeCreatorLabel.Text = "By " + root.WorldInfo.Value.Creator.Name;

			if (_placeThumbnailImage == null)
			{
				_placeThumbnailImage = new();
				_placeThumbnailImage.ResourceLoaded += OnWorldThumbnailLoaded;
				_placeThumbnailImage.ImageType = ImageTypeEnum.WorldThumbnail;
				_placeThumbnailImage.ImageID = root.FirstWorldMedia;
				_placeThumbnailImage.LoadResource();
			}
		}
		else
		{
			_placeTypeLabel.Visible = false;
			_placeCreatorLabel.Visible = false;
			if (root.WorldID == 0)
			{
				_placeNameLabel.Text = "Local Testing";
			}
			else
			{
				_placeNameLabel.Text = "Unknown";
			}
		}

		_statPlayerNameLabel.Text = root.Players.LocalPlayer.Name;

		if (_userAvatarImage == null)
		{
			_userAvatarImage = new();
			_userAvatarImage.ResourceLoaded += OnAvatarImageLoaded;
			_userAvatarImage.ImageType = ImageTypeEnum.UserAvatar;
			_userAvatarImage.ImageID = root.Players.LocalPlayer.UserID;
			_userAvatarImage.LoadResource();
		}

		base.ShowView();
	}

	private void OnWorldThumbnailLoaded(Resource resource)
	{
		_placeThumbnailRect.Texture = (Texture2D)resource;
	}

	private void OnAvatarImageLoaded(Resource resource)
	{
		_statPlayerImage.Texture = (Texture2D)resource;
	}

	public override void _Process(double delta)
	{
		World root = Menu.CoreUI.Root;
		_statInstanceCountLabel.Text = root.InstanceCount.ToString() + " Instances";
		_statTimePlayedLabel.Text = "Playing for " + TimeUtils.FormatSeconds((long)root.UpTime);
		_statPlayerCountLabel.Text = root.Players.PlayersCount.ToString() + " players in the server";
		base._Process(delta);
	}

	public override void HideView()
	{
		SetProcess(false);
		base.HideView();
	}
}

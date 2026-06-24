// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Resources;

namespace BrickVerse.Client.UI;

public partial class UIPlayerCard : Node
{
	[Export] private Button _reportButton = null!;
	[Export] private Button _profileButton = null!;

	public Player TargetPlayer = null!;

	private PTImageAsset _plrIconAsset = null!;

	public override void _Ready()
	{
		_plrIconAsset = new();
		GetNode<Label>("Layout/Label").Text = TargetPlayer.Name;

		_plrIconAsset.ResourceLoaded += OnIconLoaded;
		_plrIconAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
		_plrIconAsset.ImageID = TargetPlayer.UserID.ToString();
		_plrIconAsset.LoadResource();

		_profileButton.Pressed += OnProfile;
		_reportButton.Pressed += OnReport;
	}

	private void OnReport()
	{
		OS.ShellOpen("https://brickverse.gg/report?type=user&id=" + TargetPlayer.UserID);
	}

	private void OnProfile()
	{
		OS.ShellOpen("https://brickverse.gg/users/" + TargetPlayer.UserID);
	}

	public override void _ExitTree()
	{
		_plrIconAsset.ResourceLoaded -= OnIconLoaded;
		_profileButton.Pressed -= OnProfile;
		_reportButton.Pressed -= OnReport;
		base._ExitTree();
	}

	private void OnIconLoaded(Resource resource)
	{
		GetNode<TextureRect>("Layout/Icon").Texture = (Texture2D)resource;
	}
}

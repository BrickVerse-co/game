// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Humanizer;
using BrickVerse.Creator.Managers;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;

namespace BrickVerse.Creator.UI.Splashes.Components;

public partial class RecentPlaceCard : Button
{
	public ProjectManager.RecentData Data { get; set; }
	public RecentPlaceList ListUI { get; set; } = null!;

	[Export] private Label _placeTitleLabel = null!;
	[Export] private Label _recentOpenLabel = null!;
	[Export] private Label _pathLabel = null!;
	[Export] private TextureRect _thumbnail = null!;
	[Export] private MenuButton _menuLabel = null!;

	public override void _Ready()
	{
		_placeTitleLabel.Text = Data.PlaceName;
		_recentOpenLabel.Text = Data.LastOpened.Humanize();
		_pathLabel.Text = Data.FolderPath;
		_pathLabel.TooltipText = Data.FolderPath;

		if (Data.IconID is > 0)
		{
			string thumbnailUrl = Globals.ApiEndpoint.PathJoin(
				"/v3/thumbnails/asset/" + Data.IconID.Value
			);
			WebAssetLoader.Singleton.GetResource(new() { URL = thumbnailUrl }, resource =>
			{
				if (IsInstanceValid(_thumbnail) && resource is Texture2D texture)
					_thumbnail.Texture = texture;
			});
		}

		PopupMenu menu = _menuLabel.GetPopup();
		menu.IdPressed += OnMenu;

		base._Ready();
	}

	private async void OnMenu(long id)
	{
		switch (id)
		{
			case 91: // Remove from Recents
				if (!await CreatorService.Interface.PromptConfirmation("Are you sure you want to remove this from recents? This won't delete the project from your file system")) return;
				await ProjectManager.RemoveFromRecents(Data.FolderPath);
				ListUI.Reload();
				break;
		}
	}

	public override async void _Pressed()
	{
		Disabled = true;
		try
		{
			await CreatorService.Singleton.CreateNewSession(Data.FolderPath);
		}
		catch
		{
			// CreateNewSession reports the detailed error and restores the landing page.
		}
		finally
		{
			if (IsInstanceValid(this)) Disabled = false;
		}
		base._Pressed();
	}
}

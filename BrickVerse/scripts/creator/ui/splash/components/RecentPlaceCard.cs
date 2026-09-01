// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Humanizer;
using BrickVerse.Creator.Managers;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Schemas.API;
using System;
using BrickVerse.Creator.Settings;

namespace BrickVerse.Creator.UI.Splashes.Components;

public partial class RecentPlaceCard : Button
{
	public ProjectManager.RecentData Data { get; set; }
	public RecentPlaceList ListUI { get; set; } = null!;
	public CreatorPlaceItem? CloudWorld { get; set; }
	public string CloudOwner { get; set; } = "";
	public bool IsDownloaded { get; set; }

	[Export] private Label _placeTitleLabel = null!;
	[Export] private Label _recentOpenLabel = null!;
	[Export] private Label _pathLabel = null!;
	[Export] private TextureRect _thumbnail = null!;
	[Export] private MenuButton _menuLabel = null!;

	public override void _Ready()
	{
		CreatorPlaceItem? cloud = CloudWorld;
		_placeTitleLabel.Text = cloud?.Name ?? Data.PlaceName;
		DateTime activity = cloud?.UpdatedAt ?? cloud?.CreatedAt ?? Data.LastOpened;
		_recentOpenLabel.Text = cloud.HasValue ? $"Updated {activity.Humanize()}" : Data.LastOpened.Humanize();
		_pathLabel.Text = cloud.HasValue
			? IsDownloaded ? $"Downloaded · {Data.FolderPath}" : $"Cloud · {CloudOwner} · Download to edit"
			: Data.FolderPath;
		_pathLabel.TooltipText = cloud.HasValue && !IsDownloaded
			? "This project is in BrickVerse Cloud. Select it to download and begin editing."
			: Data.FolderPath;
		_menuLabel.Visible = !string.IsNullOrWhiteSpace(Data.FolderPath);

		string thumbnailUrl = cloud?.IconUrl ?? Data.ThumbnailUrl;
		if (string.IsNullOrWhiteSpace(thumbnailUrl) && Data.IconID is > 0)
		{
			thumbnailUrl = Globals.ApiEndpoint.PathJoin(
				"/v3/thumbnails/asset/" + Data.IconID.Value
			);
		}
		if (!string.IsNullOrWhiteSpace(thumbnailUrl))
		{
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
			if (CloudWorld.HasValue && string.IsNullOrWhiteSpace(Data.FolderPath)
				&& CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.ConfirmCloudDownload)
				&& !await CreatorService.Interface.PromptConfirmation(
					$"Download '{CloudWorld.Value.Name}' and create a local project?"))
			{
				return;
			}
			StartupSplash.Singleton.Close();
			string projectName = CloudWorld?.Name ?? Data.PlaceName;
			CreatorService.Interface.LoadOverlay?.SetTitle("Opening " + projectName);
			CreatorService.Interface.LoadOverlay?.SetStatus("Preparing project...");
			CreatorService.Interface.LoadOverlay?.Show();
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (!string.IsNullOrWhiteSpace(Data.FolderPath))
				await CreatorService.Singleton.CreateNewSession(Data.FolderPath);
			else if (CloudWorld.HasValue)
				await CreatorService.Singleton.CreateNewSessionByWorldId((CloudWorld.Value.WorldId ?? CloudWorld.Value.Id).ToString());
		}
		catch
		{
			// CreateNewSession reports the detailed error and restores the landing page.
			StartupSplash.Singleton.Open();
		}
		finally
		{
			if (IsInstanceValid(this)) Disabled = false;
		}
		base._Pressed();
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Humanizer;
using Polytoria.Creator.Managers;
using Polytoria.Datamodel.Creator;

namespace Polytoria.Creator.UI.Splashes.Components;

public partial class RecentPlaceCard : Button
{
	public ProjectManager.RecentData Data { get; set; }
	public RecentPlaceList ListUI { get; set; } = null!;

	[Export] private Label _placeTitleLabel = null!;
	[Export] private Label _recentOpenLabel = null!;
	[Export] private MenuButton _menuLabel = null!;

	public override void _Ready()
	{
		_placeTitleLabel.Text = Data.PlaceName;
		_recentOpenLabel.Text = Data.LastOpened.Humanize();

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

	public override void _Pressed()
	{
		_ = CreatorService.Singleton.CreateNewSession(Data.FolderPath);
		base._Pressed();
	}
}

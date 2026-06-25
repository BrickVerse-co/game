// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Client.WebAPI;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using SystemNetHttp = System.Net.Http;

namespace BrickVerse.Creator.UI;

public partial class StatusBar : Control
{
	[Export] private Label _titleLabel = null!;
	[Export] private Label _versionLabel = null!;
	[Export] private TextureRect _userAvatar = null!;
	[Export] private Label _userInfoLabel = null!;

	public override void _Ready()
	{
		CreatorService.Interface.StatusBar = this;
		_versionLabel.Text = $"BrickVerse Creator {Globals.AppVersion}";

		ClientAuthAPI.UserAuthenticated += UpdateUserDisplay;
#if CREATOR
		if (CreatorAPI.IsUserAuthenticated)
		{
			UpdateUserDisplay(new Schemas.API.APIV3AuthMeUser
			{
				Id = CreatorAPI.UserID,
				Username = CreatorAPI.UserInfo.Username,
			});
		}
#endif
		base._Ready();
	}

	private async void UpdateUserDisplay(Schemas.API.APIV3AuthMeUser me)
	{
		if (_userInfoLabel != null)
			_userInfoLabel.Text = $"{me.Username} ({me.Id})";

		if (_userAvatar != null)
		{
			try
			{
				string thumbUrl = Globals.ApiEndpoint.PathJoin($"/v3/user/{me.Id}/thumbnail?size=48");
				using var resp = await new SystemNetHttp.HttpClient().GetAsync(thumbUrl);
				if (resp.IsSuccessStatusCode)
				{
					byte[] data = await resp.Content.ReadAsByteArrayAsync();
					var img = new Image();
					img.LoadPngFromBuffer(data);
					var tex = ImageTexture.CreateFromImage(img);
					_userAvatar.Texture = tex;
				}
			}
			catch { }
		}
	}

	public void SetStatus(string text)
	{
		_titleLabel.Text = text;
	}

	public void SetEmpty()
	{
		_titleLabel.Text = "";
	}
}

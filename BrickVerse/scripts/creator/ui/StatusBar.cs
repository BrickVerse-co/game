// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Client.WebAPI;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using BrickVerse.Schemas.API;
using SystemNetHttp = System.Net.Http;

namespace BrickVerse.Creator.UI;

public partial class StatusBar : Control
{
	[Export] private Label _titleLabel = null!;
	[Export] private Label _versionLabel = null!;
	[Export] private TextureRect _userAvatar = null!;
	[Export] private Label _userInfoLabel = null!;

	private readonly SystemNetHttp.HttpClient _http = new();

	public override void _Ready()
	{
		CreatorService.Interface.StatusBar = this;
		string commit = Globals.ShortBuildCommit;
		_versionLabel.Text = string.IsNullOrWhiteSpace(commit)
			? $"BrickVerse Creator {Globals.AppVersion}"
			: $"BrickVerse Creator {Globals.AppVersion}  •  {commit}";
		_versionLabel.TooltipText = string.IsNullOrWhiteSpace(Globals.BuildCommit)
			? "Build commit unavailable"
			: $"Git commit: {Globals.BuildCommit}";

#if CREATOR
		CreatorAPI.UserAuthenticated += UpdateUserDisplay;

		if (CreatorAPI.CurrentUserInfo.HasValue)
			UpdateUserDisplay(CreatorAPI.CurrentUserInfo.Value);
#endif

		base._Ready();
	}


#if CREATOR
	private void UpdateCreatorUserDisplay()
	{
		if (!CreatorAPI.CurrentUserInfo.HasValue)
			return;

		UpdateUserDisplay(CreatorAPI.CurrentUserInfo.Value);
	}
#endif

	private async void UpdateUserDisplay(OpenIdUserInfoResponse me)
	{
		string username = !string.IsNullOrWhiteSpace(me.PreferredUsername)
			? me.PreferredUsername
			: me.Name;

		string userId = me.Sub;

		if (_userInfoLabel != null)
			_userInfoLabel.Text = $"{username} ({userId})";

		if (_userAvatar == null)
			return;

		try
		{
			string thumbUrl = !string.IsNullOrWhiteSpace(me.Picture)
				? me.Picture
				: me.HeadshotUrl;

			if (string.IsNullOrWhiteSpace(thumbUrl))
				thumbUrl = Globals.ApiEndpoint.PathJoin($"/v3/user/{userId}/thumbnail?size=48");

			using SystemNetHttp.HttpResponseMessage resp = await _http.GetAsync(thumbUrl);

			if (!resp.IsSuccessStatusCode)
				return;

			byte[] data = await resp.Content.ReadAsByteArrayAsync();

			Image img = new();

			Error err = img.LoadPngFromBuffer(data);
			if (err != Error.Ok)
				err = img.LoadJpgFromBuffer(data);

			if (err != Error.Ok)
				return;

			_userAvatar.Texture = ImageTexture.CreateFromImage(img);
		}
		catch
		{
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

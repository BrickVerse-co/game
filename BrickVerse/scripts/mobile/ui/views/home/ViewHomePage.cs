// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Mobile.Utils;
using BrickVerse.Schemas.API;
using Godot;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Utils;

namespace BrickVerse.Mobile.UI;

public partial class ViewHomePage : MobileViewBase
{
	private Label _usernameLabel = null!;
	private TextureRect _bodyshot = null!;
	//private BrickversianModel _polytorian = null!;

	public override void _EnterTree()
	{
		BVMobileAuthAPI.UserAuthenticated += OnUserAuthenticated;
		//_polytorian.AvatarLoaded += OnAvatarLoaded;

		base._EnterTree();
	}

	public override void _Ready()
	{
		_usernameLabel = GetNode<Label>("ScrollContainer/VBoxContainer/Control/Layout/Username");
		_bodyshot = GetNode<TextureRect>("ScrollContainer/VBoxContainer/Control/TextureRect");
		Button terms = GetNode<Button>("ScrollContainer/VBoxContainer/PanelContainer/Layout/Footer/Links/Terms");
		Button privacy = GetNode<Button>("ScrollContainer/VBoxContainer/PanelContainer/Layout/Footer/Links/Privacy");
		terms.Pressed += () => OS.ShellOpen("https://resources.brickverse.gg/legal/terms/terms-of-service");
		privacy.Pressed += () => OS.ShellOpen("https://resources.brickverse.gg/legal/privacy/privacy-policy");
		MobileMotion.Bind(terms);
		MobileMotion.Bind(privacy);
		if (BVMobileAuthAPI.IsAuthenticated) LoadView();
	}

	public override void _ExitTree()
	{
		BVMobileAuthAPI.UserAuthenticated -= OnUserAuthenticated;
		//_polytorian.AvatarLoaded -= OnAvatarLoaded;

		base._ExitTree();
	}

	private static void OnAvatarLoaded()
	{
		//((Node3D)_polytorian.GDNode).Visible = true;
		//_polytorian.Animator.PlayOneShotAnimation("poly_welcome");
		//_polytorian.SetState(CharacterModel.CharacterState.Idle);
	}

	private void OnUserAuthenticated(APIV3AuthMeUser response)
	{
		LoadView();
	}

	private async void LoadView()
	{
		_usernameLabel.Text = BVMobileAuthAPI.CurrentUserInfo.Username;
		try
		{
			string url = await BVAPI.ResolveThumbnailUrl("USER_BODYSHOT", BVMobileAuthAPI.CurrentUserInfo.Id);
			if (!string.IsNullOrWhiteSpace(url)) WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, resource => { if (IsInstanceValid(_bodyshot)) _bodyshot.Texture = (Texture2D)resource; });
		}
		catch { }
		//_polytorian.LoadAppearance(BVMobileAuthAPI.CurrentUserInfo.Id);
	}

	public override void ShowView(object? args)
	{
		//((Node3D)_polytorian.GDNode).Visible = false;
		//if (_polytorian.IsAvatarLoaded)
		//{
		//	OnAvatarLoaded();
		//}
		base.ShowView(args);
	}
}

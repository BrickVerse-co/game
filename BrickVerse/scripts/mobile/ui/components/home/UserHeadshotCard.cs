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

public partial class UserHeadshotCard : PanelContainer
{
	[Export] public string UserID = string.Empty;
	public string InitialUsername = string.Empty;
	public bool IsVerified;
	public bool IsAdmin;

	[Export] private TextureRect _imageRect = null!;
	[Export] private Label _usernameLabel = null!;

	private readonly BVImageAsset _iconAsset = new();
	private bool _disposed;
	private APIUserInfo _userData;
	private Control _imageSkeleton = null!;
	private Control _textSkeleton = null!;
	private Tween? _skeletonTween;

	public override void _Ready()
	{
		_imageRect.Texture = null;
		_usernameLabel.Text = InitialUsername;
		_imageSkeleton = GetNode<Control>("VBoxContainer/Panel/ImageSkeleton");
		_textSkeleton = GetNode<Control>("VBoxContainer/TextSkeleton");
		PulseSkeletons();
		if (!string.IsNullOrWhiteSpace(InitialUsername))
		{
			_textSkeleton.Visible = false;
			StopSkeletonWhenReady();
		}
		_iconAsset.ResourceLoaded += OnIconLoaded;
		Button button = GetNode<Button>("Button");
		MobileMotion.Bind(button);
		button.Pressed += OpenActions;
		LoadUserCard();
	}

	private void OnIconLoaded(Resource resource)
	{
		if (!_disposed && IsInstanceValid(_imageRect))
		{
			_imageRect.Texture = (Texture2D)resource;
			_imageSkeleton.Visible = false;
			StopSkeletonWhenReady();
		}
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
			_userData = userData;

			if (!_disposed && IsInstanceValid(_usernameLabel))
			{
				_usernameLabel.Text = userData.Username;
				GetNode<TextureRect>("VBoxContainer/NameRow/Verified").Visible = IsVerified;
				GetNode<TextureRect>("VBoxContainer/NameRow/Admin").Visible = IsAdmin || userData.IsStaff;
				_textSkeleton.Visible = false;
				StopSkeletonWhenReady();
			}
		}
		catch (Exception ex)
		{
			BV.PrintErr(ex);
		}
	}

	private void PulseSkeletons()
	{
		_skeletonTween = CreateTween().SetLoops().SetTrans(Tween.TransitionType.Sine);
		_skeletonTween.TweenProperty(this, "modulate:a", 0.72f, 0.65);
		_skeletonTween.TweenProperty(this, "modulate:a", 1f, 0.65);
	}

	private void StopSkeletonWhenReady()
	{
		if (_imageSkeleton.Visible || _textSkeleton.Visible) return;
		_skeletonTween?.Kill();
		Modulate = Colors.White;
	}

	private void OpenActions()
	{
		if (string.IsNullOrWhiteSpace(_userData.Username) && string.IsNullOrWhiteSpace(InitialUsername)) return;
		PopupPanel sheet = GD.Load<PackedScene>("res://scenes/mobile/components/home/friend_actions.tscn").Instantiate<PopupPanel>();
		GetTree().Root.AddChild(sheet);
		sheet.GetNode<Label>("Layout/Name").Text = string.IsNullOrWhiteSpace(_userData.Username) ? InitialUsername : _userData.Username;
		sheet.GetNode<Button>("Layout/ViewProfile").Pressed += () => { sheet.QueueFree(); MobileUI.Singleton.SwitchTo(MobileViewEnum.Profile, UserID); };
		sheet.GetNode<Button>("Layout/Report").Pressed += () => OS.ShellOpen(Globals.MainEndpoint.PathJoin($"/report?type=user&id={Uri.EscapeDataString(UserID)}"));
		sheet.GetNode<Button>("Layout/Close").Pressed += sheet.QueueFree;
		Vector2 viewport = GetViewport().GetVisibleRect().Size;
		Vector2 cardPosition = GetGlobalRect().Position;
		const int sheetWidth = 340;
		const int sheetHeight = 250;
		int x = Mathf.Clamp((int)(cardPosition.X + Size.X * 0.5f - sheetWidth * 0.5f), 12, Mathf.Max(12, (int)viewport.X - sheetWidth - 12));
		int y = (int)(cardPosition.Y + Size.Y + 8);
		if (y + sheetHeight > viewport.Y - 12) y = Mathf.Max(12, (int)cardPosition.Y - sheetHeight - 8);
		sheet.Popup(new Rect2I(x, y, sheetWidth, sheetHeight));
		Control layout = sheet.GetNode<Control>("Layout");
		layout.PivotOffset = layout.Size / 2f;
		layout.Scale = new Vector2(0.92f, 0.92f);
		layout.Modulate = new Color(1, 1, 1, 0);
		Tween tween = sheet.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(layout, "scale", Vector2.One, 0.22);
		tween.TweenProperty(layout, "modulate:a", 1f, 0.16);
	}
}

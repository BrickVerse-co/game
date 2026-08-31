// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Utils;
using System;
using System.Net.Http;

namespace BrickVerse.Mobile.UI;

public partial class FeedPostCard : PanelContainer
{
	[Export] private Label _usernameLabel = null!;
	[Export] private Label _postDateLabel = null!;
	[Export] private Label _locationLabel = null!;
	[Export] private Label _contentLabel = null!;
	[Export] private TextureRect _pfpRect = null!;
	[Export] private TextureRect _mediaRect = null!;
	[Export] private Label _likeLabel = null!;
	[Export] private Label _commentLabel = null!;
	[Signal] public delegate void ReplyRequestedEventHandler();
	public bool DetailMode { get; set; }

	private readonly BVImageAsset _pfpAsset = new();
	private Button _likeButton = null!;
	private Button _commentButton = null!;
	private bool _isLiked;
	private bool _likeBusy;
	private bool _isReposted;
	private bool _isBookmarked;
	private bool _repostBusy;
	private bool _bookmarkBusy;
	private Button _repostButton = null!;
	private Button _bookmarkButton = null!;
	private Button _shareButton = null!;
	private Button _moreButton = null!;
	private Texture2D _heartOutline = null!;
	private Texture2D _heartFilled = null!;

	public APIFeedPostData Data;

	public override void _Ready()
	{
		MobileMotion.BindCard(this);
		ConfigurePointerTargets(this);
		ConfigureAvatarShape();
		LoadHeadshot();
		_usernameLabel.Text = Data.Author.Username;
		TextureRect verifiedBadge = GetNode<TextureRect>("HBoxContainer/VBoxContainer/HBoxContainer/Verified");
		verifiedBadge.Visible = Data.Author.IsVerified || Data.Author.IsBrandAccount;
		verifiedBadge.Texture = GD.Load<Texture2D>(Data.Author.IsBrandAccount
			? "res://assets/textures/client/ui/feed/verified-brand.svg"
			: "res://assets/textures/client/ui/feed/verified-user.svg");
		verifiedBadge.TooltipText = Data.Author.IsBrandAccount ? "Verified brand" : "Verified account";
		DateTime postedUtc = Data.PostedAt.Kind == DateTimeKind.Utc
			? Data.PostedAt
			: DateTime.SpecifyKind(Data.PostedAt, DateTimeKind.Utc);
		_postDateLabel.Text = " · " + RelativeTime(postedUtc);
		_locationLabel.Visible = false;
		_contentLabel.Text = Data.Content;
		_likeLabel.Text = CompactCount(Data.LikeCount);
		_commentLabel.Text = CompactCount(Data.ReplyCount);
		_isLiked = Data.IsLiked;
		_isReposted = Data.IsReposted;
		_isBookmarked = Data.IsBookmarked;
		_likeButton = GetNode<Button>("HBoxContainer/VBoxContainer/HBoxContainer2/Like/LikeButton");
		_commentButton = GetNode<Button>("HBoxContainer/VBoxContainer/HBoxContainer2/Reply/CommentButton");
		_repostButton = GetNode<Button>("HBoxContainer/VBoxContainer/HBoxContainer2/Repost/Button");
		_bookmarkButton = GetNode<Button>("HBoxContainer/VBoxContainer/HBoxContainer2/BookmarkButton");
		_shareButton = GetNode<Button>("HBoxContainer/VBoxContainer/HBoxContainer2/ShareButton");
		_moreButton = GetNode<Button>("HBoxContainer/VBoxContainer/HBoxContainer/MoreButton");
		GetNode<Label>("HBoxContainer/VBoxContainer/HBoxContainer2/Repost/Label").Text = CompactCount(Data.RepostCount);
		GetNode<Label>("HBoxContainer/VBoxContainer/HBoxContainer2/Views/Label").Text = CompactCount(Data.ViewCount);
		_heartOutline = GD.Load<Texture2D>("res://assets/textures/ui-icons/heart.svg");
		_heartFilled = GD.Load<Texture2D>("res://assets/textures/ui-icons/heart-filled.svg");
		UpdateLikeVisual();
		UpdateSecondaryActions();
		_likeButton.Pressed += ToggleLike;
		_commentButton.Pressed += OpenComments;
		_repostButton.Pressed += ToggleRepost;
		_bookmarkButton.Pressed += ToggleBookmark;
		_shareButton.Pressed += SharePost;
		_moreButton.Pressed += OpenMoreMenu;
		MobileMotion.Bind(_likeButton);
		MobileMotion.Bind(_commentButton);
		MobileMotion.Bind(_repostButton);
		MobileMotion.Bind(_bookmarkButton);
		MobileMotion.Bind(_shareButton);
		MobileMotion.Bind(_moreButton);
		GuiInput += OpenThreadFromCard;

		if (Data.PlaceID != null)
		{
			string place = string.IsNullOrWhiteSpace(Data.PlaceName) ? "In a world" : Data.PlaceName;
			_locationLabel.Visible = true;
			_locationLabel.Text = "  •  " + place;
		}

		if (Data.MediaUrl != null)
		{
			((Control)_mediaRect.GetParent()).Visible = true;
			WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = Data.MediaUrl }, (resource) =>
			{
				if (IsInstanceValid(_mediaRect)) _mediaRect.Texture = (Texture2D)resource;
			});
		}
		else
		{
			((Control)_mediaRect.GetParent()).Visible = false;
		}
	}

	private void OpenThreadFromCard(InputEvent input)
	{
		bool activated = input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }
			|| input is InputEventScreenTouch { Pressed: false };
		if (!DetailMode && activated)
			OpenComments();
	}

	private static void ConfigurePointerTargets(Control control)
	{
		foreach (Node childNode in control.GetChildren())
		{
			if (childNode is not Control child) continue;
			if (child is not BaseButton) child.MouseFilter = MouseFilterEnum.Ignore;
			ConfigurePointerTargets(child);
		}
	}

	private void ConfigureAvatarShape()
	{
		Panel frame = GetNode<Panel>("HBoxContainer/Control/TextureRect");
		StyleBoxFlat style = new() { BgColor = Color.FromHtml("171B22") };
		int radius = Data.Author.IsBrandAccount ? 8 : 22;
		style.CornerRadiusTopLeft = radius;
		style.CornerRadiusTopRight = radius;
		style.CornerRadiusBottomLeft = radius;
		style.CornerRadiusBottomRight = radius;
		frame.AddThemeStyleboxOverride("panel", style);
	}

	private void LoadHeadshot()
	{
		if (!string.IsNullOrWhiteSpace(Data.HeadshotUrl))
		{
			WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = Data.HeadshotUrl }, resource =>
			{
				if (IsInstanceValid(_pfpRect)) _pfpRect.Texture = (Texture2D)resource;
			});
			return;
		}
		_pfpAsset.ResourceLoaded += OnPFPLoaded;
		_pfpAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
		_pfpAsset.ImageID = Data.Author.Id.ToString();
		_pfpAsset.LoadResource();
	}

	private static string RelativeTime(DateTime postedUtc)
	{
		TimeSpan elapsed = DateTime.UtcNow - postedUtc;
		if (elapsed < TimeSpan.Zero || elapsed.TotalSeconds < 45) return "Just now";
		if (elapsed.TotalMinutes < 60) return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m ago";
		if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
		if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
		if (elapsed.TotalDays < 30) return $"{Math.Max(1, (int)(elapsed.TotalDays / 7))}w ago";
		return postedUtc.ToLocalTime().ToString("MMM d, yyyy");
	}

	private static string CompactCount(int count) => count switch
	{
		>= 1_000_000 => $"{count / 1_000_000d:0.#}M",
		>= 1_000 => $"{count / 1_000d:0.#}K",
		_ => count.ToString(),
	};

	private async void ToggleLike()
	{
		if (_likeBusy) return;
		_likeBusy = true;
		_likeButton.Disabled = true;
		try
		{
			using var response = await BVAPI.SendJson(_isLiked ? HttpMethod.Delete : HttpMethod.Post, $"/v3/social/posts/{Data.Id}/like");
			if (response.RootElement.TryGetProperty("success", out var success) && success.ValueKind == System.Text.Json.JsonValueKind.False)
				throw new InvalidOperationException(response.RootElement.TryGetProperty("message", out var message) ? message.GetString() : "The reaction could not be updated.");
			_isLiked = !_isLiked;
			Data.LikeCount = Math.Max(0, Data.LikeCount + (_isLiked ? 1 : -1));
			_likeLabel.Text = Data.LikeCount.ToString();
			UpdateLikeVisual();
		}
		catch (Exception exception) { OS.Alert(exception.Message, "Could not update like"); }
		finally { if (IsInstanceValid(_likeButton)) { _likeBusy = false; _likeButton.Disabled = false; } }
	}

	private void UpdateLikeVisual()
	{
		if (!IsInstanceValid(_likeButton)) return;
		_likeButton.Icon = _isLiked ? _heartFilled : _heartOutline;
		_likeButton.Modulate = _isLiked ? new Color(1f, 0.2f, 0.3f) : Colors.White;
	}

	private void UpdateSecondaryActions()
	{
		if (IsInstanceValid(_repostButton)) _repostButton.Modulate = _isReposted ? Color.FromHtml("00BA7C") : Colors.White;
		if (IsInstanceValid(_bookmarkButton)) _bookmarkButton.Modulate = _isBookmarked ? Color.FromHtml("1D9BF0") : Colors.White;
	}

	private async void ToggleRepost()
	{
		if (_repostBusy) return;
		_repostBusy = true;
		_repostButton.Disabled = true;
		try
		{
			using var response = await BVAPI.SendJson(_isReposted ? HttpMethod.Delete : HttpMethod.Post, $"/v3/social/posts/{Data.Id}/repost");
			if (response.RootElement.TryGetProperty("success", out var success) && success.ValueKind == System.Text.Json.JsonValueKind.False)
				throw new InvalidOperationException("The repost could not be updated.");
			_isReposted = !_isReposted;
			Data.RepostCount = Math.Max(0, Data.RepostCount + (_isReposted ? 1 : -1));
			GetNode<Label>("HBoxContainer/VBoxContainer/HBoxContainer2/Repost/Label").Text = CompactCount(Data.RepostCount);
			UpdateSecondaryActions();
		}
		catch (Exception exception) { OS.Alert(exception.Message, "Could not repost"); }
		finally { if (IsInstanceValid(_repostButton)) { _repostBusy = false; _repostButton.Disabled = false; } }
	}

	private async void ToggleBookmark()
	{
		if (_bookmarkBusy) return;
		_bookmarkBusy = true;
		_bookmarkButton.Disabled = true;
		try
		{
			using var response = await BVAPI.SendJson(_isBookmarked ? HttpMethod.Delete : HttpMethod.Post, $"/v3/social/posts/{Data.Id}/bookmark");
			if (response.RootElement.TryGetProperty("success", out var success) && success.ValueKind == System.Text.Json.JsonValueKind.False)
				throw new InvalidOperationException("The bookmark could not be updated.");
			_isBookmarked = !_isBookmarked;
			UpdateSecondaryActions();
		}
		catch (Exception exception) { OS.Alert(exception.Message, "Could not bookmark"); }
		finally { if (IsInstanceValid(_bookmarkButton)) { _bookmarkBusy = false; _bookmarkButton.Disabled = false; } }
	}

	private void SharePost()
	{
		DisplayServer.ClipboardSet(Globals.MainEndpoint.PathJoin($"/feed/{Data.Id}"));
		_shareButton.TooltipText = "Link copied";
		Tween feedback = CreateTween();
		feedback.TweenProperty(_shareButton, "modulate", Color.FromHtml("1D9BF0"), 0.08);
		feedback.TweenProperty(_shareButton, "modulate", Colors.White, 0.25);
	}

	private void OpenMoreMenu()
	{
		PopupMenu menu = new();
		menu.AddItem("Copy link", 0);
		menu.AddItem("View Feed profile", 1);
		menu.AddSeparator();
		menu.AddItem("Report post", 2);
		menu.IdPressed += id =>
		{
			if (id == 0) SharePost();
			else if (id == 1) MobileUI.Singleton.SwitchTo(MobileViewEnum.Profile, Data.Author.Id.ToString());
			else MobileReportDialog.Open(this, "post", Data.Id);
			menu.QueueFree();
		};
		menu.PopupHide += menu.QueueFree;
		GetTree().Root.AddChild(menu);
		menu.Position = (Vector2I)(_moreButton.GlobalPosition + new Vector2(-150, 30));
		menu.Popup();
	}

	private void OpenComments()
	{
		if (DetailMode) { EmitSignal(SignalName.ReplyRequested); return; }
		_commentButton.PivotOffset = _commentButton.Size / 2f;
		Tween feedback = CreateTween();
		feedback.TweenProperty(_commentButton, "scale", new Vector2(0.86f, 0.86f), 0.06);
		feedback.TweenProperty(_commentButton, "scale", Vector2.One, 0.1);
		FeedCommentsDialog dialog = GD.Load<PackedScene>("res://scenes/mobile/components/home/feed_comments_dialog.tscn").Instantiate<FeedCommentsDialog>();
		GetTree().Root.AddChild(dialog);
		dialog.Open(Data);
		dialog.CommentAdded += () => { Data.ReplyCount++; _commentLabel.Text = Data.ReplyCount.ToString(); };
	}

	private void OnPFPLoaded(Resource resource)
	{
		_pfpRect.Texture = (Texture2D)resource;
	}
}

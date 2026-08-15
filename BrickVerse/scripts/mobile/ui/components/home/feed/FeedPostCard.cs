// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Schemas.API;
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

	private readonly BVImageAsset _pfpAsset = new();
	private Button _likeButton = null!;
	private Button _commentButton = null!;
	private bool _isLiked;
	private bool _likeBusy;
	private Texture2D _heartOutline = null!;
	private Texture2D _heartFilled = null!;

	public APIFeedPostData Data;

	public override void _Ready()
	{
		MobileMotion.BindCard(this);
		_pfpAsset.ResourceLoaded += OnPFPLoaded;
		_pfpAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
		_pfpAsset.ImageID = Data.Author.Id.ToString();
		_pfpAsset.LoadResource();
		_usernameLabel.Text = Data.Author.Username;
		GetNode<TextureRect>("HBoxContainer/VBoxContainer/HBoxContainer/Verified").Visible = Data.Author.IsVerified;
		DateTime postedUtc = Data.PostedAt.Kind == DateTimeKind.Utc
			? Data.PostedAt
			: DateTime.SpecifyKind(Data.PostedAt, DateTimeKind.Utc);
		_postDateLabel.Text = postedUtc.ToLocalTime().ToString("M/d/yyyy");
		_locationLabel.Visible = true;
		_locationLabel.Text = RelativeTime(postedUtc);
		_contentLabel.Text = Data.Content;
		_likeLabel.Text = Data.LikeCount.ToString();
		_commentLabel.Text = Data.ReplyCount.ToString();
		_isLiked = Data.IsLiked;
		_likeButton = GetNode<Button>("HBoxContainer/VBoxContainer/HBoxContainer2/HBoxContainer/LikeButton");
		_commentButton = GetNode<Button>("HBoxContainer/VBoxContainer/HBoxContainer2/HBoxContainer2/CommentButton");
		_heartOutline = GD.Load<Texture2D>("res://assets/textures/ui-icons/heart.svg");
		_heartFilled = GD.Load<Texture2D>("res://assets/textures/ui-icons/heart-filled.svg");
		UpdateLikeVisual();
		_likeButton.Pressed += ToggleLike;
		_commentButton.Pressed += OpenComments;
		MobileMotion.Bind(_likeButton);
		MobileMotion.Bind(_commentButton);

		if (Data.PlaceID != null)
		{
			string place = string.IsNullOrWhiteSpace(Data.PlaceName) ? "In a world" : Data.PlaceName;
			_locationLabel.Text += "  •  " + place;
		}

		if (Data.MediaUrl != null)
		{
			_mediaRect.Visible = true;
			WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = Data.MediaUrl }, (resource) =>
			{
				if (IsInstanceValid(_mediaRect)) _mediaRect.Texture = (Texture2D)resource;
			});
		}
		else
		{
			_mediaRect.Visible = false;
		}
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

	private void OpenComments()
	{
		_commentButton.PivotOffset = _commentButton.Size / 2f;
		Tween feedback = CreateTween();
		feedback.TweenProperty(_commentButton, "scale", new Vector2(0.86f, 0.86f), 0.06);
		feedback.TweenProperty(_commentButton, "scale", Vector2.One, 0.1);
		FeedCommentsDialog dialog = GD.Load<PackedScene>("res://scenes/mobile/components/home/feed_comments_dialog.tscn").Instantiate<FeedCommentsDialog>();
		GetTree().Root.AddChild(dialog);
		dialog.Open(Data.Id);
		dialog.CommentAdded += () => { Data.ReplyCount++; _commentLabel.Text = Data.ReplyCount.ToString(); };
	}

	private void OnPFPLoaded(Resource resource)
	{
		_pfpRect.Texture = (Texture2D)resource;
	}
}

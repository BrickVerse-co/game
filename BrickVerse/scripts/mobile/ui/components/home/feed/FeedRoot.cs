// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Utils;
using BrickVerse.Mobile.Utils;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace BrickVerse.Mobile.UI;

public partial class FeedRoot : Node
{
	private const string FeedCardPath = "res://scenes/mobile/components/home/feed_card.tscn";

	private PackedScene _feedCard = null!;
	private FeedComposer _composer = null!;
	private int _page;
	private const int PageSize = 10;
	private Button _previous = null!;
	private Button _next = null!;
	private Label _pageLabel = null!;
	private PackedScene _skeletonScene = null!;
	[Export] private Control _feedContainer = null!;
	private int _loadVersion;
	private bool _disposed;

	public override void _Ready()
	{
		_feedCard = GD.Load<PackedScene>(FeedCardPath);
		_skeletonScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/skeleton_card.tscn");
		_composer = GD.Load<PackedScene>("res://scenes/mobile/components/home/feed_composer.tscn").Instantiate<FeedComposer>();
		AddChild(_composer);
		_composer.PostCreated += LoadFeed;
		GetNode<Button>("HBoxContainer/Button").Pressed += OpenComposer;
		_previous = GetNode<Button>("Pagination/Previous");
		_next = GetNode<Button>("Pagination/Next");
		_pageLabel = GetNode<Label>("Pagination/Page");
		_previous.Pressed += () => { if (_page > 0) { _page--; LoadFeed(); } };
		_next.Pressed += () => { _page++; LoadFeed(); };
		MobileMotion.Bind(_previous);
		MobileMotion.Bind(_next);
		BVMobileAuthAPI.UserAuthenticated += OnUserAuthenticated;
		if (BVMobileAuthAPI.IsAuthenticated) LoadFeed();
	}

	public override void _ExitTree()
	{
		_disposed = true;
		_loadVersion++;
		BVMobileAuthAPI.UserAuthenticated -= OnUserAuthenticated;
		if (_composer != null) _composer.PostCreated -= LoadFeed;
		base._ExitTree();
	}

	private void OnUserAuthenticated(APIV3AuthMeUser _) => LoadFeed();

	private void OpenComposer() => _composer.Open();

	private async void LoadFeed()
	{
		int version = ++_loadVersion;
		try
		{
			foreach (Node child in _feedContainer.GetChildren()) child.QueueFree();
			for (int index = 0; index < 3; index++) _feedContainer.AddChild(_skeletonScene.Instantiate());
			using JsonDocument feed = await BVAPI.GetJson($"/v3/social/feed?limit={PageSize}&offset={_page * PageSize}");
			await RunOnMainThread(() =>
			{
				if (_disposed || version != _loadVersion || !IsInstanceValid(_feedContainer)) return;
				foreach (Node child in _feedContainer.GetChildren()) child.QueueFree();
				if (!feed.RootElement.TryGetProperty("posts", out JsonElement posts)) return;
				foreach (JsonElement item in posts.EnumerateArray())
				{
					JsonElement user = item.TryGetProperty("user", out JsonElement userNode) ? userNode : default;
					long.TryParse(user.ValueKind == JsonValueKind.Object && user.TryGetProperty("id", out JsonElement userId) ? userId.ToString() : "0", out long authorId);
					DateTime.TryParse(item.TryGetProperty("createdAt", out JsonElement created) ? created.GetString() : null, out DateTime postedAt);
					FeedPostCard card = _feedCard.Instantiate<FeedPostCard>();
					bool isBrand = user.ValueKind == JsonValueKind.Object && user.TryGetProperty("isBrandAccount", out JsonElement brand) && brand.ValueKind == JsonValueKind.True;
					card.Data = new APIFeedPostData
					{
						Id = item.TryGetProperty("id", out JsonElement postId) ? postId.ToString() : "",
						Content = item.TryGetProperty("content", out JsonElement content) ? content.GetString() ?? "" : "",
						PostedAt = postedAt,
						Author = new APIFeedPostAuthor { Id = authorId, Username = user.ValueKind == JsonValueKind.Object && user.TryGetProperty("username", out JsonElement username) ? username.GetString() ?? "BrickVerse user" : "BrickVerse user", IsVerified = user.ValueKind == JsonValueKind.Object && user.TryGetProperty("isVerified", out JsonElement verified) && verified.ValueKind == JsonValueKind.True, IsBrandAccount = isBrand },
						LikeCount = ReadCount(item, "totalLikes", "likeCount"),
						ReplyCount = ReadCount(item, "totalComments", "commentCount"),
						RepostCount = ReadCount(item, "totalReposts", "repostCount"),
						ViewCount = ReadCount(item, "totalViews", "viewCount"),
						IsLiked = item.TryGetProperty("isLikedByUser", out JsonElement liked) && liked.ValueKind == JsonValueKind.True,
						IsReposted = item.TryGetProperty("isRepostedByUser", out JsonElement reposted) && reposted.ValueKind == JsonValueKind.True,
						IsBookmarked = item.TryGetProperty("isBookmarkedByUser", out JsonElement bookmarked) && bookmarked.ValueKind == JsonValueKind.True,
						MediaUrl = FirstMediaUrl(item),
						HeadshotUrl = ReadHeadshotUrl(item, user, isBrand),
						Comments = [],
					};
					_feedContainer.AddChild(card);
				}
				_previous.Disabled = _page == 0;
				_next.Disabled = posts.GetArrayLength() < PageSize;
				_pageLabel.Text = $"Page {_page + 1}";
			});
		}
		catch (Exception exception)
		{
			if (_disposed || version != _loadVersion || !IsInstanceValid(_feedContainer)) return;
			await RunOnMainThread(() =>
			{
				if (IsInstanceValid(_feedContainer)) foreach (Node child in _feedContainer.GetChildren()) child.QueueFree();
			});
			BV.PrintErr("Failed to load feed: ", exception);
		}
	}

	private static int ReadCount(JsonElement item, params string[] names)
	{
		foreach (string name in names) if (item.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int count)) return count;
		return 0;
	}

	private static string? FirstMediaUrl(JsonElement item)
	{
		if (item.TryGetProperty("mediaUrls", out JsonElement media) && media.ValueKind == JsonValueKind.Array)
			foreach (JsonElement value in media.EnumerateArray())
				if (value.ValueKind == JsonValueKind.String) return value.GetString();
		return item.TryGetProperty("mediaUrl", out JsonElement single) && single.ValueKind == JsonValueKind.String
			? single.GetString() : null;
	}

	private static string? ReadHeadshotUrl(JsonElement item, JsonElement user, bool isBrand)
	{
		if (item.TryGetProperty("headshotUrl", out JsonElement direct) && direct.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(direct.GetString()))
			return direct.GetString();
		if (user.ValueKind != JsonValueKind.Object) return null;
		string preferred = isBrand ? "brandHeadshotUrl" : "headshotUrl";
		if (user.TryGetProperty(preferred, out JsonElement headshot) && headshot.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(headshot.GetString()))
			return headshot.GetString();
		return user.TryGetProperty("headshotUrl", out JsonElement fallback) && fallback.ValueKind == JsonValueKind.String ? fallback.GetString() : null;
	}

	public void Refresh() => LoadFeed();

	private static Task RunOnMainThread(Action action)
	{
		TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		BV.CallOnMainThread(() =>
		{
			try { action(); completion.SetResult(); }
			catch (Exception exception) { completion.SetException(exception); }
		});
		return completion.Task;
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Net.Http;
using System.Text.Json;
using BrickVerse.Schemas.API;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class FeedCommentsDialog : AcceptDialog
{
	[Signal] public delegate void CommentAddedEventHandler();
	private string _postId = "";
	private APIFeedPostData? _threadPost;
	private VBoxContainer _comments = null!;
	private LineEdit _input = null!;
	private Button _post = null!;
	private PackedScene _commentCard = null!;
	private string? _replyToId;

	public override void _Ready()
	{
		_comments = GetNode<VBoxContainer>("Layout/Scroll/Comments");
		_input = GetNode<LineEdit>("Layout/Composer/Input");
		_post = GetNode<Button>("Layout/Composer/Post");
		_commentCard = GD.Load<PackedScene>("res://scenes/mobile/components/home/feed_comment_card.tscn");
		_post.Pressed += Submit;
		_input.TextSubmitted += _ => Submit();
		MobileMotion.Bind(_post);
		CloseRequested += QueueFree;
		Confirmed += QueueFree;
	}

	public void Open(APIFeedPostData post)
	{
		_threadPost = post;
		_postId = post.Id;
		Vector2 viewport = GetViewport().GetVisibleRect().Size;
		int width = Math.Min(620, Math.Max(300, (int)viewport.X - 12));
		int height = Math.Min(780, Math.Max(360, (int)viewport.Y - 20));
		MaxSize = new Vector2I(width, height);
		PopupCentered(new Vector2I(width, height));
		_ = LoadComments();
	}

	private async System.Threading.Tasks.Task LoadComments()
	{
		foreach (Node child in _comments.GetChildren()) child.QueueFree();
		if (_threadPost.HasValue)
		{
			FeedPostCard original = GD.Load<PackedScene>("res://scenes/mobile/components/home/feed_card.tscn").Instantiate<FeedPostCard>();
			original.Data = _threadPost.Value;
			original.DetailMode = true;
			original.ReplyRequested += () => BeginReply(null, _threadPost.Value.Author.Username);
			_comments.AddChild(original);
		}
		using JsonDocument response = await BVAPI.GetJson($"/v3/social/posts/{_postId}/comments?limit=50");
		if (!response.RootElement.TryGetProperty("comments", out JsonElement records)) return;
		if (records.GetArrayLength() == 0)
		{
			Label empty = GD.Load<PackedScene>("res://scenes/mobile/components/shared/info_label.tscn").Instantiate<Label>();
			empty.Text = "No comments yet. Start the conversation.";
			_comments.AddChild(empty);
			return;
		}
		foreach (JsonElement item in records.EnumerateArray()) AddReply(item, 0);
	}

	private void AddReply(JsonElement item, int depth)
	{
		PanelContainer row = _commentCard.Instantiate<PanelContainer>();
		JsonElement user = item.TryGetProperty("user", out JsonElement userNode) ? userNode : default;
		string author = user.ValueKind == JsonValueKind.Object && user.TryGetProperty("username", out JsonElement username) ? username.GetString() ?? "User" : "User";
		string content = item.TryGetProperty("content", out JsonElement text) ? text.GetString() ?? "" : "";
		string commentId = item.TryGetProperty("id", out JsonElement id) ? id.ToString() : "";
		int likes = item.TryGetProperty("totalLikes", out JsonElement likeNode) && likeNode.TryGetInt32(out int likeCount) ? likeCount : 0;
		bool liked = item.TryGetProperty("isLikedByUser", out JsonElement likedNode) && likedNode.ValueKind == JsonValueKind.True;
		string createdAt = item.TryGetProperty("createdAt", out JsonElement created) ? created.GetString() ?? "" : "";
		string headshotUrl = item.TryGetProperty("headshotUrl", out JsonElement headshot) && headshot.ValueKind == JsonValueKind.String ? headshot.GetString() ?? "" : "";

		row.GetNode<Label>("Layout/Content/Header/Author").Text = author;
		bool isVerified = user.ValueKind == JsonValueKind.Object && user.TryGetProperty("isVerified", out JsonElement verified) && verified.ValueKind == JsonValueKind.True;
		bool isBrand = user.ValueKind == JsonValueKind.Object && user.TryGetProperty("isBrandAccount", out JsonElement brand) && brand.ValueKind == JsonValueKind.True;
		TextureRect verifiedBadge = row.GetNode<TextureRect>("Layout/Content/Header/Verified");
		verifiedBadge.Visible = isVerified || isBrand;
		verifiedBadge.Texture = GD.Load<Texture2D>(isBrand
			? "res://assets/textures/client/ui/feed/verified-brand.svg"
			: "res://assets/textures/client/ui/feed/verified-user.svg");
		verifiedBadge.TooltipText = isBrand ? "Verified brand" : "Verified account";
		row.GetNode<Label>("Layout/Content/Header/Time").Text = $"@{author.ToLowerInvariant().Replace(" ", "")} · {(DateTime.TryParse(createdAt, out DateTime timestamp) ? RelativeTime(timestamp) : "Just now")}";
		row.GetNode<Label>("Layout/Content/Body").Text = content;
		Button reply = row.GetNode<Button>("Layout/Content/Actions/Reply");
		Button like = row.GetNode<Button>("Layout/Content/Actions/Like");
		Button more = row.GetNode<Button>("Layout/Content/Header/More");
		like.Text = likes.ToString();
		like.Modulate = liked ? Color.FromHtml("F91880") : Colors.White;
		reply.Pressed += () => BeginReply(commentId, author);
		like.Pressed += async () =>
		{
			if (string.IsNullOrWhiteSpace(commentId) || like.Disabled) return;
			like.Disabled = true;
			try
			{
				using JsonDocument _ = await BVAPI.SendJson(liked ? HttpMethod.Delete : HttpMethod.Post, $"/v3/social/comments/{commentId}/like");
				liked = !liked;
				likes = Math.Max(0, likes + (liked ? 1 : -1));
				like.Text = likes.ToString();
				like.Modulate = liked ? Color.FromHtml("F91880") : Colors.White;
			}
			catch (Exception exception) { OS.Alert(exception.Message, "Could not update reply like"); }
			finally { if (IsInstanceValid(like)) like.Disabled = false; }
		};
		more.Pressed += () => OpenReplyMenu(more, content, commentId);
		MobileMotion.Bind(reply); MobileMotion.Bind(like); MobileMotion.Bind(more);

		if (!string.IsNullOrWhiteSpace(headshotUrl))
		{
			TextureRect avatar = row.GetNode<TextureRect>("Layout/Avatar");
			WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = headshotUrl }, resource =>
			{
				if (IsInstanceValid(avatar)) avatar.Texture = (Texture2D)resource;
			});
		}

		if (depth > 0)
		{
			StyleBoxFlat nested = new() { BgColor = Colors.Transparent, BorderColor = Color.FromHtml("27303A"), BorderWidthLeft = 2, BorderWidthBottom = 1, ContentMarginLeft = Math.Min(depth, 3) * 16 + 8, ContentMarginTop = 10, ContentMarginRight = 8, ContentMarginBottom = 10 };
			row.AddThemeStyleboxOverride("panel", nested);
		}
		_comments.AddChild(row);
		if (item.TryGetProperty("replies", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
			foreach (JsonElement child in children.EnumerateArray()) AddReply(child, depth + 1);
	}

	private void BeginReply(string? commentId, string username)
	{
		_replyToId = string.IsNullOrWhiteSpace(commentId) ? null : commentId;
		_input.PlaceholderText = _replyToId == null ? $"Reply to {username}" : $"Replying to @{username}";
		_input.GrabFocus();
	}

	private void OpenReplyMenu(Button anchor, string content, string commentId)
	{
		PopupMenu menu = new();
		menu.AddItem("Copy reply text", 0);
		menu.AddItem("Report reply", 1);
		menu.IdPressed += id =>
		{
			if (id == 0) DisplayServer.ClipboardSet(content);
			else if (!string.IsNullOrWhiteSpace(commentId)) MobileReportDialog.Open(this, "comment", commentId);
			menu.QueueFree();
		};
		menu.PopupHide += menu.QueueFree;
		GetTree().Root.AddChild(menu);
		menu.Position = (Vector2I)(anchor.GlobalPosition + new Vector2(-130, 28));
		menu.Popup();
	}

	private static string RelativeTime(DateTime timestamp)
	{
		DateTime utc = timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime();
		TimeSpan elapsed = DateTime.UtcNow - utc;
		if (elapsed < TimeSpan.Zero || elapsed.TotalMinutes < 1) return "Just now";
		if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}m ago";
		if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}h ago";
		return $"{(int)elapsed.TotalDays}d ago";
	}

	private async void Submit()
	{
		string content = _input.Text.Trim();
		if (string.IsNullOrWhiteSpace(content)) return;
		_post.Disabled = true;
		try
		{
			string json = _replyToId == null
				? $"{{\"content\":{JsonSerializer.Serialize(content)}}}"
				: $"{{\"content\":{JsonSerializer.Serialize(content)},\"parentId\":{JsonSerializer.Serialize(_replyToId)}}}";
			using JsonDocument response = await BVAPI.SendJson(HttpMethod.Post, $"/v3/social/posts/{_postId}/comments", json);
			if (response.RootElement.TryGetProperty("success", out JsonElement success) && success.ValueKind == JsonValueKind.False)
				throw new InvalidOperationException(response.RootElement.TryGetProperty("message", out JsonElement message) ? message.GetString() : "The comment could not be posted.");
			_input.Clear();
			_replyToId = null;
			_input.PlaceholderText = "Post your reply";
			EmitSignal(SignalName.CommentAdded);
			await LoadComments();
		}
		catch (Exception exception) { OS.Alert(exception.Message, "Could not comment"); }
		finally { _post.Disabled = false; }
	}
}

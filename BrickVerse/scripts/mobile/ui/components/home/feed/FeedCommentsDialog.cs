// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Net.Http;
using System.Text.Json;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class FeedCommentsDialog : AcceptDialog
{
	[Signal] public delegate void CommentAddedEventHandler();
	private long _postId;
	private VBoxContainer _comments = null!;
	private LineEdit _input = null!;
	private Button _post = null!;
	private PackedScene _commentCard = null!;

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

	public void Open(long postId) { _postId = postId; PopupCenteredRatio(0.9f); _ = LoadComments(); }

	private async System.Threading.Tasks.Task LoadComments()
	{
		foreach (Node child in _comments.GetChildren()) child.QueueFree();
		using JsonDocument response = await BVAPI.GetJson($"/v3/social/posts/{_postId}/comments?limit=50");
		if (!response.RootElement.TryGetProperty("comments", out JsonElement records)) return;
		if (records.GetArrayLength() == 0)
		{
			Label empty = GD.Load<PackedScene>("res://scenes/mobile/components/shared/info_label.tscn").Instantiate<Label>();
			empty.Text = "No comments yet. Start the conversation.";
			_comments.AddChild(empty);
			return;
		}
		foreach (JsonElement item in records.EnumerateArray())
		{
			PanelContainer row = _commentCard.Instantiate<PanelContainer>();
			string author = item.TryGetProperty("user", out JsonElement user) && user.TryGetProperty("username", out JsonElement username) ? username.GetString() ?? "User" : "User";
			string content = item.TryGetProperty("content", out JsonElement text) ? text.GetString() ?? "" : "";
			row.GetNode<Label>("Content/Author").Text = author;
			row.GetNode<Label>("Content/Body").Text = content;
			string createdAt = item.TryGetProperty("createdAt", out JsonElement created) ? created.GetString() ?? "" : "";
			row.GetNode<Label>("Content/Time").Text = DateTime.TryParse(createdAt, out DateTime timestamp)
				? RelativeTime(timestamp) : "Just now";
			_comments.AddChild(row);
		}
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
			string json = $"{{\"content\":{JsonSerializer.Serialize(content)}}}";
			using JsonDocument response = await BVAPI.SendJson(HttpMethod.Post, $"/v3/social/posts/{_postId}/comments", json);
			if (response.RootElement.TryGetProperty("success", out JsonElement success) && success.ValueKind == JsonValueKind.False)
				throw new InvalidOperationException(response.RootElement.TryGetProperty("message", out JsonElement message) ? message.GetString() : "The comment could not be posted.");
			_input.Clear();
			EmitSignal(SignalName.CommentAdded);
			await LoadComments();
		}
		catch (Exception exception) { OS.Alert(exception.Message, "Could not comment"); }
		finally { _post.Disabled = false; }
	}
}

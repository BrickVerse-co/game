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

	public override void _Ready()
	{
		_comments = GetNode<VBoxContainer>("Layout/Scroll/Comments");
		_input = GetNode<LineEdit>("Layout/Composer/Input");
		_post = GetNode<Button>("Layout/Composer/Post");
		_post.Pressed += Submit;
		CloseRequested += QueueFree;
		Confirmed += QueueFree;
	}

	public void Open(long postId) { _postId = postId; PopupCenteredRatio(0.9f); _ = LoadComments(); }

	private async System.Threading.Tasks.Task LoadComments()
	{
		foreach (Node child in _comments.GetChildren()) child.QueueFree();
		using JsonDocument response = await BVAPI.GetJson($"/v3/social/posts/{_postId}/comments?limit=50");
		if (!response.RootElement.TryGetProperty("comments", out JsonElement records)) return;
		foreach (JsonElement item in records.EnumerateArray())
		{
			Label row = GD.Load<PackedScene>("res://scenes/mobile/components/shared/info_label.tscn").Instantiate<Label>();
			string author = item.TryGetProperty("user", out JsonElement user) && user.TryGetProperty("username", out JsonElement username) ? username.GetString() ?? "User" : "User";
			string content = item.TryGetProperty("content", out JsonElement text) ? text.GetString() ?? "" : "";
			row.Text = $"{author}\n{content}";
			_comments.AddChild(row);
		}
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
			_input.Clear();
			EmitSignal(SignalName.CommentAdded);
			await LoadComments();
		}
		catch (Exception exception) { OS.Alert(exception.Message, "Could not comment"); }
		finally { _post.Disabled = false; }
	}
}

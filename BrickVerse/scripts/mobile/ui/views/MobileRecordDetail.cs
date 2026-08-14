// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Text;
using System.Text.Json;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public sealed record MobileRecordDetailArgs(string Title, string Meta, string Description, string ImageUrl, MobileViewEnum ReturnView, string Id = "");

public partial class MobileRecordDetail : MobileViewBase
{
	private Label _title = null!;
	private Label _meta = null!;
	private MobileMarkdown _description = null!;
	private TextureRect _image = null!;
	private Label _repliesTitle = null!;
	private VBoxContainer _replies = null!;
	private PackedScene _replyScene = null!;
	private MobileViewEnum _returnView = MobileViewEnum.Dev;

	public override void _Ready()
	{
		_title = GetNode<Label>("Layout/Title");
		_meta = GetNode<Label>("Layout/Meta");
		_description = GetNode<MobileMarkdown>("Layout/Scroll/Content/Description");
		_repliesTitle = GetNode<Label>("Layout/Scroll/Content/RepliesTitle");
		_replies = GetNode<VBoxContainer>("Layout/Scroll/Content/Replies");
		_replyScene = GD.Load<PackedScene>("res://scenes/mobile/components/forum/forum_reply.tscn");
		_image = GetNode<TextureRect>("Layout/Image");
		GetNode<Button>("Layout/Header/Back").Pressed += () => MobileUI.Singleton.SwitchTo(_returnView, _returnView);
	}

	public override void ShowView(object? args)
	{
		if (args is not MobileRecordDetailArgs detail) return;
		_returnView = detail.ReturnView;
		GetNode<Control>("Layout/Actions").Visible = false;
		_title.Text = detail.Title;
		_meta.Text = detail.Meta;
		_description.SetMarkdown(string.IsNullOrWhiteSpace(detail.Description) ? "No description provided." : detail.Description);
		_repliesTitle.Visible = false;
		_replies.Visible = false;
		foreach (Node child in _replies.GetChildren()) child.QueueFree();
		_image.Visible = !string.IsNullOrWhiteSpace(detail.ImageUrl);
		if (_image.Visible) _ = LoadImage(detail.ImageUrl);
		if (detail.ReturnView == MobileViewEnum.Forum && !string.IsNullOrWhiteSpace(detail.Id)) _ = LoadForumThread(detail.Id);
		else if (detail.ReturnView == MobileViewEnum.Guilds && !string.IsNullOrWhiteSpace(detail.Id)) _ = LoadGuild(detail.Id);
	}

	private async System.Threading.Tasks.Task LoadGuild(string id)
	{
		HBoxContainer actions = GetNode<HBoxContainer>("Layout/Actions");
		actions.Visible = true;
		Button primary = actions.GetNode<Button>("Primary");
		Button membersButton = actions.GetNode<Button>("Members");
		Button manage = actions.GetNode<Button>("Manage");
		MobileMotion.Bind(primary); MobileMotion.Bind(membersButton); MobileMotion.Bind(manage);
		try
		{
			using JsonDocument document = await BVAPI.GetJson($"/v3/social/guilds/{Uri.EscapeDataString(id)}");
			JsonElement guild = document.RootElement.TryGetProperty("guild", out JsonElement node) ? node : document.RootElement;
			_title.Text = Read(guild, "name") ?? _title.Text;
			int members = guild.TryGetProperty("memberCount", out JsonElement count) && count.TryGetInt32(out int parsed) ? parsed : 0;
			string joinType = Read(guild, "joinType") ?? "PUBLIC";
			_meta.Text = $"{members:N0} members  •  {joinType.ToLowerInvariant()} guild";
			StringBuilder body = new();
			body.AppendLine(Read(guild, "description") ?? "No description provided.");
			body.AppendLine(); body.AppendLine("## Ranks");
			if (guild.TryGetProperty("ranks", out JsonElement ranks) && ranks.ValueKind == JsonValueKind.Array)
				foreach (JsonElement rank in ranks.EnumerateArray()) body.AppendLine($"- **{Read(rank, "name") ?? "Member"}** — {(rank.TryGetProperty("memberCount", out JsonElement rankCount) ? rankCount.ToString() : "0")} members");
			_description.SetMarkdown(body.ToString());
			primary.Text = joinType == "PRIVATE" ? "Request to join" : "Join guild";
			primary.Pressed += async () =>
			{
				primary.Disabled = true;
				try { using JsonDocument response = await BVAPI.SendJson(System.Net.Http.HttpMethod.Post, $"/v3/social/guilds/{Uri.EscapeDataString(id)}/join"); primary.Text = response.RootElement.TryGetProperty("message", out JsonElement message) ? message.GetString() ?? "Joined" : "Joined"; }
				catch (Exception exception) { OS.Alert(exception.Message, "Guild"); primary.Disabled = false; }
			};
			membersButton.Pressed += () => _ = LoadGuildMembers(id);
			manage.Pressed += () => OS.ShellOpen(Globals.MainEndpoint.PathJoin($"/guilds/{id}/settings"));
		}
		catch (Exception exception) { _description.SetMarkdown("This guild could not be loaded."); BV.PrintErr(exception); }
	}

	private async System.Threading.Tasks.Task LoadGuildMembers(string id)
	{
		using JsonDocument document = await BVAPI.GetJson($"/v3/social/guilds/{Uri.EscapeDataString(id)}/members?limit=25&page=1");
		StringBuilder body = new("## Members\n\n");
		if (document.RootElement.TryGetProperty("members", out JsonElement members) && members.ValueKind == JsonValueKind.Array)
			foreach (JsonElement member in members.EnumerateArray()) body.AppendLine($"- **{Nested(member, "user", "username") ?? "Member"}** — {Nested(member, "rank", "name") ?? "Member"}");
		_description.SetMarkdown(body.ToString());
	}

	private async System.Threading.Tasks.Task LoadForumThread(string id)
	{
		try
		{
			using JsonDocument document = await BVAPI.GetJson($"/v3/forum/threads/{Uri.EscapeDataString(id)}");
			JsonElement root = document.RootElement;
			JsonElement thread = root.TryGetProperty("thread", out JsonElement threadNode) ? threadNode : root;
			_title.Text = Read(thread, "title") ?? _title.Text;
			string author = Nested(thread, "user", "username") ?? "Unknown author";
			_description.SetMarkdown(Read(thread, "content") ?? "No thread content.");
			int replyCount = 0;
			if (root.TryGetProperty("posts", out JsonElement posts) && posts.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement post in posts.EnumerateArray())
				{
					string postAuthor = Nested(post, "user", "username") ?? "Unknown author";
					AddReply(postAuthor, Read(post, "createdAt") ?? "", Read(post, "content") ?? "", false);
					replyCount++;
					if (post.TryGetProperty("replies", out JsonElement replies) && replies.ValueKind == JsonValueKind.Array)
						foreach (JsonElement reply in replies.EnumerateArray())
						{
							AddReply(Nested(reply, "user", "username") ?? "Unknown", Read(reply, "createdAt") ?? "", Read(reply, "content") ?? "", true);
							replyCount++;
						}
				}
			}
			_meta.Text = $"By {author} • Read/react-only on mobile";
			_repliesTitle.Text = replyCount == 1 ? "1 Reply" : $"{replyCount:N0} Replies";
			_repliesTitle.Visible = true;
			_replies.Visible = true;
		}
		catch (Exception exception) { _description.SetMarkdown("This thread could not be loaded. Please try again."); BV.PrintErr(exception); }
	}

	private void AddReply(string author, string timestamp, string content, bool nested)
	{
		MobileForumReply reply = _replyScene.Instantiate<MobileForumReply>();
		_replies.AddChild(reply);
		string displayTime = DateTime.TryParse(timestamp, out DateTime parsed) ? parsed.ToLocalTime().ToString("MMM d, yyyy • h:mm tt") : "";
		reply.Configure(author, displayTime, content, nested);
	}

	private static string? Read(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
	private static string? Nested(JsonElement element, string parent, string property) => element.TryGetProperty(parent, out JsonElement nested) ? Read(nested, property) : null;

	private async System.Threading.Tasks.Task LoadImage(string url)
	{
		const string userMarker = "/v3/thumbnails/USER_HEADSHOT/";
		int userIndex = url.IndexOf(userMarker, System.StringComparison.OrdinalIgnoreCase);
		if (userIndex >= 0) url = await BVAPI.ResolveThumbnailUrl("USER_HEADSHOT", url[(userIndex + userMarker.Length)..]);
		const string marker = "/v3/thumbnails/asset/";
		int index = url.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
		if (index >= 0) url = await BVAPI.ResolveThumbnailUrl("ASSET", url[(index + marker.Length)..]);
		if (string.IsNullOrWhiteSpace(url)) return;
		WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, resource => { if (IsInstanceValid(_image)) _image.Texture = (Texture2D)resource; });
	}
}

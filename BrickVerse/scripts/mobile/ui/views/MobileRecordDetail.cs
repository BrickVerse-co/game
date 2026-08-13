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
	private MobileViewEnum _returnView = MobileViewEnum.Dev;

	public override void _Ready()
	{
		_title = GetNode<Label>("Layout/Title");
		_meta = GetNode<Label>("Layout/Meta");
		_description = GetNode<MobileMarkdown>("Layout/Scroll/Description");
		_image = GetNode<TextureRect>("Layout/Image");
		GetNode<Button>("Layout/Header/Back").Pressed += () => MobileUI.Singleton.SwitchTo(_returnView, _returnView);
	}

	public override void ShowView(object? args)
	{
		if (args is not MobileRecordDetailArgs detail) return;
		_returnView = detail.ReturnView;
		_title.Text = detail.Title;
		_meta.Text = detail.Meta;
		_description.SetMarkdown(string.IsNullOrWhiteSpace(detail.Description) ? "No description provided." : detail.Description);
		_image.Visible = !string.IsNullOrWhiteSpace(detail.ImageUrl);
		if (_image.Visible) _ = LoadImage(detail.ImageUrl);
		if (detail.ReturnView == MobileViewEnum.Forum && !string.IsNullOrWhiteSpace(detail.Id)) _ = LoadForumThread(detail.Id);
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
			StringBuilder markdown = new();
			markdown.AppendLine(Read(thread, "content") ?? "No thread content.");
			markdown.AppendLine();
			markdown.AppendLine("---");
			markdown.AppendLine("## Replies");
			if (root.TryGetProperty("posts", out JsonElement posts) && posts.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement post in posts.EnumerateArray())
				{
					string postAuthor = Nested(post, "user", "username") ?? "Unknown author";
					markdown.AppendLine($"### {postAuthor}");
					markdown.AppendLine(Read(post, "content") ?? "");
					markdown.AppendLine();
					if (post.TryGetProperty("replies", out JsonElement replies) && replies.ValueKind == JsonValueKind.Array)
						foreach (JsonElement reply in replies.EnumerateArray()) markdown.AppendLine($"- **{Nested(reply, "user", "username") ?? "Unknown"}:** {Read(reply, "content") ?? ""}");
				}
			}
			_meta.Text = $"By {author} • Read/react-only on mobile";
			_description.SetMarkdown(markdown.ToString());
		}
		catch (Exception exception) { _description.SetMarkdown("This thread could not be loaded. Please try again."); BV.PrintErr(exception); }
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

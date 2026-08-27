using Godot;
using BrickVerse.Shared;
using System;
using System.Net.Http;

namespace BrickVerse.Creator.TeamCreate;

public sealed partial class TeamChatMessage : PanelContainer
{
	private const string DefaultHeadshot = "https://f004.backblazeb2.com/file/brickverse-ugc-public/defaults/headshot.png";
	private readonly System.Net.Http.HttpClient _http = new();
	private TextureRect _avatar = null!;

	public override void _Ready() => _avatar = GetNode<TextureRect>("Margin/Row/Avatar/Texture");

	public void Setup(string username, string message, string headshotUrl)
	{
		GetNode<Label>("Margin/Row/Content/Author").Text = username;
		GetNode<Label>("Margin/Row/Content/Message").Text = message;
		_ = LoadHeadshot(string.IsNullOrWhiteSpace(headshotUrl) ? DefaultHeadshot : NormalizeUrl(headshotUrl));
	}

	private async System.Threading.Tasks.Task LoadHeadshot(string url)
	{
		try
		{
			byte[] data = await _http.GetByteArrayAsync(url);
			Image image = new();
			Error error = image.LoadPngFromBuffer(data);
			if (error != Error.Ok) error = image.LoadJpgFromBuffer(data);
			if (error == Error.Ok && IsInstanceValid(this)) _avatar.Texture = ImageTexture.CreateFromImage(image);
		}
		catch (Exception error) { BV.PrintWarn("Could not load Team Chat headshot: ", error.Message); }
	}

	private static string NormalizeUrl(string url) => Uri.IsWellFormedUriString(url, UriKind.Absolute) ? url : Globals.ApiEndpoint.PathJoin(url);
}

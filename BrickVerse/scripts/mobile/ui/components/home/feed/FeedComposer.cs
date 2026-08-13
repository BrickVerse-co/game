// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Net.Http;
using System.Text.Json;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class FeedComposer : AcceptDialog
{
	[Signal] public delegate void PostCreatedEventHandler();
	private TextEdit _editor = null!;

	public override void _Ready()
	{
		_editor = GetNode<TextEdit>("Editor");
		Confirmed += Submit;
	}

	public void Open()
	{
		_editor.Text = "";
		PopupCentered();
	}

	private async void Submit()
	{
		string content = _editor.Text.Trim();
		if (string.IsNullOrWhiteSpace(content)) return;
		if (content.Length > 500) { OS.Alert("Posts can contain up to 500 characters.", "Post is too long"); return; }
		GetOkButton().Disabled = true;
		try
		{
			string json = $"{{\"content\":{JsonSerializer.Serialize(content)},\"type\":\"STATUS\"}}";
			using JsonDocument response = await BVAPI.SendJson(HttpMethod.Post, "/v3/social/posts", json);
			if (!response.RootElement.TryGetProperty("success", out JsonElement success) || !success.GetBoolean())
				throw new Exception(response.RootElement.TryGetProperty("message", out JsonElement message) ? message.GetString() : "Post failed");
			EmitSignal(SignalName.PostCreated);
			Hide();
		}
		catch (Exception exception) { OS.Alert(exception.Message, "Could not post"); }
		finally { GetOkButton().Disabled = false; }
	}
}

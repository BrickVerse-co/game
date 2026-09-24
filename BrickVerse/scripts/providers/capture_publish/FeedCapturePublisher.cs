using BrickVerse.Client.WebAPI;
using BrickVerse.Shared;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace BrickVerse.Providers.CapturePublish;

/// <summary>Publishes in-game captures as MEDIA posts through the same Feed API as the app/site.</summary>
public sealed class FeedCapturePublisher : ICapturePublisher
{
	private readonly BVHttpClient _http = new();

	public async Task Publish(byte[] photoPng, string caption, bool openPost)
	{
		string authorization = ClientAuthAPI.GetAuthorizationHeaderValue();
		if (string.IsNullOrWhiteSpace(authorization))
			throw new InvalidOperationException(
				"This game session has no world join token. Rejoin the published world before sharing to Feed.");
		_http.DefaultRequestHeaders["Authorization"] = authorization;

		using MultipartFormDataContent form = new();
		form.Add(BVHttpClient.FormFile("file", "brickverse-capture.png", photoPng, "image/png"));
		form.Add(BVHttpClient.FormString("caption", caption));
		using HttpResponseMessage post = await _http.PostAsync(
			Globals.ApiEndpoint.PathJoin("/v3/world/client/capture/feed"), form);
		string responseBody = await post.Content.ReadAsStringAsync();
		if (!post.IsSuccessStatusCode)
		{
			string message = responseBody;
			try
			{
				using JsonDocument error = JsonDocument.Parse(responseBody);
				if (error.RootElement.TryGetProperty("message", out JsonElement value))
					message = value.GetString() ?? message;
			}
			catch (JsonException) { }
			throw new InvalidOperationException($"Feed upload failed ({(int)post.StatusCode}): {message}");
		}

		if (openPost)
		{
			try
			{
				using JsonDocument response = JsonDocument.Parse(responseBody);
				if (response.RootElement.TryGetProperty("post", out JsonElement postValue)
					&& postValue.TryGetProperty("id", out JsonElement id)
					&& id.ValueKind == JsonValueKind.String)
					OS.ShellOpen(Globals.MainEndpoint.PathJoin($"/feed/{id.GetString()}"));
			}
			catch (JsonException) { }
		}
	}
}

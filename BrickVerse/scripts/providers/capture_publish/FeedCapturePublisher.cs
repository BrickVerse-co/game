using BrickVerse.Client.WebAPI;
using BrickVerse.Shared;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
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
			throw new InvalidOperationException("Sign in before sharing a capture to your Feed.");
		_http.DefaultRequestHeaders["Authorization"] = authorization;

		using MultipartFormDataContent form = new();
		form.Add(BVHttpClient.FormFile("file", "brickverse-capture.png", photoPng, "image/png"));
		form.Add(BVHttpClient.FormString("caption", caption));
		using HttpResponseMessage post = await _http.PostAsync(
			Globals.ApiEndpoint.PathJoin("/v3/world/client/capture/feed"), form);
		post.EnsureSuccessStatusCode();
	}
}

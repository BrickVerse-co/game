using BrickVerse.Attributes;
using BrickVerse.Utils;
using BrickVerse.Client.WebAPI;
using BrickVerse.Client.WebAPI.Interfaces;
using BrickVerse.Networking;
using BrickVerse.Shared;
using System.Net.Http;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace BrickVerse.Datamodel.Services;

public enum AdShape { Leaderboard, Square, Skyscraper, Video }
public sealed record AdPlacement(string Id, string CreativeId, string CreativeType, string CtaUri);

[Static("AdService"), ExplorerExclude, SaveIgnore]
public sealed partial class AdService : Instance
{
	private readonly BVHttpClient _client = new();
	[ScriptMethod]
	public async Task<AdPlacement?> GetAdAsync(AdShape shape = AdShape.Leaderboard, bool enableVideoAds = true)
	{
		string path = $"/v3/ads/random?shape={shape.ToString().ToUpperInvariant()}&enableVideo={enableVideoAds.ToString().ToLowerInvariant()}";
		string joinAuthorization = ClientAuthAPI.GetAuthorizationHeaderValue();
		JsonDocument response;
		if (!string.IsNullOrWhiteSpace(joinAuthorization))
		{
			using HttpRequestMessage request = new(HttpMethod.Get, Globals.ApiEndpoint.PathJoin(path.Replace("/v3/ads/random", "/v3/world/client/ads/random")));
			request.Headers.TryAddWithoutValidation("Authorization", joinAuthorization);
			using HttpResponseMessage httpResponse = await _client.SendAsync(request);
			if (httpResponse.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.NotFound) return null;
			httpResponse.EnsureSuccessStatusCode();
			response = JsonDocument.Parse(await httpResponse.Content.ReadAsStringAsync());
		}
		else response = await BVAPI.GetJson(path);
		using (response)
		{
			if (!response.RootElement.TryGetProperty("ad", out JsonElement ad)) return null;
			return new AdPlacement(ad.GetProperty("id").GetString() ?? "", ad.GetProperty("creativeId").GetString() ?? "", ad.TryGetProperty("creativeType", out JsonElement type) ? type.GetString() ?? "TEXTURE" : "TEXTURE", ad.GetProperty("ctaUri").GetString() ?? "");
		}
	}

	internal async Task RecordClickAsync(string id)
	{
		using JsonDocument _ = await BVAPI.SendJson(System.Net.Http.HttpMethod.Post, $"/v3/ads/{Uri.EscapeDataString(id)}/click");
	}
}

using System;
using System.Net.Http;
using System.Text.Json;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Shared;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileAdBanner : Button
{
	private string _adId = "";
	private string _cta = "";
	private TextureRect _image = null!;

	public override void _Ready()
	{
		_image = GetNode<TextureRect>("Image");
		Pressed += OpenAd;
		MobileMotion.Bind(this);
		_ = LoadAd();
	}

	private async System.Threading.Tasks.Task LoadAd()
	{
		Visible = false;
		try
		{
			using JsonDocument response = await BVAPI.GetJson("/v3/ads/random?shape=LEADERBOARD");
			if (!response.RootElement.TryGetProperty("ad", out JsonElement ad)) return;
			_adId = ad.GetProperty("id").ToString();
			_cta = ad.TryGetProperty("ctaUri", out JsonElement cta) ? cta.GetString() ?? "" : "";
			string creativeId = ad.TryGetProperty("creativeId", out JsonElement creative) ? creative.ToString() : "";
			string url = await BVAPI.ResolveThumbnailUrl("ASSET", creativeId);
			if (string.IsNullOrWhiteSpace(url)) return;
			WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, resource =>
			{
				if (!IsInstanceValid(_image)) return;
				_image.Texture = (Texture2D)resource;
				Visible = true;
			});
		}
		catch (Exception exception) { BV.PrintErr("Mobile ad unavailable: ", exception.Message); }
	}

	private async void OpenAd()
	{
		if (string.IsNullOrWhiteSpace(_cta)) return;
		try { using JsonDocument _ = await BVAPI.SendJson(HttpMethod.Post, $"/v3/ads/{Uri.EscapeDataString(_adId)}/click"); }
		catch (Exception exception) { BV.PrintErr("Ad click tracking failed: ", exception.Message); }
		if (TryOpenInApp(_cta)) return;
		OS.ShellOpen(AddTrackingParameters(_cta));
	}

	private bool TryOpenInApp(string cta)
	{
		if (MobileUI.Singleton == null) return false;

		string route;
		if (Uri.TryCreate(cta, UriKind.Absolute, out Uri? uri))
		{
			if (uri.Scheme.Equals("brickverse", StringComparison.OrdinalIgnoreCase))
				route = "/" + uri.Host + uri.AbsolutePath;
			else
			{
				if (!IsFirstPartyHost(uri.Host)) return false;
				route = uri.AbsolutePath;
			}
		}
		else route = cta.Split('?', '#')[0];

		string[] segments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length == 0) return false;
		string section = segments[0].ToLowerInvariant();
		string id = segments.Length > 1 ? Uri.UnescapeDataString(segments[1]) : "";

		switch (section)
		{
			case "worlds":
			case "places":
			case "games":
				if (long.TryParse(id, out long worldId)) MobileUI.Singleton.SwitchTo(MobileViewEnum.PlaceInfo, worldId);
				else MobileUI.Singleton.SwitchTo(MobileViewEnum.Worlds, MobileViewEnum.Worlds);
				return true;
			case "market":
			case "marketplace":
			case "catalog":
				if (!string.IsNullOrWhiteSpace(id)) MobileUI.Singleton.SwitchTo(MobileViewEnum.MarketplaceItem, id);
				else MobileUI.Singleton.SwitchTo(MobileViewEnum.Store, MobileViewEnum.Store);
				return true;
			case "guilds":
				if (!string.IsNullOrWhiteSpace(id))
					MobileUI.Singleton.SwitchTo(MobileViewEnum.RecordDetail,
						new MobileRecordDetailArgs("Loading guild…", "Guild", "Loading guild details…", "", MobileViewEnum.Guilds, id));
				else MobileUI.Singleton.SwitchTo(MobileViewEnum.Guilds, MobileViewEnum.Guilds);
				return true;
			case "users":
				if (string.IsNullOrWhiteSpace(id)) return false;
				MobileUI.Singleton.SwitchTo(MobileViewEnum.Profile, id);
				return true;
			case "forum":
				if (segments.Length > 2 && segments[1].Equals("thread", StringComparison.OrdinalIgnoreCase))
					MobileUI.Singleton.SwitchTo(MobileViewEnum.RecordDetail,
						new MobileRecordDetailArgs("Loading thread…", "Forum", "Loading thread…", "", MobileViewEnum.Forum, Uri.UnescapeDataString(segments[2])));
				else MobileUI.Singleton.SwitchTo(MobileViewEnum.Forum, MobileViewEnum.Forum);
				return true;
			case "events":
				if (!string.IsNullOrWhiteSpace(id))
					MobileUI.Singleton.SwitchTo(MobileViewEnum.RecordDetail,
						new MobileRecordDetailArgs("Event", "BrickVerse event", "Open Events to view the latest event information.", "", MobileViewEnum.Events, id));
				else MobileUI.Singleton.SwitchTo(MobileViewEnum.Events, MobileViewEnum.Events);
				return true;
			case "upgrade":
				MobileUI.Singleton.SwitchTo(MobileViewEnum.Upgrade, MobileViewEnum.Upgrade);
				return true;
			case "my" when segments.Length > 1:
				switch (segments[1].ToLowerInvariant())
				{
					case "avatar":
					case "character": MobileUI.Singleton.SwitchTo(MobileViewEnum.Avatar, MobileViewEnum.Avatar); return true;
					case "settings": MobileUI.Singleton.SwitchTo(MobileViewEnum.Settings, MobileViewEnum.Settings); return true;
					case "transactions": MobileUI.Singleton.SwitchTo(MobileViewEnum.Transactions, MobileViewEnum.Transactions); return true;
				}
				break;
		}
		return false;
	}

	private static bool IsFirstPartyHost(string host)
	{
		if (!Uri.TryCreate(Globals.MainEndpoint, UriKind.Absolute, out Uri? main)) return false;
		return host.Equals(main.Host, StringComparison.OrdinalIgnoreCase)
			|| host.Equals("brickverse.gg", StringComparison.OrdinalIgnoreCase)
			|| host.Equals("www.brickverse.gg", StringComparison.OrdinalIgnoreCase);
	}

	private string AddTrackingParameters(string cta)
	{
		string absolute = Uri.TryCreate(cta, UriKind.Absolute, out _) ? cta : Globals.MainEndpoint.PathJoin(cta);
		if (!Uri.TryCreate(absolute, UriKind.Absolute, out Uri? uri)) return cta;
		UriBuilder builder = new(uri);
		string tracking = $"utm_source=brickverse_mobile&utm_medium=ad&utm_campaign={Uri.EscapeDataString(_adId)}";
		builder.Query = string.IsNullOrWhiteSpace(uri.Query) ? tracking : uri.Query.TrimStart('?') + "&" + tracking;
		return builder.Uri.ToString();
	}
}

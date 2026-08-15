// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Text.Json;

namespace BrickVerse.Mobile.UI;

public partial class WorldsGrid : Control
{
	private const string PlaceCardPath = "res://scenes/mobile/components/shared/place_card.tscn";
	public PackedScene _placeCardPacked = null!;
	private LineEdit _search = null!;
	private TabBar _sort = null!;
	private OptionButton _genre = null!;
	private Label _status = null!;
	private PackedScene _skeletonScene = null!;
	private int _loadVersion;
	private bool _disposed;

	public override void _Ready()
	{
		_placeCardPacked = GD.Load<PackedScene>(PlaceCardPath);
		_search = GetNode<LineEdit>("../../Search");
		_sort = GetNode<TabBar>("../../Filters/Sort");
		_genre = GetNode<OptionButton>("../../Filters/Genre");
		_status = GetNode<Label>("../../Status");
		Resized += UpdateColumns;
		UpdateColumns();
		_skeletonScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/skeleton_card.tscn");
		_search.TextSubmitted += query => LoadWorlds(query);
		_sort.TabChanged += _ => LoadWorlds(_search.Text);
		_genre.ItemSelected += _ => LoadWorlds(_search.Text);
		LoadWorlds("");
	}

	public override void _ExitTree()
	{
		_disposed = true;
		_loadVersion++;
		base._ExitTree();
	}

	private async void LoadWorlds(string search)
	{
		int version = ++_loadVersion;
		_status.Text = "Loading worlds…";
		try
		{
			ClearCards();
			for (int index = 0; index < 6; index++) AddChild(_skeletonScene.Instantiate());
			string path = "/v3/worlds/discover?limit=30&platform=MOBILE";
			if (!string.IsNullOrWhiteSpace(search)) path += "&search=" + Uri.EscapeDataString(search);
			string sort = _genre.Selected > 0 ? "trending_genre" : _sort.CurrentTab switch { 1 => "featured", 2 => "top_trending", 3 => "top_rated", 4 => "top_playing_now", 5 => "up_and_coming", _ => "all" };
			path += "&sortBy=" + sort;
			if (_genre.Selected > 0)
			{
				path += "&genre=" + Uri.EscapeDataString(_genre.GetItemText(_genre.Selected).ToUpperInvariant().Replace(' ', '_'));
			}
			using JsonDocument response = await BVAPI.GetJson(path);
			if (_disposed || version != _loadVersion || !IsInstanceValid(this)) return;
			ClearCards();
			JsonElement worlds = response.RootElement.GetProperty("worlds");
			foreach (JsonElement item in worlds.EnumerateArray())
			{
				if (!long.TryParse(item.GetProperty("id").ToString(), out long id)) continue;
				PlaceCard card = _placeCardPacked.Instantiate<PlaceCard>();
				card.PlaceData = new APIWorldsData
				{
					Id = id,
					Name = ReadString(item, "name", "World"),
					Description = ReadString(item, "description", ""),
					Genre = ReadString(item, "genre", "All"),
					Playing = ReadInt(item, "totalPlayers", "playing"),
					Visits = ReadInt(item, "totalVisits", "visits"),
				};
				string thumbnailId = FindPrimaryThumbnailId(item);
				card.ThumbnailUrl = await BVAPI.ResolveThumbnailUrl("ASSET", thumbnailId);
				if (_disposed || version != _loadVersion || !IsInstanceValid(this)) return;
				AddChild(card);
				MobileMotion.Enter(card, GetChildCount() - 1);
			}
			_status.Text = worlds.GetArrayLength() == 0 ? "No worlds match those filters." : "";
		}
		catch (Exception ex)
		{
			if (_disposed || version != _loadVersion || !IsInstanceValid(this)) return;
			ClearCards();
			BV.PrintErr(ex);
			_status.Text = "Worlds could not be loaded. Try another search or refresh this page.";
			if (OS.IsDebugBuild())
			{
				OS.Alert(ex.ToString(), "Error loading games");
			}
			else
			{
				OS.Alert("Something went wrong, please try again.", "Error");
			}
		}
	}

	public void Refresh() => LoadWorlds(_search.Text);
	private void ClearCards()
	{
		foreach (Node child in GetChildren())
		{
			RemoveChild(child);
			child.QueueFree();
		}
	}
	private void UpdateColumns()
	{
		float available = GetViewportRect().Size.X - 40f;
		Set("columns", Mathf.Clamp(Mathf.FloorToInt(available / 220f), 2, 4));
	}

	private static string ReadString(JsonElement item, string name, string fallback) =>
		item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString() ?? fallback : fallback;
	private static int ReadInt(JsonElement item, params string[] names)
	{
		foreach (string name in names)
			if (item.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number)) return number;
		return 0;
	}
	private static string FindPrimaryThumbnailId(JsonElement item)
	{
		if (!item.TryGetProperty("universe", out JsonElement universe)
			|| !universe.TryGetProperty("universeThumbnails", out JsonElement thumbnails)
			|| thumbnails.ValueKind != JsonValueKind.Array) return "";
		JsonElement? fallback = null;
		foreach (JsonElement thumbnail in thumbnails.EnumerateArray())
		{
			fallback ??= thumbnail;
			if (thumbnail.TryGetProperty("primary", out JsonElement primary) && primary.GetBoolean())
				return ThumbnailId(thumbnail);
		}
		return fallback.HasValue ? ThumbnailId(fallback.Value) : "";
	}
	private static string ThumbnailId(JsonElement thumbnail)
	{
		if (!thumbnail.TryGetProperty("thumbnailId", out JsonElement id)) return "";
		return id.ToString();
	}
}

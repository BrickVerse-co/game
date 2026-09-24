// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using BrickVerse.Schemas.API;
using BrickVerse.Creator.Utils;
using BrickVerse.Shared;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Creator.UI;

public sealed partial class Toolbox : Control
{
	private const string CardScene = "res://scenes/creator/docks/toolbox/toolbox_card.tscn";
	private const string RecentPath = "user://creator/toolbox_recents.cfg";
	private const int RecentLimit = 36;
	private static readonly List<(APILibraryItem Item, LibraryQueryTypeEnum Type)> Recents = [];
	private static bool _recentsLoaded;

	private enum ViewMode
	{
		Discover,
		Inventory,
		Recent,
	}

	[Export]
	private Button _discoverTab = null!;

	[Export]
	private Button _inventoryTab = null!;

	[Export]
	private Button _recentTab = null!;

	[Export]
	private Control _browseOptions = null!;

	[Export]
	private OptionButton _typeOption = null!;

	[Export]
	private OptionButton _filterOption = null!;

	[Export]
	private LineEdit _searchEdit = null!;

	[Export]
	private Control _resultsContainer = null!;

	[Export]
	private Button _pagNavPrev = null!;

	[Export]
	private Label _pagNavLabel = null!;

	[Export]
	private Button _pagNavNext = null!;

	[Export]
	private Control _pagination = null!;

	[Export]
	private Control _loaderView = null!;

	[Export]
	private Control _noResultView = null!;

	[Export]
	private Label _noResultLabel = null!;

	public LibraryQueryTypeEnum QueryType = LibraryQueryTypeEnum.Model;
	public int CurrentPage = 1;
	public int MaxPage = 1;
	public string SearchQuery = "";
	public string AuthorQuery = "";
	public string TopCategory = "";
	public string SortBy = "newlyCreated";
	public ToolboxCard? SoundPreviewingCard;
	private ViewMode _view;
	private bool _discoverHome = true;
	private int _requestVersion;

	public override void _Ready()
	{
		LoadRecents();
		_searchEdit.TextSubmitted += _ => Search();
		_searchEdit.FocusExited += Search;
		_typeOption.ItemSelected += ChangeType;
		_filterOption.ItemSelected += ChangeSort;
		_discoverTab.Pressed += () => SelectView(ViewMode.Discover);
		_inventoryTab.Pressed += () => SelectView(ViewMode.Inventory);
		_recentTab.Pressed += () => SelectView(ViewMode.Recent);
		_pagNavPrev.Pressed += () =>
		{
			if (CurrentPage > 1)
				CurrentPage--;
			Refresh();
		};
		_pagNavNext.Pressed += () =>
		{
			if (CurrentPage < MaxPage)
				CurrentPage++;
			Refresh();
		};
		Refresh();
	}

	private void SelectView(ViewMode view)
	{
		_view = view;
		_discoverHome = view == ViewMode.Discover;
		CurrentPage = 1;
		SearchQuery = "";
		AuthorQuery = "";
		TopCategory = "";
		_searchEdit.Text = "";
		_searchEdit.PlaceholderText = view switch
		{
			ViewMode.Inventory => "Search your inventory...",
			ViewMode.Recent => "Search recently used assets...",
			_ => "Search assets or by:creator...",
		};
		Refresh();
	}

	private void Search()
	{
		string raw = _searchEdit.Text.Trim();
		string nextSearch = raw;
		string nextAuthor = "";
		if (raw.StartsWith('@')) { nextAuthor = raw[1..].Trim(); nextSearch = ""; }
		else if (raw.StartsWith("by:", StringComparison.OrdinalIgnoreCase)) { nextAuthor = raw[3..].Trim(); nextSearch = ""; }
		else if (raw.StartsWith("creator:", StringComparison.OrdinalIgnoreCase)) { nextAuthor = raw[8..].Trim(); nextSearch = ""; }
		if (SearchQuery == nextSearch && AuthorQuery == nextAuthor && !_discoverHome)
			return;
		SearchQuery = nextSearch;
		AuthorQuery = nextAuthor;
		CurrentPage = 1;
		_discoverHome = _view == ViewMode.Discover && nextSearch.Length == 0 && nextAuthor.Length == 0;
		Refresh();
	}

	private void ChangeType(long index)
	{
		QueryType = TypeFromIndex(index);
		TopCategory = "";
		CurrentPage = 1;
		Refresh();
	}

	private void ChangeSort(long index)
	{
		SortBy = index switch
		{
			1 => "recentlyUpdated",
			2 => "featured",
			3 => "sponsored",
			4 => "priceLowToHigh",
			5 => "priceHighToLow",
			_ => "newlyCreated",
		};
		CurrentPage = 1;
		_discoverHome = false;
		Refresh();
	}

	private static LibraryQueryTypeEnum TypeFromIndex(long index) =>
		index switch
		{
			1 => LibraryQueryTypeEnum.Image,
			2 => LibraryQueryTypeEnum.Audio,
			3 => LibraryQueryTypeEnum.Mesh,
			4 => LibraryQueryTypeEnum.Addon,
			5 => LibraryQueryTypeEnum.Font,
			_ => LibraryQueryTypeEnum.Model,
		};

	private static int IndexFromType(LibraryQueryTypeEnum type) =>
		type switch
		{
			LibraryQueryTypeEnum.Image => 1,
			LibraryQueryTypeEnum.Audio => 2,
			LibraryQueryTypeEnum.Mesh => 3,
			LibraryQueryTypeEnum.Addon => 4,
			LibraryQueryTypeEnum.Font => 5,
			_ => 0,
		};

	public void Refresh()
	{
		int version = ++_requestVersion;
		Clear();
		_discoverTab.SetPressedNoSignal(_view == ViewMode.Discover);
		_inventoryTab.SetPressedNoSignal(_view == ViewMode.Inventory);
		_recentTab.SetPressedNoSignal(_view == ViewMode.Recent);
		_browseOptions.Visible = _view == ViewMode.Discover;
		_filterOption.Visible = _view == ViewMode.Discover;
		_pagination.Visible = _view == ViewMode.Discover && !_discoverHome;
		_noResultView.Visible = false;
		_loaderView.Visible = true;
		if (_view == ViewMode.Recent)
			RenderRecent(version);
		else if (_view == ViewMode.Inventory)
			LoadInventory(version);
		else if (_discoverHome)
			LoadHome(version);
		else
			LoadStore(version);
	}

	public void Clear()
	{
		SoundPreviewingCard?.StopSoundPreview();
		foreach (Node item in _resultsContainer.GetChildren())
			item.QueueFree();
	}

	private async void LoadHome(int version)
	{
		AddHeading("Categories", "Browse assets made for the way you build");
		AddCategories();
		try
		{
			Task<APILibraryResponse> trending = BVAPI.GetLibrary(
				QueryType,
				1,
				"",
				"",
				"",
				"featured"
			);
			Task<APILibraryResponse> newest = BVAPI.GetLibrary(
				QueryType,
				1,
				"",
				"",
				"",
				"newlyCreated"
			);
			await Task.WhenAll(trending, newest);
			if (version != _requestVersion)
				return;
			AddSection("Trending", "Popular picks from creators", trending.Result.Data);
			AddSection(
				"Discover something new",
				"Fresh additions to the Creator Store",
				newest.Result.Data
			);
			_loaderView.Visible = false;
		}
		catch (Exception ex)
		{
			ShowError(version, "Could not load the Creator Store", ex);
		}
	}

	private void AddCategories()
	{
		GridContainer grid = new()
		{
			Columns = 2,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		grid.AddThemeConstantOverride("h_separation", 7);
		grid.AddThemeConstantOverride("v_separation", 7);
		_resultsContainer.AddChild(grid);
		AddCategory(grid, "3D Assets", "cube.svg", LibraryQueryTypeEnum.Model, "");
		AddCategory(grid, "2D Assets", "image-square.svg", LibraryQueryTypeEnum.Image, "");
		AddCategory(grid, "Audio", "waveform.svg", LibraryQueryTypeEnum.Audio, "");
		AddCategory(grid, "Plugins", "addon.svg", LibraryQueryTypeEnum.Addon, "engineTools");
		AddCategory(
			grid,
			"Gameplay",
			"gamepad.svg",
			LibraryQueryTypeEnum.Model,
			"gameplayFeatures"
		);
		AddCategory(
			grid,
			"Environments",
			"mountain.svg",
			LibraryQueryTypeEnum.Model,
			"environments"
		);
	}

	private void AddCategory(
		GridContainer grid,
		string label,
		string icon,
		LibraryQueryTypeEnum type,
		string category
	)
	{
		Button button = new()
		{
			CustomMinimumSize = new Vector2(104, 72),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			TooltipText = label,
		};
		VBoxContainer content = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
			Alignment = BoxContainer.AlignmentMode.Center,
		};
		button.AddChild(content);
		content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepSize, 6);
		TextureRect iconView = new()
		{
			CustomMinimumSize = new Vector2(26, 26),
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Texture = GD.Load<Texture2D>($"res://assets/textures/ui-icons/{icon}"),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		Label text = new()
		{
			Text = label,
			HorizontalAlignment = HorizontalAlignment.Center,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		content.AddChild(iconView);
		content.AddChild(text);
		button.Pressed += () =>
		{
			QueryType = type;
			TopCategory = category;
			_typeOption.Select(IndexFromType(type));
			_discoverHome = false;
			CurrentPage = 1;
			Refresh();
		};
		grid.AddChild(button);
	}

	private async void LoadStore(int version)
	{
		_pagNavNext.Disabled = true;
		_pagNavPrev.Disabled = true;
		try
		{
			APILibraryResponse response = await BVAPI.GetLibrary(
				QueryType,
				CurrentPage,
				SearchQuery,
				AuthorQuery,
				TopCategory,
				SortBy
			);
			if (version != _requestVersion)
				return;
			MaxPage = Math.Max(1, response.Meta.LastPage);
			_pagNavPrev.Disabled = CurrentPage == 1;
			_pagNavNext.Disabled = CurrentPage >= MaxPage;
			_pagNavLabel.Text = $"Page {CurrentPage} of {MaxPage}";
			_loaderView.Visible = false;
			ShowEmpty(response.Data.Length == 0, "No assets found");
			AddGrid(response.Data, QueryType);
		}
		catch (Exception ex)
		{
			ShowError(version, "Unable to load assets", ex);
		}
	}

	private async void LoadInventory(int version)
	{
		try
		{
			// Inventory is a Creator OAuth route. Refresh first so a long-running
			// editor session never sends the stale token cached by BVAPI.
			string accessToken = await CreatorAPI.GetValidAccessTokenAsync();
			if (version != _requestVersion) return;
			BVAPI.SetAuthToken(accessToken);
			using JsonDocument response = await BVAPI.GetJson("/v3/asset/inventory?limit=50");
			if (version != _requestVersion)
				return;
			List<(APILibraryItem, LibraryQueryTypeEnum)> items = [];
			if (response.RootElement.TryGetProperty("items", out JsonElement list))
				foreach (JsonElement entry in list.EnumerateArray())
				{
					if (!entry.TryGetProperty("asset", out JsonElement asset))
						continue;
					LibraryQueryTypeEnum type = ParseType(Read(asset, "assetType"));
					string name = Read(asset, "name");
					if (
						SearchQuery.Length > 0
						&& !name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
					)
						continue;
					string id = Read(asset, "id");
					items.Add(
						(
							new APILibraryItem
							{
								ID = id,
								Name = name,
								ThumbnailUrl = Globals.ApiEndpoint.PathJoin(
									$"/v3/thumbnails/asset/{id}"
								),
								CreatorName = "Your inventory",
							},
							type
						)
					);
				}
			_loaderView.Visible = false;
			ShowEmpty(items.Count == 0, "Your inventory has no matching assets");
			AddMixedGrid(items);
		}
		catch (Exception ex)
		{
			if (version != _requestVersion) return;
			ShowError(version, "Sign in to view your inventory", ex);
		}
	}

	private void RenderRecent(int version)
	{
		if (version != _requestVersion)
			return;
		List<(APILibraryItem Item, LibraryQueryTypeEnum Type)> items =
			SearchQuery.Length == 0
				? [.. Recents]
				: Recents.FindAll(x =>
					x.Item.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
				);
		_loaderView.Visible = false;
		ShowEmpty(items.Count == 0, "Assets you insert will appear here");
		AddMixedGrid(items);
	}

	private void AddHeading(string title, string subtitle)
	{
		VBoxContainer box = new();
		Label heading = new() { Text = title };
		heading.AddThemeFontSizeOverride("font_size", 14);
		Label detail = new() { Text = subtitle, ThemeTypeVariation = "CreatorMutedLabel" };
		detail.AddThemeFontSizeOverride("font_size", 10);
		box.AddChild(heading);
		box.AddChild(detail);
		_resultsContainer.AddChild(box);
	}

	private void AddSection(string title, string subtitle, APILibraryItem[] items)
	{
		AddHeading(title, subtitle);
		HFlowContainer flow = NewFlow();
		_resultsContainer.AddChild(flow);
		for (int i = 0; i < Math.Min(6, items.Length); i++)
			AddCard(flow, items[i], QueryType);
	}

	private void AddGrid(APILibraryItem[] items, LibraryQueryTypeEnum type)
	{
		HFlowContainer flow = NewFlow();
		_resultsContainer.AddChild(flow);
		foreach (APILibraryItem item in items)
			AddCard(flow, item, type);
	}

	private void AddMixedGrid(IEnumerable<(APILibraryItem Item, LibraryQueryTypeEnum Type)> items)
	{
		HFlowContainer flow = NewFlow();
		_resultsContainer.AddChild(flow);
		foreach ((APILibraryItem item, LibraryQueryTypeEnum type) in items)
			AddCard(flow, item, type);
	}

	private static HFlowContainer NewFlow()
	{
		HFlowContainer flow = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		flow.AddThemeConstantOverride("h_separation", 8);
		flow.AddThemeConstantOverride("v_separation", 9);
		return flow;
	}

	private void AddCard(Control parent, APILibraryItem item, LibraryQueryTypeEnum type)
	{
		ToolboxCard card = Globals.CreateInstanceFromScene<ToolboxCard>(CardScene);
		card.ItemData = item;
		card.ItemType = type;
		card.ToolboxParent = this;
		parent.AddChild(card);
	}

	private void ShowEmpty(bool visible, string message)
	{
		_noResultLabel.Text = message;
		_noResultView.Visible = visible;
	}

	private void ShowError(int version, string message, Exception ex)
	{
		if (version != _requestVersion)
			return;
		BV.PrintErr("Toolbox: ", ex);
		_loaderView.Visible = false;
		ShowEmpty(true, message);
		_pagNavLabel.Text = message;
	}

	private static string Read(JsonElement element, string key) =>
		element.TryGetProperty(key, out JsonElement value)
		&& value.ValueKind == JsonValueKind.String
			? value.GetString() ?? ""
			: "";

	private static LibraryQueryTypeEnum ParseType(string type) =>
		type.ToUpperInvariant() switch
		{
			"TEXTURE" => LibraryQueryTypeEnum.Image,
			"SOUND" => LibraryQueryTypeEnum.Audio,
			"MESH" => LibraryQueryTypeEnum.Mesh,
			"PLUGIN" => LibraryQueryTypeEnum.Addon,
			"FONT" => LibraryQueryTypeEnum.Font,
			_ => LibraryQueryTypeEnum.Model,
		};

	public static void RecordRecent(APILibraryItem item, LibraryQueryTypeEnum type)
	{
		LoadRecents();
		Recents.RemoveAll(x => x.Item.ID == item.ID && x.Type == type);
		Recents.Insert(0, (item, type));
		if (Recents.Count > RecentLimit)
			Recents.RemoveRange(RecentLimit, Recents.Count - RecentLimit);
		ConfigFile config = new();
		config.SetValue("recent", "count", Recents.Count);
		for (int i = 0; i < Recents.Count; i++)
		{
			var saved = Recents[i];
			string section = $"item_{i}";
			config.SetValue(section, "id", saved.Item.ID);
			config.SetValue(section, "name", saved.Item.Name);
			config.SetValue(section, "thumbnail", saved.Item.ThumbnailUrl);
			config.SetValue(section, "creator", saved.Item.CreatorName);
			config.SetValue(section, "type", (int)saved.Type);
		}
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://creator"));
		config.Save(RecentPath);
	}

	private static void LoadRecents()
	{
		if (_recentsLoaded)
			return;
		_recentsLoaded = true;
		ConfigFile config = new();
		if (config.Load(RecentPath) != Error.Ok)
			return;
		int count = Math.Min(RecentLimit, (int)config.GetValue("recent", "count", 0));
		for (int i = 0; i < count; i++)
		{
			string section = $"item_{i}";
			string id = (string)config.GetValue(section, "id", "");
			if (id.Length == 0)
				continue;
			Recents.Add(
				(
					new APILibraryItem
					{
						ID = id,
						Name = (string)config.GetValue(section, "name", "Asset"),
						ThumbnailUrl = (string)config.GetValue(section, "thumbnail", ""),
						CreatorName = (string)config.GetValue(section, "creator", "Creator Store"),
					},
					(LibraryQueryTypeEnum)(int)config.GetValue(section, "type", 0)
				)
			);
		}
	}
}

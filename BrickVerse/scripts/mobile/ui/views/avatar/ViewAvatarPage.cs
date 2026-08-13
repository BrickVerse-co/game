// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using BrickVerse.Mobile.Utils;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Shared;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class ViewAvatarPage : MobileViewBase
{
	private GridContainer _grid = null!;
	private TabBar _category = null!;
	private Label _status = null!;
	private TextureRect _preview = null!;
	private PackedScene _cardScene = null!;
	private readonly List<(string Id, string Name, string Type)> _inventory = [];
	private readonly HashSet<string> _equipped = [];
	private string _appearanceId = "";

	public override void _Ready()
	{
		_grid = GetNode<GridContainer>("Layout/Scroll/Grid");
		_category = GetNode<TabBar>("Layout/Category");
		_status = GetNode<Label>("Layout/Status");
		_preview = GetNode<TextureRect>("Layout/Preview");
		_cardScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/grid_card.tscn");
		_category.TabChanged += _ => RenderInventory();
		GetNode<Button>("Layout/Header/Refresh").Pressed += () => _ = LoadAsync();
		GetNode<Button>("Layout/Save").Pressed += Save;
		MobileMotion.Bind(GetNode<Button>("Layout/Header/Refresh"));
		MobileMotion.Bind(GetNode<Button>("Layout/Save"));
	}

	public override void ShowView(object? args)
	{
		base.ShowView(args);
		if (_inventory.Count == 0) _ = LoadAsync();
	}

	public override void RefreshView() => _ = LoadAsync();

	private async System.Threading.Tasks.Task LoadAsync()
	{
		_status.Text = "Loading your avatar...";
		_inventory.Clear(); _equipped.Clear();
		try
		{
			using JsonDocument appearances = await BVAPI.GetJson("/v3/character/appearances");
			if (appearances.RootElement.TryGetProperty("appearances", out JsonElement list))
				foreach (JsonElement appearance in list.EnumerateArray()) if (appearance.TryGetProperty("isEquipped", out JsonElement equipped) && equipped.ValueKind == JsonValueKind.True)
				{
					_appearanceId = appearance.GetProperty("id").ToString();
					if (appearance.TryGetProperty("accessories", out JsonElement accessories)) foreach (JsonElement accessory in accessories.EnumerateArray()) _equipped.Add(accessory.ValueKind == JsonValueKind.Object && accessory.TryGetProperty("id", out JsonElement id) ? id.ToString() : accessory.ToString());
				}
			using JsonDocument inventory = await BVAPI.GetJson("/v3/marketplace/inventory?limit=50");
			foreach (JsonElement owned in inventory.RootElement.GetProperty("items").EnumerateArray())
			{
				JsonElement item = owned.GetProperty("item");
				_inventory.Add((item.GetProperty("id").ToString(), item.GetProperty("name").GetString() ?? "Item", item.GetProperty("type").GetString() ?? "Accessory"));
			}
			string previewUrl = await BVAPI.ResolveThumbnailUrl("USER_BODYSHOT", BVMobileAuthAPI.CurrentUserInfo.Id);
			if (!string.IsNullOrWhiteSpace(previewUrl)) WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = previewUrl }, resource => { if (IsInstanceValid(_preview)) _preview.Texture = (Texture2D)resource; });
			RenderInventory(); _status.Text = $"{_equipped.Count} items equipped";
		}
		catch (Exception exception) { _status.Text = "Avatar inventory could not be loaded."; BV.PrintErr(exception); }
	}

	private void RenderInventory()
	{
		foreach (Node child in _grid.GetChildren()) child.QueueFree();
		string filter = _category.CurrentTab == 0 ? "" : _category.GetTabTitle(_category.CurrentTab);
		foreach ((string id, string name, string type) in _inventory)
		{
			if (!string.IsNullOrWhiteSpace(filter) && !type.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
			MobileListCard card = _cardScene.Instantiate<MobileListCard>();
			_grid.AddChild(card); card.Configure(name, _equipped.Contains(id) ? "Equipped" : type, "Tap to toggle", "marketplace-item://" + id);
			card.Pressed += () => { if (!_equipped.Add(id)) _equipped.Remove(id); RenderInventory(); _status.Text = $"{_equipped.Count} items selected"; };
		}
	}

	private async void Save()
	{
		if (string.IsNullOrWhiteSpace(_appearanceId)) { OS.Alert("Create an appearance on your account first.", "Avatar"); return; }
		Button save = GetNode<Button>("Layout/Save"); save.Disabled = true;
		try
		{
			string json = $"{{\"id\":{JsonSerializer.Serialize(_appearanceId)},\"accessories\":{JsonSerializer.Serialize(_equipped)}}}";
			using JsonDocument _ = await BVAPI.SendJson(HttpMethod.Patch, "/v3/character/appearance", json);
			_status.Text = "Avatar saved. New thumbnails are rendering.";
		}
		catch (Exception exception) { OS.Alert(exception.Message, "Avatar save failed"); }
		finally { save.Disabled = false; }
	}
}

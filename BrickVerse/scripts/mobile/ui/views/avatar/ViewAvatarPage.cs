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
using BrickVerse.Datamodel;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class ViewAvatarPage : MobileViewBase
{
	private GridContainer _grid = null!;
	private TabBar _category = null!;
	private Label _status = null!;
	private Control _previewSkeleton = null!;
	private PackedScene _cardScene = null!;
	private PackedScene _skeletonScene = null!;
	private BrickversianModel? _previewModel;
	private ConfirmationDialog _discardWarning = null!;
	private Action? _pendingNavigation;
	private bool _allowNavigationOnce;
	private bool _dirty;
	private readonly List<(string Id, string Name, string Type)> _inventory = [];
	private readonly HashSet<string> _equipped = [];
	private readonly HashSet<string> _savedEquipped = [];
	private LineEdit _search = null!;
	private string _appearanceId = "";

	public override void _Ready()
	{
		_grid = GetNode<GridContainer>("Layout/Scroll/Grid");
		_category = GetNode<TabBar>("Layout/Category");
		_status = GetNode<Label>("Layout/Status");
		_previewSkeleton = GetNode<Control>("Layout/Preview/Skeleton");
		_search = GetNode<LineEdit>("Layout/Search");
		_cardScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/grid_card.tscn");
		_skeletonScene = GD.Load<PackedScene>("res://scenes/mobile/components/avatar/avatar_item_skeleton.tscn");
		_discardWarning = GetNode<ConfirmationDialog>("DiscardWarning");
		_discardWarning.Confirmed += ConfirmNavigation;
		CreatePreviewModel();
		_category.TabChanged += _ => RenderInventory();
		_search.TextChanged += _ => RenderInventory();
		GetNode<Button>("Layout/Header/Refresh").Pressed += () => _ = LoadAsync();
		GetNode<Button>("Layout/Actions/Save").Pressed += Save;
		GetNode<Button>("Layout/Actions/Discard").Pressed += Discard;
		MobileMotion.Bind(GetNode<Button>("Layout/Header/Refresh"));
		MobileMotion.Bind(GetNode<Button>("Layout/Actions/Save"));
		MobileMotion.Bind(GetNode<Button>("Layout/Actions/Discard"));
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
		_previewSkeleton.Visible = true;
		_inventory.Clear(); _equipped.Clear();
		foreach (Node child in _grid.GetChildren()) child.QueueFree();
		for (int index = 0; index < 6; index++) _grid.AddChild(_skeletonScene.Instantiate());
		try
		{
			using JsonDocument appearances = await BVAPI.GetJson("/v3/character/appearances");
			if (appearances.RootElement.TryGetProperty("appearances", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
			{
				JsonElement? first = null;
				bool foundEquipped = false;
				foreach (JsonElement appearance in list.EnumerateArray())
				{
					first ??= appearance.Clone();
					if (!appearance.TryGetProperty("isEquipped", out JsonElement equipped) || equipped.ValueKind != JsonValueKind.True) continue;
					ReadAppearance(appearance);
					foundEquipped = true;
					break;
				}
				if (!foundEquipped && first.HasValue)
				{
					string firstId = first.Value.GetProperty("id").ToString();
					using JsonDocument _ = await BVAPI.SendJson(HttpMethod.Post, $"/v3/character/appearance/{Uri.EscapeDataString(firstId)}/equip");
					ReadAppearance(first.Value);
				}
				else if (!foundEquipped && !first.HasValue)
				{
					using JsonDocument created = await BVAPI.SendJson(HttpMethod.Patch, "/v3/character/appearance", "{\"isEquipped\":true}");
					if (created.RootElement.TryGetProperty("appearance", out JsonElement appearance)) ReadAppearance(appearance);
				}
			}
			using JsonDocument inventory = await BVAPI.GetJson("/v3/marketplace/inventory?limit=50");
			foreach (JsonElement owned in inventory.RootElement.GetProperty("items").EnumerateArray())
			{
				JsonElement item = owned.GetProperty("item");
				string itemId = owned.TryGetProperty("itemId", out JsonElement ownedItemId) ? ownedItemId.ToString() : item.GetProperty("id").ToString();
				_inventory.Add((itemId, item.GetProperty("name").GetString() ?? "Item", item.GetProperty("type").GetString() ?? "Accessory"));
			}
			_previewModel?.LoadAppearance(BVMobileAuthAPI.CurrentUserInfo.Id, false);
			_savedEquipped.Clear(); _savedEquipped.UnionWith(_equipped);
			_dirty = false;
			RenderInventory(); _status.Text = $"{_equipped.Count} items equipped";
		}
		catch (Exception exception) { _previewSkeleton.Visible = false; _status.Text = "Avatar inventory could not be loaded."; BV.PrintErr(exception); }
	}

	private void RenderInventory()
	{
		foreach (Node child in _grid.GetChildren()) child.QueueFree();
		string filter = _category.CurrentTab == 0 ? "" : _category.GetTabTitle(_category.CurrentTab);
		string query = _search.Text.Trim();
		foreach ((string id, string name, string type) in _inventory)
		{
			if (!string.IsNullOrWhiteSpace(filter) && !type.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
			if (!string.IsNullOrWhiteSpace(query) && !name.Contains(query, StringComparison.OrdinalIgnoreCase) && !type.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
			MobileListCard card = _cardScene.Instantiate<MobileListCard>();
			_grid.AddChild(card); card.Configure(name, _equipped.Contains(id) ? "Equipped" : type, "Tap to toggle", "marketplace-item://" + id);
			card.Pressed += () =>
			{
				if (!_equipped.Add(id)) _equipped.Remove(id);
				else if (IsSingleSlot(type))
					foreach (var other in _inventory)
						if (other.Id != id && IsSameSlot(type, other.Type)) _equipped.Remove(other.Id);
				_dirty = !_equipped.SetEquals(_savedEquipped);
				RenderInventory();
				_status.Text = $"{_equipped.Count} items selected • {(_dirty ? "Unsaved changes" : "Saved")}";
				_previewSkeleton.Visible = true;
				_previewModel?.LoadAppearance(BVMobileAuthAPI.CurrentUserInfo.Id, false);
			};
		}
	}

	private void ReadAppearance(JsonElement appearance)
	{
		_appearanceId = appearance.GetProperty("id").ToString();
		if (!appearance.TryGetProperty("accessories", out JsonElement accessories) || accessories.ValueKind != JsonValueKind.Array) return;
		foreach (JsonElement accessory in accessories.EnumerateArray())
			_equipped.Add(accessory.ValueKind == JsonValueKind.Object && accessory.TryGetProperty("id", out JsonElement id) ? id.ToString() : accessory.ToString());
	}

	private static bool IsSingleSlot(string type) => type.Equals("Shirt", StringComparison.OrdinalIgnoreCase)
		|| type.Equals("Pants", StringComparison.OrdinalIgnoreCase)
		|| type.Equals("Face", StringComparison.OrdinalIgnoreCase);

	private static bool IsSameSlot(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);

	private void Discard()
	{
		_equipped.Clear(); _equipped.UnionWith(_savedEquipped);
		RenderInventory();
		_status.Text = "Changes discarded";
		_dirty = false;
	}

	private async void Save()
	{
		if (string.IsNullOrWhiteSpace(_appearanceId)) { OS.Alert("Create an appearance on your account first.", "Avatar"); return; }
		Button save = GetNode<Button>("Layout/Actions/Save"); save.Disabled = true;
		try
		{
			string json = $"{{\"id\":{JsonSerializer.Serialize(_appearanceId)},\"accessories\":{JsonSerializer.Serialize(_equipped)}}}";
			using JsonDocument _ = await BVAPI.SendJson(HttpMethod.Patch, "/v3/character/appearance", json);
			_savedEquipped.Clear(); _savedEquipped.UnionWith(_equipped);
			_dirty = false;
			_previewModel?.LoadAppearance(BVMobileAuthAPI.CurrentUserInfo.Id, false);
			_status.Text = "Avatar saved. New thumbnails are rendering.";
		}
		catch (Exception exception) { OS.Alert(exception.Message, "Avatar save failed"); }
		finally { save.Disabled = false; }
	}

	private void CreatePreviewModel()
	{
		_previewModel = new BrickversianModel();
		_previewModel.AvatarLoaded += OnPreviewLoaded;
		GetNode<SubViewport>("Layout/Preview/ViewportContainer/Viewport").AddChild(_previewModel.GDNode);
		_previewModel.InitEntry();
		_previewModel.Position = Vector3.Zero;
	}

	private async void OnPreviewLoaded()
	{
		if (_previewModel == null) return;
		foreach (BrickVerse.Datamodel.Instance child in _previewModel.GetChildren())
		{
			if (child is not Accessory and not Clothing) continue;
			(string Id, string Name, string Type) match = _inventory.Find(item => item.Name.Equals(child.Name, StringComparison.OrdinalIgnoreCase));
			if (!string.IsNullOrWhiteSpace(match.Id) && !_equipped.Contains(match.Id)) child.Delete();
		}
		foreach ((string id, string name, string type) in _inventory)
		{
			if (!_equipped.Contains(id) || _savedEquipped.Contains(id)) continue;
			if (type.Equals("Face", StringComparison.OrdinalIgnoreCase) || type.Equals("Shirt", StringComparison.OrdinalIgnoreCase) || type.Equals("Pants", StringComparison.OrdinalIgnoreCase)) continue;
			try
			{
				Accessory? accessory = await _previewModel.Root.Insert.AccessoryAsync(id);
				if (accessory != null) accessory.Parent = _previewModel;
			}
			catch (Exception exception) { BV.PrintErr($"Could not preview avatar item {id}: {exception.Message}"); }
		}
		if (IsInstanceValid(_previewSkeleton)) _previewSkeleton.Visible = false;
	}

	public override bool TryNavigateAway(Action continuation)
	{
		if (_allowNavigationOnce) { _allowNavigationOnce = false; return true; }
		if (!_dirty) return true;
		_pendingNavigation = continuation;
		_discardWarning.PopupCentered();
		return false;
	}

	private void ConfirmNavigation()
	{
		Discard();
		_allowNavigationOnce = true;
		Action? continuation = _pendingNavigation;
		_pendingNavigation = null;
		continuation?.Invoke();
	}

	public override void _ExitTree()
	{
		if (_previewModel != null) _previewModel.AvatarLoaded -= OnPreviewLoaded;
		_previewModel?.Delete();
		_previewModel = null;
		base._ExitTree();
	}
}

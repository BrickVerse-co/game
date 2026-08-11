// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.UI.Popups;
using BrickVerse.Creator.Utils;
using BrickVerse.Creator.Managers;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Schemas.API;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Collections.Generic;
using Mesh = BrickVerse.Datamodel.Mesh;

namespace BrickVerse.Creator.UI;

/// <summary>Creator-owned and acquired asset browser, similar to Studio's Asset Manager.</summary>
public sealed partial class AssetManagerDock : MarginContainer
{
	private static readonly PackedScene CardScene = GD.Load<PackedScene>("res://scenes/creator/docks/asset-manager/asset_manager_card.tscn");
	[Export] private LineEdit _search = null!;
	[Export] private OptionButton _type = null!;
	[Export] private OptionButton _scope = null!;
	[Export] private GridContainer _grid = null!;
	[Export] private Label _status = null!;
	[Export] private Button _previous = null!;
	[Export] private Button _next = null!;
	[Export] private Button _publishSelection = null!;
	private readonly List<string?> _cursors = [null];
	private string? _nextCursor;
	private int _page = 1;
	private int _generation;

	public override void _Ready()
	{
		foreach (string label in new[] { "All", "Prefabs", "Textures", "Sounds", "Meshes", "Animations", "Plugins" }) _type.AddItem(label);
		_scope.AddItem("Created");
		_scope.AddItem("Owned");
		_scope.AddItem("Universe owner");
		_publishSelection.Pressed += PublishSelection;

		_search.TextSubmitted += _ => ResetAndRefresh();
		_search.FocusExited += ResetAndRefresh;
		_type.ItemSelected += _ => ResetAndRefresh();
		_scope.ItemSelected += _ => ResetAndRefresh();
		_previous.Pressed += Previous;
		_next.Pressed += Next;
		Refresh();
	}

	private PublishPopup.PublishTypeEnum? SelectedType() => _type.Selected switch
	{
		1 => PublishPopup.PublishTypeEnum.Prefab,
		2 => PublishPopup.PublishTypeEnum.Texture,
		3 => PublishPopup.PublishTypeEnum.Sound,
		4 => PublishPopup.PublishTypeEnum.Mesh,
		5 => PublishPopup.PublishTypeEnum.Animation,
		6 => PublishPopup.PublishTypeEnum.Plugin,
		_ => null,
	};

	private void ResetAndRefresh()
	{
		_cursors.Clear();
		_cursors.Add(null);
		_page = 1;
		Refresh();
	}

	private void Previous()
	{
		if (_page <= 1) return;
		_cursors.RemoveAt(_cursors.Count - 1);
		_page--;
		Refresh();
	}

	private void Next()
	{
		if (string.IsNullOrEmpty(_nextCursor)) return;
		_cursors.Add(_nextCursor);
		_page++;
		Refresh();
	}

	private async void Refresh()
	{
		int generation = ++_generation;
		_status.Text = "Loading…";
		_previous.Disabled = true;
		_next.Disabled = true;
		try
		{
			string? creatorType = null;
			string? creatorId = null;
			if (_scope.Selected == 2)
			{
				APIPlaceInfo? worldInfo = World.Current?.WorldInfo;
				if (worldInfo.HasValue)
				{
					creatorType = worldInfo.Value.Creator.Type.ToUpperInvariant();
					creatorId = worldInfo.Value.Creator.Id.ToString();
				}
				else
				{
					throw new InvalidOperationException("World ownership information is still loading.");
				}
			}
			CreatorAssetPage page = await CreatorAPI.GetCreatorAssetPage(
				SelectedType(), _cursors[^1], _search.Text,
				creatorType: creatorType, creatorId: creatorId,
				scope: _scope.Selected == 1 ? "OWNED" : "CREATED", limit: 30);
			if (generation != _generation || !GodotObject.IsInstanceValid(this)) return;
			foreach (Node child in _grid.GetChildren()) child.QueueFree();
			foreach (CreatorAssetItem item in page.Items) AddCard(item);
			_nextCursor = page.NextCursor;
			_previous.Disabled = _page <= 1;
			_next.Disabled = string.IsNullOrEmpty(_nextCursor);
			_status.Text = page.Items.Length == 0 ? "No matching assets" : $"Page {_page}  •  {page.Items.Length} assets";
		}
		catch (Exception ex)
		{
			_status.Text = "Could not load assets";
			CreatorService.Interface.StatusBar?.SetStatus("Asset Manager: " + ex.Message);
		}
	}

	private void AddCard(CreatorAssetItem item)
	{
		AssetManagerCard card = CardScene.Instantiate<AssetManagerCard>();
		_grid.AddChild(card);
		card.Setup(item, Insert, ShowAssetMenu);
	}

	private void PublishSelection()
	{
		Instance? selected = World.Current?.CreatorContext.Selections.SelectedInstances.Count > 0
			? World.Current.CreatorContext.Selections.SelectedInstances[0]
			: null;
		if (selected is Model or ServerScript) CreatorService.Interface.OpenPublish(selected);
		else CreatorService.Interface.PopupAlert("Select a model or server script to publish.", "Asset Manager");
	}

	private void ShowAssetMenu(CreatorAssetItem item)
	{
		PopupMenu menu = new();
		menu.AddItem("Insert", 0);
		menu.AddItem("Open on BrickVerse", 1);
		if (item.Type.Equals("PREFAB", StringComparison.OrdinalIgnoreCase)) menu.AddItem("Update from selected model", 2);
		if (item.Type.Equals("PLUGIN", StringComparison.OrdinalIgnoreCase)) menu.AddItem("Update from selected server script", 3);
		menu.IdPressed += id =>
		{
			if (id == 0) Insert(item);
			else if (id == 1) OS.ShellOpen(Globals.MainEndpoint.PathJoin("/assets/" + item.Id));
			else if (id == 2)
			{
				Model? model = World.Current?.CreatorContext.Selections.SelectedInstances.Count > 0
					? World.Current.CreatorContext.Selections.SelectedInstances[0] as Model : null;
				if (model == null) CreatorService.Interface.PopupAlert("Select the model that should replace this prefab.", "Asset Manager");
				else _ = PublishManager.PublishModel(model, item.Id);
			}
			else if (id == 3)
			{
				ServerScript? script = World.Current?.CreatorContext.Selections.SelectedInstances.Count > 0
					? World.Current.CreatorContext.Selections.SelectedInstances[0] as ServerScript : null;
				if (script == null) CreatorService.Interface.PopupAlert("Select the server script that should replace this plugin.", "Asset Manager");
				else _ = PublishManager.PublishAddon(script, item.Id);
			}
		};
		AddChild(menu);
		menu.PopupOnParent(new Rect2I((Vector2I)GetGlobalMousePosition(), Vector2I.Zero));
		menu.PopupHide += menu.QueueFree;
	}

	private async void Insert(CreatorAssetItem item)
	{
		World? root = World.Current;
		if (root == null) return;
		string name = item.Name.ToPascalCase().RemoveSymbols();
		try
		{
			CreatorService.Interface.StatusBar?.SetStatus($"Inserting {item.Name}…");
			switch (item.Type.ToUpperInvariant())
			{
				case "PREFAB":
				Instance? prefab = await root.Insert.CreatorImportWebModel(item.Id.ToString(), name);
				if (prefab != null)
				{
					prefab.Parent = root.Environment;
					if (prefab is Dynamic dynamic) dynamic.Position = root.CreatorContext.Freelook.GetPlacementPosition();
					root.CreatorContext.Selections.SelectOnly(prefab);
					root.LinkedSession?.RescanFolder();
					CreatorService.Interface.StatusBar?.SetStatus($"Inserted {item.Name}");
				}
				break;
				case "TEXTURE":
				Image3D image = root.New<Image3D>(root.Environment); image.Name = name;
				BVImageAsset imageAsset = root.New<BVImageAsset>(); imageAsset.ImageID = item.Id.ToString(); image.Image = imageAsset;
				break;
				case "SOUND":
				Sound sound = root.New<Sound>(root.Environment); sound.Name = name;
				BVAudioAsset audioAsset = root.New<BVAudioAsset>(); audioAsset.AudioID = item.Id.ToString(); sound.Audio = audioAsset;
				break;
				case "MESH":
				Mesh mesh = root.New<Mesh>(root.Environment); mesh.Name = name;
				BVMeshAsset meshAsset = root.New<BVMeshAsset>(); meshAsset.AssetID = item.Id.ToString(); mesh.Asset = meshAsset;
				mesh.Position = root.CreatorContext.Freelook.GetPlacementPosition();
				root.CreatorContext.Selections.SelectOnly(mesh);
					break;
				case "FONT":
					BVFontAsset font = root.New<BVFontAsset>();
					font.FontID = item.Id.ToString();
					UILabel label = root.New<UILabel>(root.Environment); label.Name = name; label.Text = name; label.FontAsset = font;
					root.CreatorContext.Selections.SelectOnly(label);
					break;
				default:
				OS.ShellOpen(Globals.MainEndpoint.PathJoin("/assets/" + item.Id));
				break;
			}
		}
		catch (Exception error)
		{
			BV.PrintErr($"Failed to insert Asset Manager item {item.Id}: ", error);
			CreatorService.Interface.PopupAlert(error.Message, "Could not insert asset");
		}
	}
}

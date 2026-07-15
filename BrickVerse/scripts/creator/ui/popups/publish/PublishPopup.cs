// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Humanizer;
using BrickVerse.Creator.Managers;
using BrickVerse.Creator.UI.Components;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;

namespace BrickVerse.Creator.UI.Popups;

public partial class PublishPopup : PopupWindowBase
{
	private const string PublishPlaceItemPopup = "res://scenes/creator/popups/publish/components/publish_place_item.tscn";
	[Export] private Control _listContainer = null!;
	[Export] private Control _itemInfoView = null!;
	[Export] private Label _itemNameLabel = null!;
	[Export] private Label _itemLastUpdatedLabel = null!;
	[Export] private Label _itemCreatedAtLabel = null!;
	[Export] private TextureRect _itemIconRect = null!;
	[Export] private Control _loadingView = null!;
	[Export] private Button _newButton = null!;
	[Export] private Button _cancelButton = null!;
	[Export] private Button _publishButton = null!;

	private ButtonGroup _itemItemGroup = new();
	private long _targetID = 0;

	private long? _universeId = 0;
	private long? _worldId = 0;

	public PublishTypeEnum PublishType;
	public Instance Target = null!;

	public override void _Ready()
	{
		base._Ready();

		PublishType = (Target is World) ? PublishTypeEnum.Project : (Target is Model) ? PublishTypeEnum.Model : PublishTypeEnum.Addon;
		Title = "Publish " + PublishType.ToString();

		_itemInfoView.Visible = false;
		_publishButton.Disabled = true;
		_itemItemGroup.Pressed += OnPlaceItemPressed;
		_newButton.Pressed += OnCreateNew;
		_publishButton.Pressed += OnPublish;
		_cancelButton.Pressed += QueueFree;

		if (PublishType != PublishTypeEnum.Addon)
		{
			ListPublishedWorlds();
		}
	}

	private void OnPublish()
	{
		Publish(_targetID, _worldId, _universeId);
	}

	private void OnPlaceItemPressed(BaseButton button)
	{
		if (button is PublishPlaceItemUI item)
		{
			ShowPlaceInfo(item.Target);
		}
	}

	private void ShowPlaceInfo(CreatorPlaceItem item)
	{
		_itemInfoView.Visible = true;
		_publishButton.Disabled = false;
		_targetID = item.Id;
		_universeId = item.UniverseId;
		_worldId = item.WorldId;
		_itemNameLabel.Text = item.Name;
		_itemCreatedAtLabel.Text = item.CreatedAt.ToLongDateString();
		_itemLastUpdatedLabel.Text = item.UpdatedAt.Humanize();

		WebAssetLoader.Singleton.GetResource(new() { URL = item.IconUrl }, r =>
		{
			_itemIconRect.Texture = (Texture2D)r;
		});
	}

	private async void ListPublishedWorlds()
	{
		_loadingView.Visible = true;
		_publishButton.Disabled = true;
		_itemInfoView.Visible = false;

		foreach (Node child in _listContainer.GetChildren())
		{
			child.QueueFree();
		}

		CreatorPlaceItem[] items;

		try
		{
			items = await CreatorAPI.GetPublishedWorlds();
		}
		catch (System.Exception ex)
		{
			BV.PrintErr($"Failed to load published worlds: {ex.Message}");
			_loadingView.Visible = false;
			return;
		}

		_loadingView.Visible = false;

		if (items.Length == 0)
		{
			_itemInfoView.Visible = false;
			return;
		}

		foreach (CreatorPlaceItem item in items)
		{
			PublishPlaceItemUI card = Globals.CreateInstanceFromScene<PublishPlaceItemUI>(PublishPlaceItemPopup);
			card.Target = item;
			card.ButtonGroup = _itemItemGroup;
			_listContainer.AddChild(card);
		}

		PublishPlaceItemUI firstCard = (PublishPlaceItemUI)_listContainer.GetChild(0);
		firstCard.ButtonPressed = true;
		ShowPlaceInfo(firstCard.Target);
	}

	private void OnCreateNew()
	{
		Publish();
	}

	private async void Publish(long id = 0, long? worldId = null, long? universeId = null)
	{
		QueueFree();
		if (Target is ServerScript script)
		{
			//await PublishManager.PublishAddon(script, id);
		}
		else
		{
			await PublishManager.PublishModel(Target, id);
		}
	}

	public enum PublishTypeEnum
	{
		Project,
		Model,
		Addon
	}
}

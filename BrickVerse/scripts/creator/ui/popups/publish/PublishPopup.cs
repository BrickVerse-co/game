// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Humanizer;
using BrickVerse.Creator.Managers;
using BrickVerse.Creator.UI.Components;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Creator.Utils;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using System;

namespace BrickVerse.Creator.UI.Popups;

public partial class PublishPopup : PopupWindowBase
{
	private const string PublishItemPopup = "res://scenes/creator/popups/publish/components/publish_item_card.tscn";
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
	public PublishTypeEnum PublishType;
	public Instance Target = null!;

	public override void _Ready()
	{
		base._Ready();

		PublishType = (Target is Model) ? PublishTypeEnum.Prefab : PublishTypeEnum.Plugin;
		Title = "Publish " + PublishType.ToString();

		_itemInfoView.Visible = false;
		_publishButton.Disabled = true;
		_itemItemGroup.Pressed += OnPlaceItemPressed;
		_newButton.Pressed += OnCreateNew;
		_publishButton.Pressed += OnPublish;
		_cancelButton.Pressed += QueueFree;

		// Fetch list of published items based on the type of the target
		FetchPublishedItems();
	}

	private void OnPublish()
	{
		Publish(_targetID);
	}

	private void OnCreateNew()
	{
		Publish();
	}

	private void OnPlaceItemPressed(BaseButton button)
	{
		if (button is PublishItemUI item)
		{
			ShowAssetInfo(item.Target);
		}
	}

	private void ShowAssetInfo(CreatorAssetItem item)
	{
		_itemInfoView.Visible = true;
		_publishButton.Disabled = false;
		_targetID = item.Id;
		_itemNameLabel.Text = item.Name;
		_itemCreatedAtLabel.Text = item.CreatedAt.ToLongDateString();
		_itemLastUpdatedLabel.Text = item.UpdatedAt.Humanize();

		if (!string.IsNullOrEmpty(item.IconUrl))
		{
			WebAssetLoader.Singleton.GetResource(new() { URL = item.IconUrl }, r =>
			{
				_itemIconRect.Texture = (Texture2D)r;
			});
		}
		else
		{
			_itemIconRect.Texture = null;
		}
	}

	private async void FetchPublishedItems()
	{
		_loadingView.Visible = true;
		_publishButton.Disabled = true;
		_itemInfoView.Visible = false;

		// Remove all children from the list container
		foreach (Node child in _listContainer.GetChildren())
		{
			child.QueueFree();
		}

		// Fetch list of published items based on the type of the target
		// from the API

		CreatorAssetItem[] items;

		try
		{
			items = await CreatorAPI.GetCreatorAssets(PublishType);
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Failed to load published assets: {ex.Message}");
			CreatorService.Interface.PopupAlert($"Failed to load published assets: {ex.Message}");
			_loadingView.Visible = false;
			return;
		}

		_loadingView.Visible = false;

		if (items.Length == 0)
		{
			_itemInfoView.Visible = false;
			return;
		}

		foreach (CreatorAssetItem item in items)
		{
			PublishItemUI card = Globals.CreateInstanceFromScene<PublishItemUI>(PublishItemPopup);
			card.Target = item;
			card.ButtonGroup = _itemItemGroup;
			_listContainer.AddChild(card);
		}
	}

	private async void Publish(long id = 0)
	{
		QueueFree();

		if (Target is ServerScript script)
		{
			await PublishManager.PublishAddon(script, id);
		}
		else if (Target is Model model)
		{
			await PublishManager.PublishModel(model, id);
		}
		else
		{
			throw new Exception("PublishPopup: Target is not a supported publish type.");
		}
	}

	public enum PublishTypeEnum
	{
		Prefab,
		Plugin
	}
}

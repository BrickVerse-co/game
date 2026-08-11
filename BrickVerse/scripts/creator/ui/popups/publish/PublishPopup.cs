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
using System.Collections.Generic;

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
	private LineEdit _searchInput = null!;
	private OptionButton _sortInput = null!;
	private OptionButton _ownerInput = null!;
	private OptionButton _privacyInput = null!;
	private OptionButton _moderationInput = null!;
	private Button _previousButton = null!;
	private Button _nextButton = null!;
	private Label _pageLabel = null!;
	private readonly List<string?> _cursorHistory = [null];
	private readonly Dictionary<int, (string Type, string Id)> _owners = [];
	private string? _nextCursor;
	private int _page = 1;
	private int _fetchGeneration;
	private Timer _searchDebounce = null!;
	public PublishTypeEnum PublishType;
	public Instance Target = null!;

	public override void _Ready()
	{
		base._Ready();

		PublishType = (Target is Model) ? PublishTypeEnum.Prefab : PublishTypeEnum.Plugin;
		Title = "Publish " + PublishType.ToString();
		string typeName = PublishType.ToString();
		GetNode<Label>("TitleBar/Margin/Row/Title").Text = $"Publish {typeName}";
		_newButton.Text = $"+  Create New {typeName}";
		BuildFilters();

		_itemInfoView.Visible = false;
		_publishButton.Disabled = true;
		_itemItemGroup.Pressed += OnPlaceItemPressed;
		_newButton.Pressed += OnCreateNew;
		_publishButton.Pressed += OnPublish;
		_cancelButton.Pressed += QueueFree;

		// Fetch list of published items based on the type of the target
		FetchPublishedItems();
		LoadOwners();
	}

	private void BuildFilters()
	{
		VBoxContainer filters = new() { Name = "AssetFilters" };
		filters.AddThemeConstantOverride("separation", 8);
		_searchInput = new LineEdit { PlaceholderText = $"Search {PublishType.ToString().ToLowerInvariant()}s...", ClearButtonEnabled = true };
		filters.AddChild(_searchInput);

		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 7);
		filters.AddChild(row);
		_ownerInput = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_ownerInput.AddItem("My assets", 0);
		_owners[0] = ("USER", CreatorAPI.UserID);
		row.AddChild(_ownerInput);
		_sortInput = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_sortInput.AddItem("Recently updated", 0);
		_sortInput.AddItem("Newest", 1);
		_sortInput.AddItem("Oldest", 2);
		_sortInput.AddItem("Name A–Z", 3);
		row.AddChild(_sortInput);
		HBoxContainer filterRow = new();
		filterRow.AddThemeConstantOverride("separation", 7);
		filters.AddChild(filterRow);
		_privacyInput = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_privacyInput.AddItem("Any visibility", 0);
		_privacyInput.AddItem("Public", 1);
		_privacyInput.AddItem("Owner only", 2);
		filterRow.AddChild(_privacyInput);
		_moderationInput = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_moderationInput.AddItem("Any review status", 0);
		_moderationInput.AddItem("Approved", 1);
		_moderationInput.AddItem("Pending", 2);
		_moderationInput.AddItem("Rejected", 3);
		filterRow.AddChild(_moderationInput);

		HBoxContainer paging = new() { Alignment = BoxContainer.AlignmentMode.Center };
		filters.AddChild(paging);
		_previousButton = new Button { Text = "‹ Previous", Disabled = true };
		_pageLabel = new Label { Text = "Page 1" };
		_nextButton = new Button { Text = "Next ›", Disabled = true };
		paging.AddChild(_previousButton);
		paging.AddChild(_pageLabel);
		paging.AddChild(_nextButton);

		Control parent = _newButton.GetParent<Control>();
		parent.AddChild(filters);
		parent.MoveChild(filters, 0);

		_searchDebounce = new Timer { OneShot = true, WaitTime = 0.3 };
		AddChild(_searchDebounce);
		_searchInput.TextChanged += _ => _searchDebounce.Start();
		_searchDebounce.Timeout += ResetAndFetch;
		_sortInput.ItemSelected += _ => ResetAndFetch();
		_ownerInput.ItemSelected += _ => ResetAndFetch();
		_privacyInput.ItemSelected += _ => ResetAndFetch();
		_moderationInput.ItemSelected += _ => ResetAndFetch();
		_previousButton.Pressed += PreviousPage;
		_nextButton.Pressed += NextPage;
	}

	private async void LoadOwners()
	{
		try
		{
			CreatorGuildItem[] guilds = await CreatorAPI.GetUserGuilds();
			foreach (CreatorGuildItem guild in guilds)
			{
				int index = _ownerInput.ItemCount;
				_ownerInput.AddItem(guild.Name, index);
				_owners[index] = ("GUILD", guild.Id.ToString());
			}
		}
		catch (Exception ex)
		{
			BV.PrintWarn("Could not load guild asset filters: ", ex.Message);
		}
	}

	private void ResetAndFetch()
	{
		_cursorHistory.Clear();
		_cursorHistory.Add(null);
		_page = 1;
		FetchPublishedItems();
	}

	private void NextPage()
	{
		if (string.IsNullOrEmpty(_nextCursor)) return;
		_cursorHistory.Add(_nextCursor);
		_page++;
		FetchPublishedItems();
	}

	private void PreviousPage()
	{
		if (_page <= 1) return;
		_cursorHistory.RemoveAt(_cursorHistory.Count - 1);
		_page--;
		FetchPublishedItems();
	}

	private void OnPublish()
	{
		Publish(_targetID);
	}

	private void OnCreateNew()
	{
		ShowCreateDialog();
	}

	private void ShowCreateDialog()
	{
		ConfirmationDialog dialog = new()
		{
			Title = $"Create New {PublishType}",
			DialogText = "Choose the public details for this asset.",
			OkButtonText = "Create and publish",
			InitialPosition = Window.WindowInitialPosition.CenterMainWindowScreen,
			Size = new Vector2I(520, 310),
		};
		VBoxContainer fields = new();
		LineEdit name = new() { Text = Target.Name, PlaceholderText = $"{PublishType} name", MaxLength = 100 };
		TextEdit description = new() { PlaceholderText = "Optional description", CustomMinimumSize = new Vector2(0, 100), WrapMode = TextEdit.LineWrappingMode.Boundary };
		fields.AddChild(new Label { Text = "Name" });
		fields.AddChild(name);
		fields.AddChild(new Label { Text = "Description (optional)" });
		fields.AddChild(description);
		dialog.AddChild(fields);
		AddChild(dialog);
		dialog.Confirmed += () =>
		{
			string finalName = string.IsNullOrWhiteSpace(name.Text) ? Target.Name : name.Text.Trim();
			Publish(0, finalName, description.Text.Trim());
		};
		dialog.Canceled += dialog.QueueFree;
		dialog.CloseRequested += dialog.QueueFree;
		dialog.PopupCentered();
		name.GrabFocus();
		name.SelectAll();
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
		int generation = ++_fetchGeneration;
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

		CreatorAssetPage result;

		try
		{
			(string ownerType, string ownerId) = _owners.GetValueOrDefault(_ownerInput.Selected, ("USER", CreatorAPI.UserID));
			string[] sorts = ["UPDATED_DESC", "CREATED_DESC", "CREATED_ASC", "NAME_ASC"];
			string? privacy = _privacyInput.Selected switch { 1 => "Public", 2 => "Ownership", _ => null };
			string? moderation = _moderationInput.Selected switch { 1 => "APPROVED", 2 => "PENDING", 3 => "REJECTED", _ => null };
			result = await CreatorAPI.GetCreatorAssetPage(
				PublishType,
				_cursorHistory[^1],
				_searchInput.Text,
				sorts[Math.Clamp(_sortInput.Selected, 0, sorts.Length - 1)],
				ownerType,
				ownerId,
				privacy,
				moderation,
				"CREATED",
				20
			);
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Failed to load published assets: {ex.Message}");
			CreatorService.Interface.PopupAlert($"Failed to load published assets: {ex.Message}");
			_loadingView.Visible = false;
			QueueFree();
			return;
		}
		if (generation != _fetchGeneration || !GodotObject.IsInstanceValid(this)) return;

		_loadingView.Visible = false;
		CreatorAssetItem[] items = result.Items;
		_nextCursor = result.NextCursor;
		_previousButton.Disabled = _page <= 1;
		_nextButton.Disabled = string.IsNullOrEmpty(_nextCursor);
		_pageLabel.Text = $"Page {_page}";

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

	private async void Publish(long id = 0, string? name = null, string? description = null)
	{
		QueueFree();

		if (Target is ServerScript script)
		{
			await PublishManager.PublishAddon(script, id, name, description);
		}
		else if (Target is Model model)
		{
			await PublishManager.PublishModel(model, id, name, description);
		}
		else
		{
			throw new Exception("PublishPopup: Target is not a supported publish type.");
		}
	}

	public enum PublishTypeEnum
	{
		Prefab,
		Plugin,
		Animation,
		Texture,
		Sound,
		Mesh,
	}
}

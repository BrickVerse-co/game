// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrickVerse.Creator;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Formats;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

public partial class PublishAsPlaceModal : PopupWindowBase
{
	private sealed class OwnerOption
	{
		public string Id { get; init; } = string.Empty;
		public string Name { get; init; } = string.Empty;
		public string Type { get; init; } = "user";
	}

	private sealed class UniverseEntry
	{
		public long UniverseId { get; init; }
		public string Name { get; init; } = "Untitled universe";
		public string ThumbnailUrl { get; init; } = string.Empty;
		public List<CreatorPlaceItem> Worlds { get; init; } = [];
	}

	[Export]
	private Label _headerTitle = null!;

	[Export]
	private Label _leadTitle = null!;

	[Export]
	private Label _leadBody = null!;

	[Export]
	private Label _currentProjectLabel = null!;

	[Export]
	private OptionButton _ownerDropdown = null!;

	[Export]
	private LineEdit _searchInput = null!;

	[Export]
	private Button _backButton = null!;

	[Export]
	private Label _sectionTitle = null!;

	[Export]
	private Label _resultsLabel = null!;

	[Export]
	private ScrollContainer _universeScroll = null!;

	[Export]
	private GridContainer _universeGrid = null!;

	[Export]
	private ScrollContainer _worldScroll = null!;

	[Export]
	private VBoxContainer _worldList = null!;

	[Export]
	private Label _loadingLabel = null!;

	[Export]
	private Label _emptyLabel = null!;

	[Export]
	private TextureRect _detailPreview = null!;

	[Export]
	private Label _detailTitle = null!;

	[Export]
	private Label _detailMeta = null!;

	[Export]
	private Label _detailBody = null!;

	[Export]
	private Label _detailBadge = null!;

	[Export]
	private Button _publishButton = null!;

	[Export]
	private Button _cancelButton = null!;

	[Export]
	private Button _closeButton = null!;

	private readonly List<OwnerOption> _owners = [];
	private readonly List<CreatorPlaceItem> _publishedWorlds = [];
	private readonly List<UniverseEntry> _universes = [];
	private readonly List<Button> _targetButtons = [];
	private readonly ButtonGroup _targetButtonGroup = new();

	private bool _isReady;
	private bool _openRequested;
	private bool _isBusy;
	private bool _isLoading;
	private World? _world;
	private World? _pendingWorld;
	private UniverseEntry? _selectedUniverse;
	private CreatorPlaceItem? _selectedTarget;
	private bool _useNewWorld = true;
	private int _detailPreviewRequestId;
	private int _loadRequestId;

	public override void _Ready()
	{
		base._Ready();
		ResolveNodeReferences();
		_isReady = true;

		_closeButton.Pressed += CloseWindow;
		_cancelButton.Pressed += CloseWindow;
		_publishButton.Pressed += Submit;
		_backButton.Pressed += ShowUniverseGrid;
		_ownerDropdown.ItemSelected += OnOwnerSelected;
		_searchInput.TextChanged += _ => RefreshUniverseGrid();

		if (_openRequested)
			BeginOpen();
	}

	public void Open(World world)
	{
		if (world == null)
		{
			BV.PrintErr("Cannot open PublishAsPlaceModal: world is null.");
			return;
		}

		_pendingWorld = world;
		_openRequested = true;

		if (_isReady)
			BeginOpen();
	}

	private async void BeginOpen()
	{
		if (_pendingWorld == null)
			return;

		_openRequested = false;
		_world = _pendingWorld;
		_selectedUniverse = null;
		_selectedTarget = null;
		_useNewWorld = true;
		_detailPreviewRequestId = 0;
		_searchInput.Text = string.Empty;

		_headerTitle.Text = "Publish As";
		_leadTitle.Text = "Choose where to publish";
		_leadBody.Text =
			"Select one of your universes, then choose the world that should be overwritten.";
		_currentProjectLabel.Text = FormatCurrentProjectLabel(_world);

		SetSelection(null, true);
		SetBusy(true);

		try
		{
			await LoadOwners();
			await LoadSelectedOwnerWorlds();
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Failed to open publish-as browser: {ex}");
			ShowLoadError(ex.Message);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private async System.Threading.Tasks.Task LoadOwners()
	{
		_owners.Clear();
		_ownerDropdown.Clear();

		_owners.Add(
			new OwnerOption
			{
				Id = CreatorAPI.UserID,
				Name = "My Universes",
				Type = "user",
			}
		);

		CreatorGuildItem[] guilds = await CreatorAPI.GetUserGuilds(limitToEditable: true);
		foreach (CreatorGuildItem guild in guilds)
		{
			if (string.IsNullOrWhiteSpace(guild.Id))
				continue;

			_owners.Add(
				new OwnerOption
				{
					Id = guild.Id,
					Name = guild.Name,
					Type = "guild",
				}
			);
		}

		foreach (OwnerOption owner in _owners)
			_ownerDropdown.AddItem(owner.Name);

		_ownerDropdown.Select(0);
	}

	private async void OnOwnerSelected(long index)
	{
		if (_isBusy || index < 0 || index >= _owners.Count)
			return;

		await LoadSelectedOwnerWorlds();
	}

	private async System.Threading.Tasks.Task LoadSelectedOwnerWorlds()
	{
		int ownerIndex = _ownerDropdown.Selected;
		if (ownerIndex < 0 || ownerIndex >= _owners.Count)
			return;

		OwnerOption owner = _owners[ownerIndex];
		int requestId = ++_loadRequestId;

		_isLoading = true;
		SetBusy(true);
		_loadingLabel.Visible = true;
		_emptyLabel.Visible = false;
		_resultsLabel.Text = "Loading...";
		ClearContainer(_universeGrid);
		ClearContainer(_worldList);

		try
		{
			CreatorPlaceItem[] items =
				owner.Type == "guild"
					? await CreatorAPI.GetGuildWorlds(owner.Id)
					: await CreatorAPI.GetUserWorlds(owner.Id);

			if (requestId != _loadRequestId)
				return;

			_publishedWorlds.Clear();
			_publishedWorlds.AddRange(items);
			BuildUniverseIndex();
			ShowUniverseGrid();
		}
		catch (Exception ex)
		{
			if (requestId != _loadRequestId)
				return;

			BV.PrintErr($"Failed to load worlds for {owner.Type} {owner.Id}: {ex}");
			ShowLoadError(ex.Message);
		}
		finally
		{
			if (requestId == _loadRequestId)
			{
				_isLoading = false;
				_loadingLabel.Visible = false;
				SetBusy(false);
			}
		}
	}

	private void BuildUniverseIndex()
	{
		_universes.Clear();

		foreach (
			IGrouping<long, CreatorPlaceItem> group in _publishedWorlds
				.Where(item => (item.UniverseId ?? 0) > 0)
				.GroupBy(item => item.UniverseId!.Value)
				.OrderBy(
					group => group.FirstOrDefault().Name ?? string.Empty,
					StringComparer.OrdinalIgnoreCase
				)
		)
		{
			List<CreatorPlaceItem> worlds =
			[
				.. group.OrderBy(
					item => item.Name ?? string.Empty,
					StringComparer.OrdinalIgnoreCase
				),
			];
			CreatorPlaceItem primary = worlds[0];

			_universes.Add(
				new UniverseEntry
				{
					UniverseId = group.Key,
					// The current endpoint does not return a separate universe name.
					// The first/root world's name is therefore used as the universe display name.
					Name = string.IsNullOrWhiteSpace(primary.Name)
						? $"Universe {group.Key}"
						: primary.Name,
					ThumbnailUrl = primary.IconUrl ?? string.Empty,
					Worlds = worlds,
				}
			);
		}
	}

	private void ShowUniverseGrid()
	{
		_selectedUniverse = null;
		_backButton.Visible = false;
		_searchInput.Visible = true;
		_universeScroll.Visible = true;
		_worldScroll.Visible = false;
		_sectionTitle.Text = "Universes";
		RefreshUniverseGrid();
	}

	private void RefreshUniverseGrid()
	{
		if (_universeGrid == null)
			return;

		ClearContainer(_universeGrid);
		_targetButtons.Clear();

		string search = _searchInput.Text.Trim();
		List<UniverseEntry> visibleUniverses = _universes
			.Where(universe =>
				string.IsNullOrWhiteSpace(search)
				|| universe.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
			)
			.ToList();

		Button createCard = CreateUniverseCard(null, true);
		_universeGrid.AddChild(createCard);
		_targetButtons.Add(createCard);

		foreach (UniverseEntry universe in visibleUniverses)
		{
			Button card = CreateUniverseCard(universe, false);
			_universeGrid.AddChild(card);
		}

		_resultsLabel.Text =
			$"{visibleUniverses.Count} {(visibleUniverses.Count == 1 ? "universe" : "universes")}";
		_emptyLabel.Text = "No universes match your search.";
		_emptyLabel.Visible =
			!_isLoading && visibleUniverses.Count == 0 && !string.IsNullOrWhiteSpace(search);
		ApplySelectionState();
	}

	private Button CreateUniverseCard(UniverseEntry? universe, bool createNew)
	{
		Button card = new()
		{
			ToggleMode = createNew,
			ButtonGroup = createNew ? _targetButtonGroup : null,
			CustomMinimumSize = new Vector2(210, 190),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			FocusMode = Control.FocusModeEnum.All,
			ClipContents = true,
		};

		card.AddThemeStyleboxOverride(
			"normal",
			CreateCardStyle(new Color("101722"), new Color("26364b"), 1)
		);
		card.AddThemeStyleboxOverride(
			"hover",
			CreateCardStyle(new Color("141f2e"), new Color("0187f8"), 1)
		);
		card.AddThemeStyleboxOverride(
			"pressed",
			CreateCardStyle(new Color("11243a"), new Color("0187f8"), 2)
		);
		card.AddThemeStyleboxOverride(
			"focus",
			CreateCardStyle(new Color("101722"), new Color("0187f8"), 2)
		);

		VBoxContainer column = new();
		column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		column.MouseFilter = Control.MouseFilterEnum.Ignore;
		column.AddThemeConstantOverride("separation", 8);
		card.AddChild(column);

		TextureRect preview = new()
		{
			CustomMinimumSize = new Vector2(0, 118),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		column.AddChild(preview);

		MarginContainer textMargin = new();
		textMargin.AddThemeConstantOverride("margin_left", 12);
		textMargin.AddThemeConstantOverride("margin_right", 12);
		textMargin.AddThemeConstantOverride("margin_bottom", 10);
		textMargin.MouseFilter = Control.MouseFilterEnum.Ignore;
		column.AddChild(textMargin);

		VBoxContainer textColumn = new();
		textColumn.AddThemeConstantOverride("separation", 3);
		textColumn.MouseFilter = Control.MouseFilterEnum.Ignore;
		textMargin.AddChild(textColumn);

		Label title = CreateLabel(
			createNew ? "Create new universe" : universe!.Name,
			13,
			new Color("f1f6fc")
		);
		title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		title.MouseFilter = Control.MouseFilterEnum.Ignore;
		textColumn.AddChild(title);

		Label meta = CreateLabel(
			createNew
				? "Publish as a new experience"
				: $"{universe!.Worlds.Count} {(universe.Worlds.Count == 1 ? "world" : "worlds")}",
			10,
			new Color("96a6ba")
		);
		meta.MouseFilter = Control.MouseFilterEnum.Ignore;
		textColumn.AddChild(meta);

		if (createNew)
		{
			preview.Texture = GD.Load<Texture2D>(
				"res://assets/textures/creator/wizard/new-place/new_place.png"
			);
			card.Pressed += () => SetSelection(null, true);
		}
		else
		{
			preview.Texture = GD.Load<Texture2D>("res://assets/textures/ui-icons/replace.svg");
			LoadThumbnail(preview, universe!.ThumbnailUrl);
			card.Pressed += () => ShowWorldList(universe);
		}

		return card;
	}

	private void ShowWorldList(UniverseEntry universe)
	{
		_selectedUniverse = universe;
		_searchInput.Visible = false;
		_backButton.Visible = true;
		_universeScroll.Visible = false;
		_worldScroll.Visible = true;
		_sectionTitle.Text = universe.Name;
		_resultsLabel.Text =
			$"{universe.Worlds.Count} {(universe.Worlds.Count == 1 ? "world" : "worlds")}";
		_emptyLabel.Visible = universe.Worlds.Count == 0;
		_emptyLabel.Text = "This universe has no worlds.";

		ClearContainer(_worldList);
		_targetButtons.Clear();

		foreach (CreatorPlaceItem world in universe.Worlds)
		{
			Button row = CreateWorldRow(world);
			_worldList.AddChild(row);
			_targetButtons.Add(row);
		}

		ApplySelectionState();
	}

	private Button CreateWorldRow(CreatorPlaceItem target)
	{
		Button row = new()
		{
			ToggleMode = true,
			ButtonGroup = _targetButtonGroup,
			CustomMinimumSize = new Vector2(0, 92),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			FocusMode = Control.FocusModeEnum.All,
		};
		row.SetMeta("world_id", target.WorldId ?? target.Id);
		row.AddThemeStyleboxOverride(
			"normal",
			CreateCardStyle(new Color("101722"), new Color("26364b"), 1)
		);
		row.AddThemeStyleboxOverride(
			"hover",
			CreateCardStyle(new Color("141f2e"), new Color("0187f8"), 1)
		);
		row.AddThemeStyleboxOverride(
			"pressed",
			CreateCardStyle(new Color("11243a"), new Color("0187f8"), 2)
		);
		row.AddThemeStyleboxOverride(
			"focus",
			CreateCardStyle(new Color("101722"), new Color("0187f8"), 2)
		);

		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		SetMargins(margin, 10);
		margin.MouseFilter = Control.MouseFilterEnum.Ignore;
		row.AddChild(margin);

		HBoxContainer content = new();
		content.AddThemeConstantOverride("separation", 12);
		content.MouseFilter = Control.MouseFilterEnum.Ignore;
		margin.AddChild(content);

		TextureRect preview = new()
		{
			CustomMinimumSize = new Vector2(116, 68),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Texture = GD.Load<Texture2D>("res://assets/textures/ui-icons/replace.svg"),
		};
		content.AddChild(preview);
		LoadThumbnail(preview, target.IconUrl ?? string.Empty);

		VBoxContainer labels = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		labels.AddThemeConstantOverride("separation", 5);
		content.AddChild(labels);

		Label title = CreateLabel(
			string.IsNullOrWhiteSpace(target.Name) ? "Untitled world" : target.Name,
			13,
			new Color("f1f6fc")
		);
		title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		title.MouseFilter = Control.MouseFilterEnum.Ignore;
		labels.AddChild(title);

		Label meta = CreateLabel(FormatTargetMeta(target), 10, new Color("96a6ba"));
		meta.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		meta.MouseFilter = Control.MouseFilterEnum.Ignore;
		labels.AddChild(meta);

		Label description = CreateLabel(
			string.IsNullOrWhiteSpace(target.Description) ? "No description" : target.Description,
			10,
			new Color("b6c2d1")
		);
		description.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		description.MouseFilter = Control.MouseFilterEnum.Ignore;
		labels.AddChild(description);

		row.Pressed += () => SetSelection(target, false);
		return row;
	}

	private void SetSelection(CreatorPlaceItem? target, bool newWorld)
	{
		_selectedTarget = target;
		_useNewWorld = newWorld;
		UpdateDetailPanel();
		SetPublishButtonLabel();
		ApplySelectionState();
	}

	private void ApplySelectionState()
	{
		foreach (Button button in _targetButtons)
		{
			if (!button.HasMeta("world_id"))
			{
				button.ButtonPressed = _useNewWorld;
				continue;
			}

			long worldId = button.GetMeta("world_id").AsInt64();
			long selectedWorldId = _selectedTarget?.WorldId ?? _selectedTarget?.Id ?? 0;
			button.ButtonPressed = !_useNewWorld && selectedWorldId == worldId;
		}
	}

	private void UpdateDetailPanel()
	{
		if (_useNewWorld || !_selectedTarget.HasValue)
		{
			_detailBadge.Text = "NEW EXPERIENCE";
			_detailTitle.Text = "Create a new universe";
			_detailMeta.Text = "The current project will be published as a new experience.";
			_detailBody.Text = "Use this when the project should not replace an existing world.";
			_detailPreview.Texture = GD.Load<Texture2D>(
				"res://assets/textures/creator/wizard/new-place/new_place.png"
			);
			return;
		}

		CreatorPlaceItem target = _selectedTarget.Value;
		_detailBadge.Text = "OVERWRITE WORLD";
		_detailTitle.Text = string.IsNullOrWhiteSpace(target.Name) ? "Untitled world" : target.Name;
		_detailMeta.Text = FormatTargetMeta(target);
		_detailBody.Text =
			"Publishing will replace this world's current project contents. This cannot be undone from this dialog.";
		_detailPreview.Texture = GD.Load<Texture2D>("res://assets/textures/ui-icons/replace.svg");
		LoadThumbnail(_detailPreview, target.IconUrl ?? string.Empty, ++_detailPreviewRequestId);
	}

	private void Submit()
	{
		if (_isBusy)
			return;

		if (_world == null)
		{
			ShowError("Failed to publish: world data is missing.");
			return;
		}

		if (!Directory.Exists(_world.LinkedSession.ProjectFolderPath))
		{
			ShowError("Failed to publish: project folder does not exist.");
			return;
		}

		SetBusy(true);
		PublishSelectionAsync();
	}

	private async void PublishSelectionAsync()
	{
		if (_world == null)
		{
			ShowPublishError("Failed to publish: world data is missing.");
			return;
		}

		CreatorPlaceItem? target = _useNewWorld ? null : _selectedTarget;
		OwnerOption owner = _owners[Math.Max(0, _ownerDropdown.Selected)];

		try
		{
			var loadOverlay = CreatorService.Interface.LoadOverlay;
			string projectPath = _world.LinkedSession.ProjectFolderPath;
			PackedFormat.ReadProjectMetadata(
				File.ReadAllText(projectPath.PathJoin(Globals.ProjectMetaFileName))
			);
			byte[] packed = await PackedFormat.PackProject(
				projectPath,
				loadOverlay?.CreateProgressReporter("Publishing world")
			);

			loadOverlay?.SetStatus("Uploading now...");

			CreatorPublishResponse publishRes = await CreatorAPI.UploadWorld(
				packed,
				target?.UniverseId ?? 0,
				target?.WorldId ?? target?.Id ?? 0,
				true,
				owner.Id,
				owner.Type
			);

			if (
				CreatorSettingsService.Instance.Get<bool>(
					CreatorSettingKeys.Creator.OpenWebAfterPublish
				)
			)
				OS.ShellOpen(publishRes.Link);

			_world.UniverseID = publishRes.UniverseId;
			_world.WorldID = publishRes.WorldId;
			CreatorService.Interface.StatusBar?.SetStatus(
				_useNewWorld ? "World published" : "World overwritten"
			);
			loadOverlay?.Hide();
			CloseWindow();
			CreatorService.Interface.PopupAlert(
				"World "
					+ (_useNewWorld ? "published" : "overwritten")
					+ " successfully! You can now share it with others using the link: "
					+ publishRes.Link
			);
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Failed to publish world: {ex}");
			CreatorService.Interface.PopupAlert(ex.Message);
			ShowPublishError("Failed to publish: " + ex.Message);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private void LoadThumbnail(TextureRect target, string url, int requestId = -1)
	{
		if (string.IsNullOrWhiteSpace(url))
			return;

		WebAssetLoader.Singleton.GetResource(
			new() { URL = url },
			result =>
			{
				if (requestId >= 0 && requestId != _detailPreviewRequestId)
					return;

				if (GodotObject.IsInstanceValid(target) && result is Texture2D texture)
					target.Texture = texture;
			}
		);
	}

	private void SetPublishButtonLabel()
	{
		_publishButton.Text = _useNewWorld ? "Create New Experience" : "Overwrite World";
	}

	private void SetBusy(bool busy)
	{
		_isBusy = busy;
		_publishButton.Disabled = busy;
		_cancelButton.Disabled = busy;
		_closeButton.Disabled = busy;
		_ownerDropdown.Disabled = busy;
		_searchInput.Editable = !busy;
		_backButton.Disabled = busy;

		foreach (Button button in _targetButtons)
			button.Disabled = busy;
	}

	private void ShowLoadError(string message)
	{
		_loadingLabel.Visible = false;
		_emptyLabel.Visible = true;
		_emptyLabel.Text = "Unable to load publish destinations.\n" + message;
		_resultsLabel.Text = "Failed to load";
	}

	private void ShowError(string message)
	{
		_detailBadge.Text = "ERROR";
		_detailBody.Text = message;
	}

	private void ShowPublishError(string message)
	{
		SetBusy(false);
		ShowError(message);
	}

	private void CloseWindow()
	{
		if (_isBusy)
			return;

		Hide();
		QueueFree();
	}

	private static void ClearContainer(Node container)
	{
		foreach (Node child in container.GetChildren())
		{
			container.RemoveChild(child);
			child.QueueFree();
		}
	}

	private static string FormatCurrentProjectLabel(World world)
	{
		string worldName = string.IsNullOrWhiteSpace(world.WorldName)
			? "Untitled world"
			: world.WorldName;
		string universeName = string.IsNullOrWhiteSpace(world.UniverseName)
			? "Untitled universe"
			: world.UniverseName;
		return $"Current project: {worldName} · {universeName}";
	}

	private static string FormatTargetMeta(CreatorPlaceItem item)
	{
		long worldId = item.WorldId ?? item.Id;
		long universeId = item.UniverseId ?? 0;
		return $"World #{worldId} · Universe #{universeId}";
	}

	private static void SetMargins(MarginContainer container, int margin)
	{
		container.AddThemeConstantOverride("margin_left", margin);
		container.AddThemeConstantOverride("margin_top", margin);
		container.AddThemeConstantOverride("margin_right", margin);
		container.AddThemeConstantOverride("margin_bottom", margin);
	}

	private static Label CreateLabel(string text, int fontSize, Color color)
	{
		Label label = new()
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Center,
		};
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	private static StyleBoxFlat CreateCardStyle(Color background, Color border, int width)
	{
		return new StyleBoxFlat
		{
			BgColor = background,
			BorderColor = border,
			BorderWidthLeft = width,
			BorderWidthTop = width,
			BorderWidthRight = width,
			BorderWidthBottom = width,
			CornerRadiusTopLeft = 9,
			CornerRadiusTopRight = 9,
			CornerRadiusBottomLeft = 9,
			CornerRadiusBottomRight = 9,
			ContentMarginLeft = 0,
			ContentMarginTop = 0,
			ContentMarginRight = 0,
			ContentMarginBottom = 0,
		};
	}

	private void ResolveNodeReferences()
	{
		_headerTitle ??= GetNode<Label>("TitleBar/Margin/Row/Title");
		_leadTitle ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/LeadGroup/LeadTitle");
		_leadBody ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/LeadGroup/LeadBody");
		_currentProjectLabel ??= GetNode<Label>(
			"Modal/ContentMargin/ContentRoot/LeadGroup/CurrentProject"
		);
		_ownerDropdown ??= GetNode<OptionButton>("Modal/ContentMargin/ContentRoot/Toolbar/Owner");
		_searchInput ??= GetNode<LineEdit>("Modal/ContentMargin/ContentRoot/Toolbar/Search");
		_backButton ??= GetNode<Button>(
			"Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/HeaderRow/Back"
		);
		_sectionTitle ??= GetNode<Label>(
			"Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/HeaderRow/Title"
		);
		_resultsLabel ??= GetNode<Label>(
			"Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/HeaderRow/Results"
		);
		_universeScroll ??= GetNode<ScrollContainer>(
			"Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/UniverseScroll"
		);
		_universeGrid ??= GetNode<GridContainer>(
			"Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/UniverseScroll/UniverseGrid"
		);
		_worldScroll ??= GetNode<ScrollContainer>(
			"Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/WorldScroll"
		);
		_worldList ??= GetNode<VBoxContainer>(
			"Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/WorldScroll/WorldList"
		);
		_loadingLabel ??= GetNode<Label>(
			"Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/Loading"
		);
		_emptyLabel ??= GetNode<Label>(
			"Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/Empty"
		);
		_detailPreview ??= GetNode<TextureRect>(
			"Modal/ContentMargin/ContentRoot/Split/DetailPanel/DetailMargin/DetailColumn/DetailPreviewPanel/DetailPreview"
		);
		_detailBadge ??= GetNode<Label>(
			"Modal/ContentMargin/ContentRoot/Split/DetailPanel/DetailMargin/DetailColumn/DetailBadge"
		);
		_detailTitle ??= GetNode<Label>(
			"Modal/ContentMargin/ContentRoot/Split/DetailPanel/DetailMargin/DetailColumn/DetailTitle"
		);
		_detailMeta ??= GetNode<Label>(
			"Modal/ContentMargin/ContentRoot/Split/DetailPanel/DetailMargin/DetailColumn/DetailMeta"
		);
		_detailBody ??= GetNode<Label>(
			"Modal/ContentMargin/ContentRoot/Split/DetailPanel/DetailMargin/DetailColumn/DetailBody"
		);
		_publishButton ??= GetNode<Button>("Modal/ContentMargin/ContentRoot/Footer/Publish");
		_cancelButton ??= GetNode<Button>("Modal/ContentMargin/ContentRoot/Footer/Cancel");
		_closeButton ??= GetNode<Button>("TitleBar/Margin/Row/Close");
	}
}

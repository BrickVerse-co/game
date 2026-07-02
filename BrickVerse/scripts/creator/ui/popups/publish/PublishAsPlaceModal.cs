// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrickVerse.Creator;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.UI.Components;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Formats;
using BrickVerse.Schemas.API;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Shared;

namespace BrickVerse.Creator.UI.Popups;

public partial class PublishAsPlaceModal : PopupWindowBase
{
	private sealed class PublishTargetRow
	{
		public Button Button { get; init; } = null!;
		public CreatorPlaceItem? Target { get; init; }
		public bool IsNewWorld { get; init; }
	}

	private sealed class PublishUniverseGroup
	{
		public long UniverseId { get; init; }
		public Control RootContainer { get; init; } = null!;
		public Button HeaderButton { get; init; } = null!;
		public Label ChevronLabel { get; init; } = null!;
		public Control WorldListContainer { get; init; } = null!;
		public bool Expanded { get; set; }
	}

	private const string PublishAsPlaceCardPath =
		"res://scenes/creator/popups/publish/components/publish_as_place_card.tscn";

	[Export] private Label _headerTitle = null!;
	[Export] private Label _leadTitle = null!;
	[Export] private Label _leadBody = null!;
	[Export] private Label _currentProjectLabel = null!;
	[Export] private LineEdit _searchInput = null!;
	[Export] private VBoxContainer _listContainer = null!;
	[Export] private Label _resultsLabel = null!;
	[Export] private Label _loadingLabel = null!;
	[Export] private Label _emptyLabel = null!;
	[Export] private TextureRect _detailPreview = null!;
	[Export] private Label _detailTitle = null!;
	[Export] private Label _detailMeta = null!;
	[Export] private Label _detailBody = null!;
	[Export] private Label _detailBadge = null!;
	[Export] private Button _publishButton = null!;
	[Export] private Button _cancelButton = null!;
	[Export] private Button _closeButton = null!;

	private readonly ButtonGroup _rowGroup = new();
	private readonly List<CreatorPlaceItem> _publishedWorlds = [];
	private readonly List<PublishTargetRow> _rows = [];
	private readonly Dictionary<long, PublishUniverseGroup> _universeGroups = [];
	private readonly HashSet<long> _expandedUniverses = [];
	private bool _isReady;
	private bool _openRequested;
	private World? _world;
	private World? _pendingWorld;
	private CreatorPlaceItem? _selectedTarget;
	private bool _useNewWorld = true;
	private bool _isBusy;
	private bool _isLoading;
	private int _detailPreviewRequestId;

	public override void _Ready()
	{
		base._Ready();
		ResolveNodeReferences();
		_isReady = true;

		_closeButton.Pressed += CloseWindow;
		_cancelButton.Pressed += CloseWindow;
		_publishButton.Pressed += Submit;
		_searchInput.TextChanged += _ => RefreshWorldList();

		if (_openRequested)
			BeginOpen();
	}

	public void Open(World world)
	{
		if (world == null)
		{
			PT.PrintErr("Cannot open PublishAsPlaceModal: world is null.");
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
		World world = _world;
		_selectedTarget = null;
		_useNewWorld = true;
		_publishedWorlds.Clear();
		_expandedUniverses.Clear();
		_searchInput.Text = "";
		_detailPreviewRequestId = 0;

		_headerTitle.Text = "Publish As";
		_leadTitle.Text = "Choose an experience to overwrite";
		_leadBody.Text =
			"Pick a destination, preview it on the right, and publish over the top or create a fresh experience.";
		_currentProjectLabel.Text = FormatCurrentProjectLabel(world);
		_resultsLabel.Text = "Loading...";

		SetBusy(true);
		SetSelection(null, true);
		_isLoading = true;

		bool loadFailed = false;

		try
		{
			CreatorPlaceItem[] items = await CreatorAPI.GetCreatedWorlds();
			_publishedWorlds.AddRange(items);
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Failed to load publish-as worlds: {ex.Message}");
			_resultsLabel.Text = "Failed to load";
			_emptyLabel.Text = "Failed to load your games.";
			emptyAndShowError(ex.Message);
			loadFailed = true;
		}
		finally
		{
			_isLoading = false;
			SetBusy(false);
			if (!loadFailed)
				RefreshWorldList();
		}
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

		string projectPath = _world.LinkedSession.ProjectFolderPath;

		if (!Directory.Exists(projectPath))
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

		try
		{
			var loadOverlay = CreatorService.Interface.LoadOverlay;
			string projectPath = _world.LinkedSession.ProjectFolderPath;
			PackedFormat.ReadProjectMetadata(File.ReadAllText(projectPath.PathJoin(Globals.ProjectMetaFileName)));
			byte[] packed = await PackedFormat.PackProject(
				projectPath,
				loadOverlay?.CreateProgressReporter("Publishing world")
			);

			loadOverlay?.SetStatus("Uploading now...");

			CreatorPublishResponse publishRes = await CreatorAPI.UploadWorld(
				packed,
				target?.UniverseId ?? 0,
				target?.WorldId ?? 0,
				true,
				CreatorAPI.UserID,
				"user"
			);

			if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.OpenWebAfterPublish))
				OS.ShellOpen(publishRes.Link);

			_world.UniverseID = publishRes.UniverseId;
			_world.WorldID = publishRes.WorldId;
			CreatorService.Interface.StatusBar?.SetStatus(_useNewWorld ? "World published" : "World overwritten");
			loadOverlay?.Hide();
			CloseWindow();
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Failed to publish world as another target: {ex.Message}");
			CreatorService.Interface.PopupAlert(ex.Message);
			ShowPublishError("Failed to publish: " + ex.Message);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private void RefreshWorldList()
	{
		if (_listContainer == null)
			return;

		foreach (Node child in _listContainer.GetChildren())
			child.QueueFree();

		_rows.Clear();
		_universeGroups.Clear();

		PublishTargetRow newRow = CreateTargetCard(
			null,
			"Create new experience",
			"Create a fresh world entry with the current project."
		);
		_listContainer.AddChild(newRow.Button);
		_rows.Add(newRow);

		string search = _searchInput.Text.Trim();
		List<CreatorPlaceItem> worlds = _publishedWorlds;

		if (!string.IsNullOrWhiteSpace(search))
		{
			worlds = worlds.Where(item =>
				(item.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
				|| item.WorldId?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
				|| item.UniverseId?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
			).ToList();
		}

		bool anyExisting = worlds.Count > 0;
		foreach (IGrouping<long, CreatorPlaceItem> universeGroup in worlds.GroupBy(item => item.UniverseId ?? 0))
		{
			List<CreatorPlaceItem> universeWorlds = [.. universeGroup];
			bool hasSelectedTarget =
				_selectedTarget.HasValue
				&& universeWorlds.Any(item => MatchesTarget(item, _selectedTarget.Value));
			bool expanded = !string.IsNullOrWhiteSpace(search) || hasSelectedTarget || _expandedUniverses.Contains(universeGroup.Key);

			if (hasSelectedTarget)
				_expandedUniverses.Add(universeGroup.Key);

			PublishUniverseGroup group = CreateUniverseGroup(
				universeGroup.Key,
				universeWorlds,
				expanded
			);
			_universeGroups[universeGroup.Key] = group;
			_listContainer.AddChild(group.RootContainer);
		}

		_resultsLabel.Text = anyExisting
			? $"{worlds.Count} experiences"
			: "No experiences";
		_loadingLabel.Visible = _isLoading;
		_emptyLabel.Visible = !_isLoading && !anyExisting;
		ApplySelectionState();
	}

	private PublishUniverseGroup CreateUniverseGroup(long universeId, List<CreatorPlaceItem> worlds, bool expanded)
	{
		PanelContainer groupPanel = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		groupPanel.AddThemeStyleboxOverride("panel", CreateRowStyle(
			new Color(0.08f, 0.08f, 0.09f, 1f),
			new Color(1f, 1f, 1f, 0.08f),
			1,
			12
		));

		MarginContainer groupMargin = new();
		SetMargins(groupMargin, 14);
		groupPanel.AddChild(groupMargin);

		VBoxContainer groupColumn = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		SetSeparation(groupColumn, 10);
		groupMargin.AddChild(groupColumn);

		Button headerButton = new()
		{
			Text = "",
			ToggleMode = true,
			ButtonPressed = expanded,
			Flat = true,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 42),
		};
		headerButton.AddThemeStyleboxOverride("normal", CreateRowStyle(
			new Color(0.075f, 0.075f, 0.085f, 1f),
			new Color(1f, 1f, 1f, 0.06f),
			1,
			10
		));
		headerButton.AddThemeStyleboxOverride("hover", CreateRowStyle(
			new Color(0.09f, 0.09f, 0.1f, 1f),
			new Color(0.22f, 0.74f, 1f, 0.45f),
			1,
			10
		));
		headerButton.AddThemeStyleboxOverride("pressed", CreateRowStyle(
			new Color(0.1f, 0.1f, 0.115f, 1f),
			new Color(0.22f, 0.74f, 1f, 0.55f),
			1,
			10
		));
		headerButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		headerButton.Pressed += () => ToggleUniverseGroup(universeId);

		HBoxContainer headerRow = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		SetSeparation(headerRow, 8);
		headerButton.AddChild(headerRow);

		Label chevronLabel = CreateLabel(expanded ? "▾" : "▸", 13, new Color(0.84f, 0.85f, 0.88f, 1f));
		chevronLabel.CustomMinimumSize = new Vector2(14, 0);
		headerRow.AddChild(chevronLabel);

		Label titleLabel = CreateLabel(
			$"Universe #{universeId}",
			13,
			new Color(0.94f, 0.95f, 0.97f, 1f)
		);
		titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		titleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		headerRow.AddChild(titleLabel);

		Label countLabel = CreateLabel(
			$"{worlds.Count} {(worlds.Count == 1 ? "world" : "worlds")}",
			10,
			new Color(0.72f, 0.74f, 0.78f, 1f)
		);
		headerRow.AddChild(countLabel);

		groupColumn.AddChild(headerButton);

		MarginContainer childMargin = new();
		childMargin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		childMargin.AddThemeConstantOverride("margin_left", 12);
		childMargin.AddThemeConstantOverride("margin_top", 2);
		childMargin.AddThemeConstantOverride("margin_right", 0);
		childMargin.AddThemeConstantOverride("margin_bottom", 0);
		childMargin.Visible = expanded;
		groupColumn.AddChild(childMargin);

		VBoxContainer childColumn = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		SetSeparation(childColumn, 10);
		childMargin.AddChild(childColumn);

		VBoxContainer childList = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		SetSeparation(childList, 10);
		childColumn.AddChild(childList);

		foreach (CreatorPlaceItem item in worlds)
		{
			PublishTargetRow row = CreateTargetCard(
				item,
				string.IsNullOrWhiteSpace(item.Name) ? "Untitled world" : item.Name,
				FormatTargetMeta(item)
			);
			childList.AddChild(row.Button);
			_rows.Add(row);
		}

		return new PublishUniverseGroup
		{
			UniverseId = universeId,
			RootContainer = groupPanel,
			HeaderButton = headerButton,
			ChevronLabel = chevronLabel,
			WorldListContainer = childMargin,
			Expanded = expanded,
		};
	}

	private void ToggleUniverseGroup(long universeId)
	{
		if (!_universeGroups.TryGetValue(universeId, out PublishUniverseGroup? group))
			return;

		group.Expanded = !group.Expanded;
		if (group.Expanded)
			_expandedUniverses.Add(universeId);
		else
			_expandedUniverses.Remove(universeId);

		group.HeaderButton.ButtonPressed = group.Expanded;
		group.ChevronLabel.Text = group.Expanded ? "▾" : "▸";
		group.WorldListContainer.Visible = group.Expanded;
	}

	private PublishTargetRow CreateTargetCard(CreatorPlaceItem? target, string title, string meta)
	{
		PublishAsPlaceCardUI card = Globals.CreateInstanceFromScene<PublishAsPlaceCardUI>(
			PublishAsPlaceCardPath
		);
		card.ToggleMode = true;
		card.ButtonGroup = _rowGroup;
		card.CustomMinimumSize = new Vector2(0, 216);
		card.Target = target;
		card.IsNewWorld = target == null;
		card.TitleText = title;
		card.MetaText = meta;
		card.DescriptionText = target == null
			? "Create a fresh world entry with the current project."
			: "Publish as will overwrite this existing world with the current project.";

		card.Pressed += () => SetSelection(target, target == null);

		return new PublishTargetRow
		{
			Button = card,
			Target = target,
			IsNewWorld = target == null,
		};
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
		foreach (PublishTargetRow row in _rows)
		{
			bool selected =
				_useNewWorld && row.IsNewWorld
				|| !_useNewWorld && row.Target.HasValue && _selectedTarget.HasValue && MatchesTarget(row.Target.Value, _selectedTarget.Value);

			row.Button.ButtonPressed = selected;
		}
	}

	private void UpdateDetailPanel()
	{
		if (_useNewWorld || !_selectedTarget.HasValue)
		{
			_detailBadge.Text = "NEW EXPERIENCE";
			_detailTitle.Text = "Create a new experience";
			_detailMeta.Text = "Creates a brand-new world entry with the current project.";
			_detailBody.Text =
				"Use this when you want the publish flow to create a new destination instead of replacing an existing one.";
			_detailPreview.Texture = GD.Load<Texture2D>(
				"res://assets/textures/creator/wizard/new-place/new_place.png"
			);
			return;
		}

		CreatorPlaceItem target = _selectedTarget.Value;
		_detailBadge.Text = "OVERWRITE";
		_detailTitle.Text = TruncateLabelText(
			string.IsNullOrWhiteSpace(target.Name) ? "Untitled world" : target.Name,
			32
		);
		_detailMeta.Text = TruncateLabelText(FormatTargetMeta(target), 64);
		_detailBody.Text =
			"Publish as will overwrite this existing world or place with the current project contents.";
		_detailPreviewRequestId++;
		int previewRequestId = _detailPreviewRequestId;
		_detailPreview.Texture = GD.Load<Texture2D>(
			"res://assets/textures/ui-icons/replace.svg"
		);

		if (!string.IsNullOrWhiteSpace(target.IconUrl))
		{
			WebAssetLoader.Singleton.GetResource(new() { URL = target.IconUrl }, result =>
			{
				if (previewRequestId != _detailPreviewRequestId)
					return;

				if (result is Texture2D texture)
				{
					_detailPreview.Texture = texture;
				}
			});
		}
	}

	private void SetPublishButtonLabel()
	{
		_publishButton.Text = _useNewWorld ? "Create New Experience" : "Overwrite Experience";
	}

	private void SetBusy(bool busy)
	{
		_isBusy = busy;
		_publishButton.Disabled = busy;
		_cancelButton.Disabled = busy;
		_closeButton.Disabled = busy;
		_searchInput.Editable = !busy;

		foreach (PublishTargetRow row in _rows)
			row.Button.Disabled = busy;

		foreach (PublishUniverseGroup group in _universeGroups.Values)
			group.HeaderButton.Disabled = busy;

		if (busy)
			_loadingLabel.Visible = true;
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

	private static bool MatchesTarget(CreatorPlaceItem left, CreatorPlaceItem right)
	{
		return left.Id == right.Id && left.WorldId == right.WorldId && left.UniverseId == right.UniverseId;
	}

	private static string FormatCurrentProjectLabel(World world)
	{
		string worldName = string.IsNullOrWhiteSpace(world.WorldName) ? "Untitled world" : world.WorldName;
		string universeName = string.IsNullOrWhiteSpace(world.UniverseName) ? "Untitled universe" : world.UniverseName;
		return $"{worldName} · {universeName}";
	}

	private static string FormatTargetMeta(CreatorPlaceItem item)
	{
		long worldId = item.WorldId ?? 0;
		long universeId = item.UniverseId ?? 0;
		DateTime updated = item.UpdatedAt ?? item.CreatedAt;
		string baseMeta = $"World #{worldId} · Universe #{universeId}";

		if (updated.Year <= 1)
			return baseMeta;

		return $"{baseMeta} · Updated {updated:MMM d, yyyy}";
	}

	private static string FormatUniverseHeader(long universeId, int worldCount)
	{
		string worldLabel = worldCount == 1 ? "world" : "worlds";
		return $"Universe #{universeId} · {worldCount} {worldLabel}";
	}

	private static string TruncateLabelText(string value, int maxLength)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
			return value;

		return value[..Math.Max(0, maxLength - 1)] + "…";
	}

	private static void SetMargins(MarginContainer container, int margin)
	{
		container.AddThemeConstantOverride("margin_left", margin);
		container.AddThemeConstantOverride("margin_top", margin);
		container.AddThemeConstantOverride("margin_right", margin);
		container.AddThemeConstantOverride("margin_bottom", margin);
	}

	private static void SetSeparation(Container container, int separation)
	{
		container.AddThemeConstantOverride("separation", separation);
	}

	private static Label CreateLabel(string text, int fontSize, Color color, TextServer.AutowrapMode wrapMode = TextServer.AutowrapMode.Off)
	{
		Label label = new()
		{
			Text = text,
			AutowrapMode = wrapMode,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Center,
		};

		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	private static StyleBoxFlat CreateRowStyle(Color backgroundColor, Color borderColor, int borderWidth, int cornerRadius)
	{
		return new StyleBoxFlat
		{
			BgColor = backgroundColor,
			BorderColor = borderColor,
			BorderWidthLeft = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthBottom = borderWidth,
			CornerRadiusTopLeft = cornerRadius,
			CornerRadiusTopRight = cornerRadius,
			CornerRadiusBottomLeft = cornerRadius,
			CornerRadiusBottomRight = cornerRadius,
		};
	}

	private void ResolveNodeReferences()
	{
		_headerTitle ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/TopBar/Title");
		_leadTitle ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/LeadGroup/LeadTitle");
		_leadBody ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/LeadGroup/LeadBody");
		_currentProjectLabel ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/LeadGroup/CurrentProject");
		_searchInput ??= GetNode<LineEdit>("Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/Search");
		_resultsLabel ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/HeaderRow/Results");
		_listContainer ??= GetNode<VBoxContainer>("Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/ListScroll/ListList");
		_loadingLabel ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/Loading");
		_emptyLabel ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/Split/ListPanel/ListMargin/ListColumn/Empty");
		_detailPreview ??= GetNode<TextureRect>("Modal/ContentMargin/ContentRoot/Split/DetailPanel/DetailMargin/DetailColumn/DetailPreviewPanel/DetailPreview");
		_detailBadge ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/Split/DetailPanel/DetailMargin/DetailColumn/DetailBadge");
		_detailTitle ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/Split/DetailPanel/DetailMargin/DetailColumn/DetailTitle");
		_detailMeta ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/Split/DetailPanel/DetailMargin/DetailColumn/DetailMeta");
		_detailBody ??= GetNode<Label>("Modal/ContentMargin/ContentRoot/Split/DetailPanel/DetailMargin/DetailColumn/DetailBody");
		_publishButton ??= GetNode<Button>("Modal/ContentMargin/ContentRoot/Footer/Publish");
		_cancelButton ??= GetNode<Button>("Modal/ContentMargin/ContentRoot/Footer/Cancel");
		_closeButton ??= GetNode<Button>("Modal/ContentMargin/ContentRoot/TopBar/Close");
	}

	private void emptyAndShowError(string message)
	{
		_emptyLabel.Visible = true;
		_emptyLabel.Text = message;
	}
}

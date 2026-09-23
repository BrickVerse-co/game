// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Godot;
using BrickVerse.Creator.UI.Layout;

namespace BrickVerse.Creator.UI.Docking;

/// <summary>
/// A dock zone. Every panel has a ID and may be owned by exactly one host.
/// </summary>
public sealed partial class DockHost : VBoxContainer
{
	private const string InternalNodeMetadata = "dock_internal";

	/// Id used for save/restore and as a drag-and-drop target key.
	[Export]
	public string HostId = "";

	public IReadOnlyList<DockPanel> Panels => _panels;
	public int ActiveIndex { get; private set; } = -1;
	public DockPanel? ActivePanel =>
		ActiveIndex >= 0 && ActiveIndex < _panels.Count ? _panels[ActiveIndex] : null;

	private readonly List<DockPanel> _panels = [];
	private DockTabBar _tabBar = null!;
	private PanelContainer _body = null!;
	private bool _syncingTabSelection;
	private bool _minimized;
	private Button _minimizeButton = null!;
	private float _expandedSplitHeight;

	public override void _Ready()
	{
		_tabBar = GetNode<DockTabBar>("Bar/TabBar");
		_body = GetNode<PanelContainer>("Body");

		_tabBar.Host = this;
		_tabBar.TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowAlways;
		_tabBar.DragToRearrangeEnabled = false;
		_tabBar.AddThemeConstantOverride("icon_max_width", 16);
		Texture2D? closeIcon = GD.Load<Texture2D>("res://assets/textures/ui-icons/dock-close.svg");
		if (closeIcon != null)
			_tabBar.AddThemeIconOverride("close", closeIcon);
		_tabBar.TabSelected += OnTabSelected;
		_tabBar.TabClosePressed += OnTabClosePressed;

		_minimizeButton = new Button
		{
			Name = "Minimize",
			Text = "−",
			Flat = true,
			TooltipText = "Collapse dock content",
			CustomMinimumSize = new Vector2(20, 20),
			FocusMode = FocusModeEnum.None,
		};
		_minimizeButton.AddThemeFontSizeOverride("font_size", 13);
		_minimizeButton.Pressed += ToggleMinimized;
		GetNode<Control>("Bar").AddChild(_minimizeButton);

		Button popOut = new()
		{
			Name = "PopOut",
			Text = "□",
			Flat = true,
			TooltipText = "Open active tab in a separate window",
			CustomMinimumSize = new Vector2(20, 20),
			FocusMode = FocusModeEnum.None,
		};
		popOut.AddThemeFontSizeOverride("font_size", 12);
		popOut.Pressed += () =>
		{
			if (ActivePanel != null)
				DockManager.PopOutPanel(ActivePanel.Id);
		};
		GetNode<Control>("Bar").AddChild(popOut);

		foreach (Node child in _body.GetChildren())
		{
			if (IsDockableChild(child) && child is Control control)
			{
				string id = control.HasMeta("_dock_id")
					? control.GetMeta("_dock_id").AsString()
					: control.Name;
				string title = control.HasMeta("_tab_name")
					? control.GetMeta("_tab_name").AsString()
					: control.Name;
				Texture2D? icon = control.HasMeta("_dock_icon")
					? control.GetMeta("_dock_icon").AsGodotObject() as Texture2D
					: null;
				AddPanel(new DockPanel(id, title, control, icon), suppressSave: true);
			}
		}
		if (_panels.Count > 0)
			ActivateIndex(0);

		DockManager.RegisterHost(this);
	}

	private static bool IsDockableChild(Node child)
	{
		return child is Control && !child.HasMeta(InternalNodeMetadata);
	}

	private void OnTabSelected(long idx)
	{
		if (!_syncingTabSelection)
			ActivateIndex((int)idx);
	}

	public override void _ExitTree() => DockManager.UnregisterHost(this);

	public bool ContainsPanelId(string panelId) => _panels.Any(panel => panel.Id == panelId);

	public int GetDropIndex(Vector2 globalPosition)
	{
		if (!_tabBar.GetGlobalRect().HasPoint(globalPosition))
			return _panels.Count;

		int index = _tabBar.GetTabIdxAtPoint(globalPosition - _tabBar.GetGlobalRect().Position);
		return index < 0 ? _panels.Count : index;
	}

	private void OnTabClosePressed(long idx)
	{
		if (idx >= 0 && idx < _panels.Count)
			DockManager.ClosePanel(_panels[(int)idx].Id);
	}

	public bool ActivatePanel(string panelId)
	{
		int index = _panels.FindIndex(panel => panel.Id == panelId);
		if (index < 0)
			return false;
		RevealRegion();
		ActivateIndex(index);
		return true;
	}

	private void RevealRegion()
	{
		FindCollapsiblePanel()?.SetCollapsed(false);
	}

	private void ToggleMinimized()
	{
		VSplitContainer? split = GetParent() as VSplitContainer;
		if (!_minimized && split != null && Size.Y > 0)
			_expandedSplitHeight = Size.Y;

		_minimized = !_minimized;
		_body.Visible = !_minimized;
		_minimizeButton.Text = _minimized ? "+" : "−";
		_minimizeButton.TooltipText = _minimized
			? "Restore dock content"
			: "Collapse dock content";
		DockRegion? region = FindDockRegion();
		if (region is { IsSplit: true } && split != null)
		{
			CustomMinimumSize = new Vector2(CustomMinimumSize.X, 0);
			Callable.From(() => ResizeInSplit(
				_minimized
					? GetNode<Control>("Bar").GetCombinedMinimumSize().Y
					: Mathf.Max(_expandedSplitHeight, 80)
			)).CallDeferred();
		}
		else
			FindCollapsiblePanel()?.SetCompact(_minimized);
		QueueRedraw();
	}

	private void ResizeInSplit(float targetHeight)
	{
		if (GetParent() is not VSplitContainer split || !IsInsideTree())
			return;

		float delta = targetHeight - Size.Y;
		if (Mathf.Abs(delta) < 1)
			return;

		int childIndex = GetIndex();
		int boundaryIndex = childIndex == 0 ? 0 : childIndex - 1;
		int[] offsets = split.SplitOffsets;
		if (boundaryIndex < 0 || boundaryIndex >= offsets.Length)
			return;

		offsets[boundaryIndex] += childIndex == 0
			? Mathf.RoundToInt(delta)
			: -Mathf.RoundToInt(delta);
		split.SplitOffsets = offsets;
		split.QueueSort();
	}

	private DockRegion? FindDockRegion()
	{
		Node? current = this;
		while (current != null)
		{
			if (current is DockRegion region)
				return region;
			current = current.GetParent();
		}
		return null;
	}

	private CollapsiblePanel? FindCollapsiblePanel()
	{
		Node? current = this;
		while (current != null)
		{
			if (current is CollapsiblePanel collapsible)
				return collapsible;
			current = current.GetParent();
		}
		return null;
	}

	public void SetPanelVisible(string panelId, bool visible)
	{
		int index = _panels.FindIndex(panel => panel.Id == panelId);
		if (index < 0)
			return;

		_tabBar.SetTabHidden(index, !visible);
		if (!visible && ActiveIndex == index)
		{
			int replacement = _panels.FindIndex(panel => panel.Id != panelId);
			if (replacement >= 0)
				ActivateIndex(replacement);
		}
	}

	public bool SetPanelIcon(string panelId, Texture2D icon)
	{
		int index = _panels.FindIndex(panel => panel.Id == panelId);
		if (index < 0)
			return false;
		_panels[index].Icon = icon;
		_tabBar.SetTabIcon(index, icon);
		return true;
	}

	/// <summary>
	/// Adds a valid panel to this host. The manager rejects conflicting IDs,
	/// and any stale ownership in another host is removed before reparenting.
	/// </summary>
	public bool AddPanel(DockPanel requestedPanel, int index = -1, bool suppressSave = false)
	{
		if (!DockManager.TryRegisterPanel(requestedPanel, out DockPanel panel))
		{
			requestedPanel.Content.GetParent()?.RemoveChild(requestedPanel.Content);
			requestedPanel.Content.QueueFree();
			return false;
		}

		DockManager.RemovePanelFromOtherHosts(panel.Id, this);
		DockManager.RegisterDefaultHost(panel.Id, HostId);
		if (ContainsPanelId(panel.Id))
			return false;

		if (panel.Content.GetParent() != _body)
		{
			if (panel.Content.GetParent() != null)
				panel.Content.Reparent(_body);
			else
				_body.AddChild(panel.Content);
		}

		if (index < 0 || index > _panels.Count)
			index = _panels.Count;
		_panels.Insert(index, panel);

		_tabBar.AddTab(panel.Title, panel.Icon);
		int addedAt = _tabBar.TabCount - 1;
		bool panelVisible = DockManager.IsPanelVisible(panel.Id);
		_tabBar.SetTabHidden(addedAt, !panelVisible);
		if (addedAt != index)
			_tabBar.MoveTab(addedAt, index);

		if (panelVisible)
			ActivateIndex(index);
		else
			panel.Content.Visible = false;
		if (!suppressSave)
			DockManager.SaveLayout();
		return true;
	}

	public DockPanel? RemovePanel(DockPanel panel)
	{
		int idx = _panels.FindIndex(existing => existing.Id == panel.Id);
		if (idx < 0)
			return null;

		DockPanel canonical = _panels[idx];
		_panels.RemoveAt(idx);
		_tabBar.RemoveTab(idx);
		ActiveIndex = _panels.Count == 0 ? -1 : Mathf.Clamp(idx, 0, _panels.Count - 1);
		if (ActiveIndex >= 0)
			ActivateIndex(ActiveIndex);

		return canonical;
	}

	public void Reorder(DockPanel panel, int newIndex, bool suppressSave = false)
	{
		int oldIndex = _panels.FindIndex(existing => existing.Id == panel.Id);
		if (oldIndex < 0)
			return;

		newIndex = Mathf.Clamp(newIndex, 0, _panels.Count - 1);
		if (oldIndex == newIndex)
			return;

		DockPanel canonical = _panels[oldIndex];
		_panels.RemoveAt(oldIndex);
		_panels.Insert(newIndex, canonical);
		_tabBar.MoveTab(oldIndex, newIndex);
		ActivateIndex(newIndex);
		if (!suppressSave)
			DockManager.SaveLayout();
	}

	public void ActivateIndex(int idx)
	{
		if (idx < 0 || idx >= _panels.Count)
			return;

		ActiveIndex = idx;
		for (int i = 0; i < _panels.Count; i++)
			_panels[i].Content.Visible = i == idx;

		if (_tabBar.CurrentTab == idx)
			return;

		_syncingTabSelection = true;
		try
		{
			_tabBar.CurrentTab = idx;
		}
		finally
		{
			_syncingTabSelection = false;
		}
	}

	public override void _Draw()
	{
		Control bar = GetNode<Control>("Bar");
		DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X, bar.Size.Y)), new Color("17191f"));
	}
}

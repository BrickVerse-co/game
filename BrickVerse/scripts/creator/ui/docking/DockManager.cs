// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using BrickVerse.Creator.UI.Layout;
using Godot;

namespace BrickVerse.Creator.UI.Docking;

/// <summary>
/// Manages all the panels, their ownership and the layout state, as well as the drag surface
/// </summary>
public static class DockManager
{
	private static readonly Dictionary<string, DockHost> _hosts = [];
	private static readonly Dictionary<string, DockRegion> _regions = [];
	private static readonly Dictionary<string, DockPanel> _panelsById = [];
	private static readonly Dictionary<string, string> _defaultHostIds = [];
	private static readonly HashSet<string> _hiddenPanelIds = [];
	private static readonly HashSet<string> _closedPanelIds = [];
	private static readonly Dictionary<string, Window> _floatingWindows = [];
	private static readonly Dictionary<string, string> _floatingSourceHostIds = [];
	private static DockDragSurface? _dragSurface;

	public static void RegisterHost(DockHost host) => _hosts[host.HostId] = host;

	public static void UnregisterHost(DockHost host)
	{
		_hosts.Remove(host.HostId);
		if (_hosts.Count == 0)
		{
			foreach (Window floating in _floatingWindows.Values)
				floating.QueueFree();
			_panelsById.Clear();
			_defaultHostIds.Clear();
			_hiddenPanelIds.Clear();
			_closedPanelIds.Clear();
			_floatingWindows.Clear();
			_floatingSourceHostIds.Clear();
		}
	}

	public static void RegisterRegion(DockRegion region) => _regions[region.RegionId] = region;

	public static void UnregisterRegion(DockRegion region) => _regions.Remove(region.RegionId);

	public static void RegisterDragSurface(DockDragSurface surface) => _dragSurface = surface;

	public static void UnregisterDragSurface(DockDragSurface surface)
	{
		if (_dragSurface == surface)
			_dragSurface = null;
	}

	/// <summary>
	/// Registers a DockPanel by its ID, returning the registered
	/// valid instance and returning true only if the ID is new or
	/// belongs to an existing panel with the exact same content.
	/// </summary>
	public static bool TryRegisterPanel(DockPanel candidate, out DockPanel valid)
	{
		if (string.IsNullOrWhiteSpace(candidate.Id))
		{
			valid = null!;
			return false;
		}

		if (_panelsById.TryGetValue(candidate.Id, out DockPanel? existing))
		{
			valid = existing;
			return existing.Content == candidate.Content;
		}

		_panelsById[candidate.Id] = candidate;
		valid = candidate;
		return true;
	}

	public static void BeginTabDrag() => _dragSurface?.BeginDrag();

	public static void RegisterDefaultHost(string panelId, string hostId)
	{
		if (!_defaultHostIds.ContainsKey(panelId))
			_defaultHostIds[panelId] = hostId;
	}

	public static bool IsPanelOpen(string panelId) =>
		_floatingWindows.ContainsKey(panelId)
		|| _hosts.Values.Any(host => host.ContainsPanelId(panelId));

	public static bool AddPanel(DockPanel panel, string preferredHostId)
	{
		if (!_hosts.TryGetValue(preferredHostId, out DockHost? host))
			return false;
		RegisterDefaultHost(panel.Id, preferredHostId);
		_closedPanelIds.Remove(panel.Id);
		return host.AddPanel(panel);
	}

	public static bool ClosePanel(string panelId, bool suppressSave = false)
	{
		if (_floatingWindows.ContainsKey(panelId))
			RedockFloatingPanel(panelId);
		if (!_panelsById.TryGetValue(panelId, out DockPanel? panel))
			return false;
		DockHost? host = _hosts.Values.FirstOrDefault(candidate =>
			candidate.ContainsPanelId(panelId)
		);
		if (host?.RemovePanel(panel) == null)
			return false;
		panel.Content.Hide();
		_closedPanelIds.Add(panelId);
		MergeEmptyRegions();
		if (!suppressSave)
			SaveLayout();
		return true;
	}

	public static bool OpenPanel(string panelId)
	{
		if (_floatingWindows.TryGetValue(panelId, out Window? floating))
		{
			floating.Show();
			return true;
		}
		if (IsPanelOpen(panelId))
			return ActivatePanel(panelId);
		if (!_panelsById.TryGetValue(panelId, out DockPanel? panel))
			return false;
		if (!_defaultHostIds.TryGetValue(panelId, out string? defaultHostId))
			return false;

		string regionId = defaultHostId.Split('.')[0];
		DockHost? target = _regions.TryGetValue(regionId, out DockRegion? region)
			? region.ReopenHost(defaultHostId)
			: _hosts.GetValueOrDefault(defaultHostId);
		if (target == null)
			return false;

		_closedPanelIds.Remove(panelId);
		bool added = target.AddPanel(panel, suppressSave: true);
		if (added)
		{
			ActivatePanel(panelId);
			SaveLayout();
		}
		return added;
	}

	public static bool PopOutPanel(string panelId)
	{
		if (_floatingWindows.ContainsKey(panelId))
			return OpenPanel(panelId);
		if (!_panelsById.TryGetValue(panelId, out DockPanel? panel))
			return false;
		DockHost? source = _hosts.Values.FirstOrDefault(host => host.ContainsPanelId(panelId));
		if (source?.RemovePanel(panel) == null)
			return false;

		Window window = new()
		{
			Title = panel.Title,
			InitialPosition = Window.WindowInitialPosition.CenterMainWindowScreen,
			Size = new Vector2I(720, 520),
			MinSize = new Vector2I(320, 220),
			WrapControls = true,
		};
		window.CloseRequested += () => RedockFloatingPanel(panelId);
		source.GetTree().Root.AddChild(window);
		panel.Content.Reparent(window);
		panel.Content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		panel.Content.Show();
		_floatingWindows[panelId] = window;
		_floatingSourceHostIds[panelId] = source.HostId;
		MergeEmptyRegions();
		SaveLayout();
		window.Popup();
		return true;
	}

	private static void RedockFloatingPanel(string panelId)
	{
		if (!_floatingWindows.Remove(panelId, out Window? window)
			|| !_panelsById.TryGetValue(panelId, out DockPanel? panel))
			return;

		string? preferredHostId = _floatingSourceHostIds.Remove(panelId, out string? sourceHostId)
			? sourceHostId
			: _defaultHostIds.GetValueOrDefault(panelId);
		if (preferredHostId == null)
		{
			window.QueueFree();
			return;
		}
		string regionId = preferredHostId.Split('.')[0];
		DockHost? target = _regions.TryGetValue(regionId, out DockRegion? region)
			? region.ReopenHost(preferredHostId)
			: _hosts.GetValueOrDefault(preferredHostId);
		if (target != null)
			target.AddPanel(panel, suppressSave: true);
		window.QueueFree();
		SaveLayout();
	}

	public static bool ActivatePanel(string panelId)
	{
		DockHost? host = _hosts.Values.FirstOrDefault(candidate =>
			candidate.ContainsPanelId(panelId)
		);
		return host?.ActivatePanel(panelId) == true;
	}

	public static void SetPanelVisible(string panelId, bool visible)
	{
		if (visible)
			_hiddenPanelIds.Remove(panelId);
		else
			_hiddenPanelIds.Add(panelId);

		DockHost? host = _hosts.Values.FirstOrDefault(candidate =>
			candidate.ContainsPanelId(panelId)
		);
		host?.SetPanelVisible(panelId, visible);
	}

	public static bool SetPanelIcon(string panelId, Godot.Texture2D icon)
	{
		if (!_panelsById.TryGetValue(panelId, out DockPanel? panel))
			return false;
		panel.Icon = icon;
		DockHost? host = _hosts.Values.FirstOrDefault(candidate =>
			candidate.ContainsPanelId(panelId)
		);
		return host == null || host.SetPanelIcon(panelId, icon);
	}

	public static bool IsPanelVisible(string panelId) => !_hiddenPanelIds.Contains(panelId);

	public static DockRegion? FindVisibleRegionAt(Godot.Vector2 globalPosition)
	{
		return _regions.Values.FirstOrDefault(region =>
			region.IsVisibleInTree() && region.GetGlobalRect().HasPoint(globalPosition)
		);
	}

	public static void RemovePanelFromOtherHosts(string panelId, DockHost keep)
	{
		if (!_panelsById.TryGetValue(panelId, out DockPanel? panel))
			return;
		foreach (
			var host in _hosts
				.Values.ToList()
				.Where(host => host != keep && host.ContainsPanelId(panelId))
		)
		{
			host.RemovePanel(panel);
		}
	}

	public static bool MovePanel(string panelId, string fromHostId, string toHostId, int index)
	{
		if (!_hosts.TryGetValue(fromHostId, out DockHost? requestedFrom))
			return false;
		if (!_hosts.TryGetValue(toHostId, out DockHost? to))
			return false;
		if (!_panelsById.TryGetValue(panelId, out DockPanel? panel))
			return false;

		DockHost? from =
			_hosts.Values.FirstOrDefault(host => host.ContainsPanelId(panelId)) ?? requestedFrom;
		if (!from.ContainsPanelId(panelId))
			return false;

		// Remove any old registration before the transfer is applied.
		RemovePanelFromOtherHosts(panelId, from);

		if (from == to)
		{
			from.Reorder(panel, index, suppressSave: true);
		}
		else
		{
			DockPanel? removed = from.RemovePanel(panel);
			if (removed == null)
				return false;

			if (!to.AddPanel(removed, index, suppressSave: true))
			{
				from.AddPanel(removed, suppressSave: true);
				return false;
			}
		}

		MergeEmptyRegions();
		SaveLayout();
		return true;
	}

	public static void SaveLayout()
	{
		DockLayoutData data = new();
		foreach ((string id, DockHost host) in _hosts)
		{
			data.Zones[id] = new DockZoneData
			{
				PanelIds = [.. host.Panels.Select(panel => panel.Id).Distinct()],
				ActiveIndex = host.ActiveIndex,
			};
		}

		foreach ((string id, DockRegion region) in _regions)
			data.RegionSplitModes[id] = region.IsSplit;
		data.ClosedPanelIds = [.. _closedPanelIds.Order()];

		DockLayoutService.SaveDockLayout(data);
	}

	public static void RestoreLayout()
	{
		DockLayoutData? data = DockLayoutService.LoadDockLayout();
		if (data == null)
			return;

		foreach ((string regionId, bool isSplit) in data.RegionSplitModes)
		{
			if (_regions.TryGetValue(regionId, out DockRegion? region))
				region.SetSplit(isSplit, suppressSave: true);
		}

		foreach ((string hostId, DockZoneData zone) in data.Zones)
		{
			if (!_hosts.TryGetValue(hostId, out DockHost? host))
				continue;

			int order = 0;
			foreach (string panelId in zone.PanelIds.Distinct())
			{
				if (!_panelsById.TryGetValue(panelId, out DockPanel? panel))
					continue;

				DockHost? currentHost = _hosts.Values.FirstOrDefault(candidate =>
					candidate.ContainsPanelId(panelId)
				);

				if (currentHost != host)
				{
					currentHost?.RemovePanel(panel);
					host.AddPanel(panel, order, suppressSave: true);
				}
				else
				{
					host.Reorder(panel, order, suppressSave: true);
				}

				order++;
			}

			if (zone.ActiveIndex >= 0 && zone.ActiveIndex < host.Panels.Count)
				host.ActivateIndex(zone.ActiveIndex);
		}

		foreach (string panelId in data.ClosedPanelIds.Distinct())
			ClosePanel(panelId, suppressSave: true);

		MergeEmptyRegions();
	}

	private static void MergeEmptyRegions()
	{
		foreach (DockRegion region in _regions.Values)
			region.MergeIfEitherSplitZoneIsEmpty();
	}

	public static void ResetLayout()
	{
		foreach (string panelId in _floatingWindows.Keys.ToList())
			RedockFloatingPanel(panelId);
		DockLayoutService.ResetToDefaults();
		_closedPanelIds.Clear();
		foreach (CollapsiblePanel panel in CollapsiblePanel.Registry.Values)
			panel.SetCollapsed(false);
		foreach (DockRegion region in _regions.Values)
			region.ResetToDefault();
		foreach ((string panelId, string defaultHostId) in _defaultHostIds.ToList())
		{
			if (!_panelsById.TryGetValue(panelId, out DockPanel? panel))
				continue;
			DockHost? current = _hosts.Values.FirstOrDefault(host => host.ContainsPanelId(panelId));
			if (current != null)
				current.RemovePanel(panel);
			if (_hosts.TryGetValue(defaultHostId, out DockHost? target))
				target.AddPanel(panel, suppressSave: true);
		}
		MergeEmptyRegions();
		SaveLayout();
	}
}

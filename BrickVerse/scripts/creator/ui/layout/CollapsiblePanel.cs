// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;

namespace BrickVerse.Creator.UI.Layout;

/// <summary>Which side of its parent SplitContainer this panel sits on.</summary>
public enum CollapseEdge
{
	// (HSplitContainer: Left, VSplitContainer: Top).
	Start,
	// (HSplitContainer: Right, VSplitContainer: Bottom).
	End
}

/// <summary>
/// Makes a panel that is inside a SplitContainer collapsible.
/// Attach directly to the panel node itself. Its parent must be the
/// SplitContainer that lays it out next to the center content.
/// Collapsed state and expanded size are remembered across sessions through DockLayoutService
/// </summary>
public sealed partial class CollapsiblePanel : Control
{
	// Id like: left_dock", "right_dock", "bottom_dock", set through Godot.
	[Export] public string PanelId = "";

	[Export] public CollapseEdge Edge = CollapseEdge.Start;

	[Export] public int ExpandedSize;

	public bool Collapsed { get; private set; }

	public static Dictionary<string, CollapsiblePanel> Registry { get; } = [];

	private SplitContainer? _split;
	private bool _vertical;
	private bool _compact;
	private Timer _saveSizeTimer = null!;

	public override void _Ready()
	{
		_split = GetParent() as SplitContainer;
		_vertical = _split is VSplitContainer;
		if (_split == null)
			// fallback to basic visibility toggling
			GD.PushWarning($"CollapsiblePanel '{Name}' has no SplitContainer parent.");
		else
			_split.Dragged += OnSplitterDragged;

		_saveSizeTimer = new Timer { OneShot = true, WaitTime = 0.2 };
		_saveSizeTimer.Timeout += SaveCurrentSize;
		AddChild(_saveSizeTimer);

		if (ExpandedSize <= 0)
		{
			ExpandedSize = (int)(_vertical ? Size.Y : Size.X);
			if (ExpandedSize <= 0) ExpandedSize = 300;
		}

		if (!string.IsNullOrEmpty(PanelId))
		{
			Registry[PanelId] = this;
			ExpandedSize = DockLayoutService.GetExpandedSize(PanelId, ExpandedSize);
			Collapsed = DockLayoutService.GetCollapsed(PanelId);
		}

		ApplyState(Collapsed);
	}

	public override void _ExitTree()
	{
		if (_split != null)
			_split.Dragged -= OnSplitterDragged;
		if (!string.IsNullOrEmpty(PanelId) && Registry.TryGetValue(PanelId, out CollapsiblePanel? self) && self == this)
			Registry.Remove(PanelId);
	}

	private void OnSplitterDragged(long offset)
	{
		if (!Collapsed && !_compact)
			_saveSizeTimer.Start();
	}

	private void SaveCurrentSize()
	{
		if (Collapsed || _compact || string.IsNullOrEmpty(PanelId))
			return;
		int size = GetCurrentSplitSize();
		if (size < 80)
			return;
		ExpandedSize = size;
		DockLayoutService.SetExpandedSize(PanelId, size);
	}

	public void Toggle() => SetCollapsed(!Collapsed);

	public void SetCompact(bool compact)
	{
		if (!_vertical || Collapsed || _compact == compact)
			return;
		_compact = compact;
		if (compact)
			ResizeTo(34);
		else
			CallDeferred(nameof(RestoreExpandedSize));
	}

	public void SetCollapsed(bool collapsed, bool animate = true)
	{
		if (Collapsed == collapsed) return;

		int currentSize = GetCurrentSplitSize();

		switch (collapsed)
		{
			// When Panel is closing: Remember the size off the Panel
			case true when currentSize > 0:
				{
					ExpandedSize = Mathf.Max(currentSize, 80);
					if (!string.IsNullOrEmpty(PanelId))
						DockLayoutService.SetExpandedSize(PanelId, ExpandedSize);
					break;
				}
			// When Panel is opening: Ensure the minimum size of 80px,
			// to prevent the panel from accidentally being cut off
			case false:
				ExpandedSize = Mathf.Max(ExpandedSize, 80);
				break;
		}

		Collapsed = collapsed;
		if (!string.IsNullOrEmpty(PanelId)) DockLayoutService.SetCollapsed(PanelId, collapsed);

		ApplyState(collapsed);
	}

	// Applies the State to the Panel
	private void ApplyState(bool collapsed)
	{
		Visible = !collapsed;
		MouseFilter = collapsed ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
		ApplyMinimumSize(collapsed ? 0 : 80);
		if (!collapsed)
			CallDeferred(nameof(RestoreExpandedSize));
		RequestSplitLayout();
	}

	private int GetCurrentSplitSize()
	{
		return Mathf.Max(0, (int)(_vertical ? Size.Y : Size.X));
	}

	private void ApplyMinimumSize(int size)
	{
		CustomMinimumSize = _vertical ? new Vector2(CustomMinimumSize.X, size) : new Vector2(size, CustomMinimumSize.Y);
	}

	private void RestoreExpandedSize()
	{
		if (Collapsed || _compact || _split == null || !IsInsideTree())
			return;
		ResizeTo(ExpandedSize);
	}

	private void ResizeTo(int targetSize)
	{
		if (_split == null || !IsInsideTree())
			return;

		int currentSize = GetCurrentSplitSize();
		int delta = targetSize - currentSize;
		if (Mathf.Abs(delta) < 2)
			return;

		int childIndex = GetIndex();
		int boundaryIndex = Edge == CollapseEdge.Start ? childIndex : childIndex - 1;
		int[] offsets = _split.SplitOffsets;
		if (boundaryIndex < 0 || boundaryIndex >= offsets.Length)
			return;

		offsets[boundaryIndex] += Edge == CollapseEdge.Start ? delta : -delta;
		_split.SplitOffsets = offsets;
		RequestSplitLayout();
	}

	private void RequestSplitLayout()
	{
		_split?.QueueSort();
	}

}

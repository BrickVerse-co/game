// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace BrickVerse.Creator.UI;

public partial class CreatorResponsiveLayout : Node
{
	private const float MenuHeight = 36.0f;
	private const float WideRibbonHeight = 74.0f;
	private const float TwoRowRibbonHeight = 124.0f;
	private const float ThreeRowRibbonHeight = 178.0f;
	private const float WorkspaceGap = 2.0f;

	private Viewport _viewport = null!;
	private Control _ribbon = null!;
	private Control _workspaceBackground = null!;
	private Control _splitter = null!;
	private Control? _colorPicker;

	public override void _Ready()
	{
		Node gui = GetParent();
		_viewport = GetViewport();
		_ribbon = gui.GetNode<Control>("Ribbon");
		_workspaceBackground = gui.GetNode<Control>("WorkspaceBackground");
		_splitter = gui.GetNode<Control>("Splitter");
		_colorPicker = gui.GetNodeOrNull<Control>("ColorPicker");

		_viewport.SizeChanged += QueueLayoutUpdate;
		CallDeferred(MethodName.UpdateLayout);
	}

	public override void _ExitTree()
	{
		if (GodotObject.IsInstanceValid(_viewport))
			_viewport.SizeChanged -= QueueLayoutUpdate;
	}

	private void QueueLayoutUpdate()
	{
		CallDeferred(MethodName.UpdateLayout);
	}

	private void UpdateLayout()
	{
		float availableWidth = _ribbon.Size.X;
		float ribbonHeight = availableWidth < 560.0f
			? ThreeRowRibbonHeight
			: availableWidth < 860.0f
				? TwoRowRibbonHeight
				: WideRibbonHeight;

		_ribbon.OffsetTop = MenuHeight;
		_ribbon.OffsetBottom = MenuHeight + ribbonHeight;

		float workspaceTop = _ribbon.OffsetBottom + WorkspaceGap;
		_workspaceBackground.OffsetTop = workspaceTop;
		_splitter.OffsetTop = workspaceTop;

		if (_colorPicker != null)
			_colorPicker.OffsetTop = workspaceTop;
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Client.UI;
using BrickVerse.Creator;
using BrickVerse.Shared;
using BrickVerse.Creator.UI;

namespace BrickVerse.Datamodel.Creator;

[Static("CreatorContext")]
[ExplorerExclude]
[SaveIgnore]
public sealed partial class CreatorContextService : Instance
{
	internal Camera Freelook = null!;
	internal Gizmos Gizmos = null!;
	internal PCGSplineEditor SplineEditor = null!;

	public bool IsViewportFocused
	{
		get
		{
			if (!Globals.Singleton.GetWindow().HasFocus()) return false;
			if (Root.Container == null || !Root.Container.IsVisibleInTree()) return false;
			if (Tabs.Singleton?.CurrentWorldContainer != Root.Container) return false;
			Control? rootFocusOwner = GDNode.GetWindow().GuiGetFocusOwner();
			Control? focusOwner = GDNode.GetViewport().GuiGetFocusOwner();
			if (rootFocusOwner == Root.Container || focusOwner is InputFallbackBase) return true;

			// Mouse presses reach WorldContainer before focus ownership settles for
			// the frame. Treat the pointer being inside the active viewport as focus
			// so the first click can select an object and restore keyboard focus.
			return Root.Container.GetGlobalRect().HasPoint(Root.Container.GetGlobalMousePosition());
		}
	}

	public CreatorSelections Selections = null!;
	public CreatorHistory History = null!;
	public CreatorAddons Addons = null!;
	public CreatorGUI GUIOverlay = null!;

	public override void Init()
	{
		if (Root.Container == null) return;
		NameOverride = "CreatorContext";

		Freelook = new()
		{
			Name = "FreeLook",
			Root = Root,
			Parent = this,
			Mode = Camera.CameraModeEnum.Free
		};
		Freelook.GDNode3D.GlobalPosition = new(0, 6, -4);
		Freelook.GDNode3D.RotationDegrees = new(-25, 0, 0);

		// Gizmos record transform operations immediately when they attach. Create
		// their stateful dependencies first so opening a world never observes a
		// partially initialized CreatorContext.
		Selections = Globals.LoadInstance<CreatorSelections>(Root);
		Selections.NameOverride = "Selections";
		Selections.NetworkParent = this;

		History = Globals.LoadInstance<CreatorHistory>(Root);
		History.NameOverride = "History";
		History.NetworkParent = this;

		Gizmos = new() { Name = "Gizmos" };
		Gizmos.Attach(Root, History, Freelook);
		// Keep the spatial editor controller in the same processing/render branch
		// as the world. Service proxy nodes may disable their own processing.
		Root.GDNode.AddChild(Gizmos, false, Node.InternalMode.Front);

		SplineEditor = new() { Name = "PCGSplineEditor" };
		SplineEditor.Attach(Root);
		GDNode.AddChild(SplineEditor, false, Node.InternalMode.Front);

		GUIOverlay = Globals.LoadInstance<CreatorGUI>(Root);
		GUIOverlay.NetworkParent = this;

		Addons = Globals.LoadInstance<CreatorAddons>(Root);
		Addons.NameOverride = "Addons";
		Addons.NetworkParent = this;

		base.Init();
	}
}

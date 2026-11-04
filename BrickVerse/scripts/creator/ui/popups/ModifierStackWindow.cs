// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using System.Linq;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>Editor controls for an EditableMeshPart's non-destructive modifier stack.</summary>
public sealed partial class ModifierStackWindow : PopupWindowBase
{
	private readonly EditableMeshPart _target;
	private VBoxContainer _list = null!;

	private ModifierStackWindow(EditableMeshPart target)
	{
		_target = target;
		Title = $"Modifier Stack — {target.Name}";
		Size = new Vector2I(600, 520);
		MinSize = new Vector2I(480, 360);
		InitialPosition = WindowInitialPosition.CenterMainWindowScreen;
		Transient = true;
	}

	public static void Open()
	{
		EditableMeshPart? target = World.Current?.CreatorContext.Selections.GetSelected()
			.OfType<EditableMeshPart>().FirstOrDefault();
		if (target == null)
		{
			CreatorService.Interface.StatusBar?.SetStatus("Select an EditableMeshPart to edit modifiers");
			return;
		}
		CreatorService.Interface.PopupWindow(new ModifierStackWindow(target));
	}

	public override void _Ready()
	{
		VBoxContainer page = new();
		page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 18);
		AddChild(page);
		HBoxContainer toolbar = new();
		page.AddChild(toolbar);
		AddButton(toolbar, "+ Subdivide", () => { _target.Modifiers.AddSubdivision(); Changed(); });
		AddButton(toolbar, "+ Weld", () => { _target.Modifiers.AddWeld(0.001f); Changed(); });
		AddButton(toolbar, "+ UV Project", () => { _target.Modifiers.AddPlanarUV(); Changed(); });
		_list = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		page.AddChild(_list);
		HBoxContainer actions = new();
		page.AddChild(actions);
		AddButton(actions, "Refresh preview", _target.RefreshModifierPreview);
		AddButton(actions, "Clear preview", _target.ClearModifierPreview);
		AddButton(actions, "Apply stack", () => { _target.ApplyModifiers(); Refresh(); });
		Refresh();
		base._Ready();
	}

	public override void _ExitTree()
	{
		_target.ClearModifierPreview();
		base._ExitTree();
	}

	private void Changed()
	{
		_target.RefreshModifierPreview();
		Refresh();
	}

	private void Refresh()
	{
		foreach (Node child in _list.GetChildren()) child.QueueFree();
		int[] modifiers = _target.Modifiers.GetModifiers();
		if (modifiers.Length == 0)
		{
			_list.AddChild(new Label { Text = "No modifiers. Add one from the toolbar.", Modulate = new Color("8993a4") });
			return;
		}
		foreach (int id in modifiers)
		{
			HBoxContainer row = new() { CustomMinimumSize = new Vector2(0, 42) };
			CheckButton enabled = new()
			{
				Text = $"{_target.Modifiers.GetModifierType(id)}  #{id}",
				ButtonPressed = _target.Modifiers.GetEnabled(id),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			enabled.Toggled += value => { _target.Modifiers.SetEnabled(id, value); Changed(); };
			row.AddChild(enabled);
			Button remove = new() { Text = "Remove" };
			remove.Pressed += () => { _target.Modifiers.Remove(id); Changed(); };
			row.AddChild(remove);
			_list.AddChild(row);
		}
	}

	private static void AddButton(Control parent, string text, System.Action action)
	{
		Button button = new() { Text = text, CustomMinimumSize = new Vector2(105, 34) };
		button.Pressed += action;
		parent.AddChild(button);
	}
}

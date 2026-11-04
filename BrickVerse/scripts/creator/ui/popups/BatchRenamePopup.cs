// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>Pattern-based, single-history-action rename tool for Explorer selections.</summary>
public sealed partial class BatchRenamePopup : Window
{
	private readonly Instance[] _targets;
	private readonly LineEdit _pattern = new() { Text = "{name}_{n}", ClearButtonEnabled = true };
	private readonly SpinBox _start = new() { MinValue = 0, MaxValue = 999999, Value = 1 };
	private readonly Label _preview = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
	private readonly Label _validation = new() { Visible = false };
	private readonly Button _apply = new() { Text = "Rename" };

	private BatchRenamePopup(Instance[] targets)
	{
		_targets = targets;
		Title = "Batch Rename";
		InitialPosition = WindowInitialPosition.CenterMainWindowScreen;
		Size = new Vector2I(580, 300);
		MinSize = new Vector2I(480, 270);
		Transient = true;
		Exclusive = true;
		CloseRequested += QueueFree;
	}

	public static void Open()
	{
		Instance[] targets = World.Current?.CreatorContext.Selections.GetSelected()
			.Where(target => !target.GetType().IsDefined(typeof(StaticAttribute), false))
			.ToArray() ?? [];
		if (targets.Length == 0)
		{
			CreatorService.Interface.StatusBar?.SetStatus("Select one or more objects to batch rename");
			return;
		}
		CreatorService.Interface.PopupWindow(new BatchRenamePopup(targets));
	}

	public override void _Ready()
	{
		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 22);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_right", 22);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		AddChild(margin);
		VBoxContainer layout = new(); layout.AddThemeConstantOverride("separation", 10); margin.AddChild(layout);
		Label title = new() { Text = $"Rename {_targets.Length} selected object{(_targets.Length == 1 ? "" : "s")}" };
		title.AddThemeFontSizeOverride("font_size", 21); layout.AddChild(title);
		layout.AddChild(new Label { Text = "Pattern · {name} keeps the original name, {class} inserts the class, and {n} inserts a number." });
		layout.AddChild(_pattern);
		HBoxContainer numberRow = new(); numberRow.AddChild(new Label { Text = "Starting number", VerticalAlignment = VerticalAlignment.Center }); numberRow.AddChild(_start); layout.AddChild(numberRow);
		_preview.AddThemeColorOverride("font_color", new Color("9fb0ca")); layout.AddChild(_preview);
		_validation.AddThemeColorOverride("font_color", new Color("f16d6d")); layout.AddChild(_validation);
		Control spacer = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; layout.AddChild(spacer);
		HBoxContainer actions = new() { Alignment = BoxContainer.AlignmentMode.End }; Button cancel = new() { Text = "Cancel" }; actions.AddChild(cancel); actions.AddChild(_apply); layout.AddChild(actions);
		_pattern.TextChanged += _ => RefreshPreview(); _pattern.TextSubmitted += _ => Apply(); _start.ValueChanged += _ => RefreshPreview();
		cancel.Pressed += QueueFree; _apply.Pressed += Apply;
		RefreshPreview(); _pattern.GrabFocus(); _pattern.SelectAll();
	}

	private string Format(Instance target, int index) => _pattern.Text
		.Replace("{name}", target.Name)
		.Replace("{class}", target.ClassName)
		.Replace("{n}", ((int)_start.Value + index).ToString());

	private void RefreshPreview()
	{
		_apply.Disabled = string.IsNullOrWhiteSpace(_pattern.Text);
		_validation.Visible = _apply.Disabled;
		_validation.Text = _apply.Disabled ? "Enter a rename pattern." : "";
		_preview.Text = "Preview: " + string.Join("  ·  ", _targets.Take(3).Select((target, index) => Format(target, index))) + (_targets.Length > 3 ? "  …" : "");
	}

	private void Apply()
	{
		if (_apply.Disabled || World.Current == null) return;
		Dictionary<Instance, string> before = _targets.Where(target => !target.IsDeleted).ToDictionary(target => target, target => target.Name);
		Dictionary<Instance, string> after = before.Keys.Select((target, index) => (target, name: Format(target, index).Trim())).Where(item => item.name.Length > 0).ToDictionary(item => item.target, item => item.name);
		if (after.Count == 0) { _validation.Text = "The pattern produced no valid names."; _validation.Visible = true; return; }
		void SetNames(Dictionary<Instance, string> names)
		{
			// Clear sibling-name collisions first so swaps and undo restore exact names.
			int temporaryIndex = 0;
			foreach (Instance target in names.Keys)
				if (!target.IsDeleted)
					target.Name = $"__BV_RENAME_{target.ObjectID}_{temporaryIndex++}";
			foreach ((Instance target, string name) in names)
				if (!target.IsDeleted)
					target.Name = name;
		}
		SetNames(after);
		World.Current.CreatorContext.History.RecordAppliedAction("Batch rename", new BVCallback(_ => SetNames(after)), new BVCallback(_ => SetNames(before)));
		CreatorService.Interface.StatusBar?.SetStatus($"Renamed {after.Count} object{(after.Count == 1 ? "" : "s")}");
		QueueFree();
	}
}

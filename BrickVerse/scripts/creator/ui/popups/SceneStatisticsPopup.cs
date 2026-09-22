// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>A lightweight scene audit view for inspecting world composition.</summary>
public sealed partial class SceneStatisticsPopup : PopupWindowBase
{
	private static SceneStatisticsPopup? _openInstance;
	private readonly VBoxContainer _summary = new();
	private readonly VBoxContainer _classRows = new();
	private readonly LineEdit _filter = new();
	private readonly Label _status = new();
	private readonly List<(string Name, int Count)> _classes = [];
	private string _report = "";

	public static void Open()
	{
		if (_openInstance != null && IsInstanceValid(_openInstance))
		{
			_openInstance.GrabFocus();
			return;
		}
		SceneStatisticsPopup popup = new();
		_openInstance = popup;
		CreatorService.Interface.PopupWindow(popup);
	}

	public SceneStatisticsPopup()
	{
		Title = "Scene Statistics";
		Size = new Vector2I(720, 620);
		MinSize = new Vector2I(560, 440);
		InitialPosition = WindowInitialPosition.CenterMainWindowScreen;
		Transient = true;
	}

	public override void _Ready()
	{
		BuildInterface();
		Refresh();
		base._Ready();
	}

	public override void _ExitTree()
	{
		if (_openInstance == this) _openInstance = null;
		base._ExitTree();
	}

	private void BuildInterface()
	{
		Panel background = new();
		background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		background.AddThemeStyleboxOverride("panel", Surface(new Color("101116"), 0));
		AddChild(background);

		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 22);
		AddChild(margin);
		VBoxContainer root = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		root.AddThemeConstantOverride("separation", 14);
		margin.AddChild(root);

		HBoxContainer header = new();
		VBoxContainer titleBlock = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		Label title = new() { Text = "Scene Statistics" };
		title.AddThemeFontSizeOverride("font_size", 24);
		titleBlock.AddChild(title);
		Label subtitle = new() { Text = "Inspect scene complexity and find the instance types that dominate this world.", Modulate = new Color("8993a4") };
		subtitle.AddThemeFontSizeOverride("font_size", 12);
		titleBlock.AddChild(subtitle);
		header.AddChild(titleBlock);
		Button refresh = ActionButton("Refresh", false);
		refresh.Pressed += Refresh;
		header.AddChild(refresh);
		Button copy = ActionButton("Copy report", true);
		copy.Pressed += CopyReport;
		header.AddChild(copy);
		root.AddChild(header);

		_summary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_summary.AddThemeConstantOverride("separation", 8);
		root.AddChild(_summary);

		HBoxContainer sectionHeader = new();
		Label breakdown = new() { Text = "INSTANCE BREAKDOWN", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Modulate = new Color("aeb7c6") };
		breakdown.AddThemeFontSizeOverride("font_size", 11);
		sectionHeader.AddChild(breakdown);
		_filter.PlaceholderText = "Filter classes…";
		_filter.ClearButtonEnabled = true;
		_filter.CustomMinimumSize = new Vector2(220, 34);
		_filter.TextChanged += _ => RenderClasses();
		sectionHeader.AddChild(_filter);
		root.AddChild(sectionHeader);

		ScrollContainer scroll = new()
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		_classRows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_classRows.AddThemeConstantOverride("separation", 5);
		scroll.AddChild(_classRows);
		root.AddChild(scroll);

		_status.Modulate = new Color("7d8798");
		_status.AddThemeFontSizeOverride("font_size", 11);
		root.AddChild(_status);
	}

	private void Refresh()
	{
		World? world = World.Current;
		if (world == null)
		{
			_status.Text = "Open a world to inspect scene statistics.";
			return;
		}

		Instance[] instances = world.GetDescendants();
		int visible = instances.Count(instance => !instance.IsHidden);
		int dynamicCount = instances.Count(instance => instance is Dynamic);
		int scripts = instances.Count(instance => instance is BrickVerse.Datamodel.Script);
		int selected = world.CreatorContext.Selections.SelectedInstances.Count;
		_classes.Clear();
		_classes.AddRange(instances.GroupBy(instance => instance.ClassName)
			.Select(group => (group.Key, group.Count()))
			.OrderByDescending(item => item.Item2)
			.ThenBy(item => item.Key));

		Clear(_summary);
		HBoxContainer cards = new();
		cards.AddThemeConstantOverride("separation", 8);
		cards.AddChild(MetricCard("INSTANCES", instances.Length.ToString("N0"), "Total descendants"));
		cards.AddChild(MetricCard("VISIBLE", visible.ToString("N0"), $"{instances.Length - visible:N0} hidden"));
		cards.AddChild(MetricCard("3D OBJECTS", dynamicCount.ToString("N0"), "Transformable"));
		cards.AddChild(MetricCard("SCRIPTS", scripts.ToString("N0"), $"{selected:N0} selected"));
		_summary.AddChild(cards);

		StringBuilder report = new();
		report.AppendLine($"Scene Statistics — {world.Name}");
		report.AppendLine($"Instances: {instances.Length:N0} | Visible: {visible:N0} | 3D Objects: {dynamicCount:N0} | Scripts: {scripts:N0}");
		foreach ((string name, int count) in _classes) report.AppendLine($"{name}: {count:N0}");
		_report = report.ToString().TrimEnd();
		RenderClasses();
		_status.Text = $"Updated {System.DateTime.Now:t}  •  {_classes.Count:N0} instance classes";
	}

	private void RenderClasses()
	{
		Clear(_classRows);
		string query = _filter.Text.Trim();
		IEnumerable<(string Name, int Count)> rows = _classes;
		if (query.Length > 0) rows = rows.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
		int max = Math.Max(1, _classes.FirstOrDefault().Count);
		int shown = 0;
		foreach ((string name, int count) in rows)
		{
			PanelContainer card = new();
			card.AddThemeStyleboxOverride("panel", Surface(new Color("171920"), 7));
			MarginContainer cardMargin = new();
			SetMargins(cardMargin, 12, 7);
			HBoxContainer row = new();
			Label className = new() { Text = name, CustomMinimumSize = new Vector2(190, 0) };
			row.AddChild(className);
			ProgressBar bar = new() { MinValue = 0, MaxValue = max, Value = count, ShowPercentage = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 7) };
			row.AddChild(bar);
			Label value = new() { Text = count.ToString("N0"), CustomMinimumSize = new Vector2(60, 0), HorizontalAlignment = HorizontalAlignment.Right, Modulate = new Color("b9c4d5") };
			row.AddChild(value);
			cardMargin.AddChild(row);
			card.AddChild(cardMargin);
			_classRows.AddChild(card);
			shown++;
		}
		if (shown == 0) _classRows.AddChild(new Label { Text = "No instance classes match this filter.", Modulate = new Color("7d8798") });
	}

	private void CopyReport()
	{
		DisplayServer.ClipboardSet(_report);
		_status.Text = "Report copied to clipboard.";
	}

	private static PanelContainer MetricCard(string label, string value, string detail)
	{
		PanelContainer card = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		card.AddThemeStyleboxOverride("panel", Surface(new Color("171920"), 9));
		MarginContainer margin = new();
		SetMargins(margin, 12, 10);
		VBoxContainer column = new();
		Label caption = new() { Text = label, Modulate = new Color("7f899a") };
		caption.AddThemeFontSizeOverride("font_size", 10);
		column.AddChild(caption);
		Label amount = new() { Text = value };
		amount.AddThemeFontSizeOverride("font_size", 22);
		column.AddChild(amount);
		Label sub = new() { Text = detail, Modulate = new Color("788294") };
		sub.AddThemeFontSizeOverride("font_size", 10);
		column.AddChild(sub);
		margin.AddChild(column);
		card.AddChild(margin);
		return card;
	}

	private static Button ActionButton(string text, bool primary)
	{
		Button button = new() { Text = text, CustomMinimumSize = new Vector2(104, 36) };
		if (primary) button.AddThemeColorOverride("font_color", Colors.White);
		return button;
	}

	private static StyleBoxFlat Surface(Color color, int radius) => new()
	{
		BgColor = color,
		BorderColor = new Color("292d37"),
		BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
		CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
		CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
	};

	private static void SetMargins(MarginContainer margin, int horizontal, int vertical)
	{
		margin.AddThemeConstantOverride("margin_left", horizontal);
		margin.AddThemeConstantOverride("margin_right", horizontal);
		margin.AddThemeConstantOverride("margin_top", vertical);
		margin.AddThemeConstantOverride("margin_bottom", vertical);
	}

	private static void Clear(Node node)
	{
		foreach (Node child in node.GetChildren()) child.QueueFree();
	}
}

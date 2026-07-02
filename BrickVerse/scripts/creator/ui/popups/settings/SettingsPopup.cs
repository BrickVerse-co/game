// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.UI.Components;
using BrickVerse.Shared;
using BrickVerse.Shared.Settings;
using System.Collections.Generic;
using System.Linq;
using System;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class SettingsPopup : PopupWindowBase
{
	private const string SettingsPropertyPath = "res://scenes/creator/popups/settings/components/settings_property.tscn";
	[Export] private Tree _categoryTree = null!;
	[Export] private Control _layout = null!;
	[Export] private LineEdit _settingsSearchEdit = null!;
	[Export] private LineEdit _keybindSearchEdit = null!;

	private static readonly Dictionary<string, List<SettingDef>> SectionDefs =
		CreatorSettingsRegistry.Definitions.Values
			.GroupBy(d => d.SectionKey)
			.ToDictionary(g => g.Key, g => g.ToList());

	private static readonly IReadOnlyList<SettingSectionDef> SortedSections =
		[.. CreatorSettingsRegistry.Sections.OrderBy(s => s.SortOrder)];

	private readonly Dictionary<TreeItem, string> _itemToSectionKey = [];
	private readonly Dictionary<string, List<Control>> _sectionUIs = [];
	private readonly Dictionary<Control, string> _keybindGroupByControl = [];
	private string _activeSection = string.Empty;

	private static readonly (string GroupKey, string Label, string[] SettingKeys)[] KeybindGroups =
	[
		("tools", "Tools", [
			CreatorSettingKeys.Keybinds.ToolSelect,
			CreatorSettingKeys.Keybinds.ToolMove,
			CreatorSettingKeys.Keybinds.ToolRotate,
			CreatorSettingKeys.Keybinds.ToolScale,
		]),
		("transform", "Transform", [
			CreatorSettingKeys.Keybinds.RotateSelection,
			CreatorSettingKeys.Keybinds.TiltSelection,
		]),
		("modes", "Modes", [
			CreatorSettingKeys.Keybinds.ToggleTransformOrientation,
			CreatorSettingKeys.Keybinds.TogglePivotMode,
		]),
	];

	public override void _Ready()
	{
		TreeItem root = _categoryTree.CreateItem();
		TreeItem? firstSelected = null;

		foreach (var section in SortedSections)
		{
			if (!SectionDefs.TryGetValue(section.Key, out var defs) || defs.Count == 0) continue;

			TreeItem ch = root.CreateChild();
			ch.SetText(0, section.Label);
			_itemToSectionKey[ch] = section.Key;

			firstSelected ??= ch;
		}

		_categoryTree.ItemSelected += OnItemSelected;
		_settingsSearchEdit.TextChanged += OnSearchChanged;
		_keybindSearchEdit.TextChanged += OnSearchChanged;
		firstSelected?.Select(0);
		base._Ready();
	}

	public override void _ExitTree()
	{
		_categoryTree.ItemSelected -= OnItemSelected;
		_settingsSearchEdit.TextChanged -= OnSearchChanged;
		_keybindSearchEdit.TextChanged -= OnSearchChanged;
		base._ExitTree();
	}

	private void OnItemSelected()
	{
		if (!_itemToSectionKey.TryGetValue(_categoryTree.GetSelected(), out var sectionKey))
			return;

		if (sectionKey == _activeSection)
			return;

		if (_sectionUIs.TryGetValue(_activeSection, out var prevUIs))
		{
			foreach (var ui in prevUIs)
				ui.Visible = false;
		}

		_activeSection = sectionKey;

		if (!_sectionUIs.TryGetValue(sectionKey, out var cachedUIs))
		{
			cachedUIs = [];
			if (!SectionDefs.TryGetValue(sectionKey, out var defs))
				return;

			if (sectionKey == "keybinds")
			{
				BuildKeybindSection(defs, cachedUIs);
			}
			else
			{
				foreach (SettingDef def in defs)
				{
					SettingsPropertyUI ui = CreatePropertyUI(def);
					cachedUIs.Add(ui);
					_layout.AddChild(ui);
				}
			}

			if (sectionKey == "advanced")
			{
				HSeparator separator = new();
				_layout.AddChild(separator);
				cachedUIs.Add(separator);

				UIViewLicensesRow licensesRow = GD.Load<PackedScene>("res://scenes/shared/settings/licenses_row.tscn").Instantiate<UIViewLicensesRow>();
				licensesRow.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
				_layout.AddChild(licensesRow);
				cachedUIs.Add(licensesRow);
			}

			_sectionUIs[sectionKey] = cachedUIs;
		}

		UpdateSearchVisibility();
		ApplyFiltersToSection(sectionKey);
	}

	private void OnSearchChanged(string _)
	{
		UpdateSearchVisibility();
		ApplyFiltersToSection(_activeSection);
	}

	private void UpdateSearchVisibility()
	{
		if (!IsInstanceValid(_keybindSearchEdit))
			return;

		bool isKeybindSection = _activeSection.Equals("keybinds", StringComparison.OrdinalIgnoreCase);
		_keybindSearchEdit.Visible = isKeybindSection;
	}

	private void ApplyFiltersToSection(string sectionKey)
	{
		if (!_sectionUIs.TryGetValue(sectionKey, out var controls))
			return;

		string settingQuery = _settingsSearchEdit.Text?.Trim() ?? string.Empty;
		string keybindQuery = _keybindSearchEdit.Text?.Trim() ?? string.Empty;
		bool isKeybindSection = sectionKey.Equals("keybinds", StringComparison.OrdinalIgnoreCase);
		Dictionary<string, bool> visibleGroup = [];

		foreach (Control child in controls)
		{
			if (child is not SettingsPropertyUI spui)
				continue;


			bool visible = spui.PropertyVisible
				&& MatchesQuery(spui.SettingDef, settingQuery)
				&& (!isKeybindSection || MatchesQuery(spui.SettingDef, keybindQuery));
			spui.Visible = visible;

			if (isKeybindSection && _keybindGroupByControl.TryGetValue(spui, out string? groupKey) && !string.IsNullOrEmpty(groupKey) && visible)
				visibleGroup[groupKey] = true;
		}

		foreach (Control child in controls)
		{
			if (child is SettingsPropertyUI)
				continue;

			if (isKeybindSection && _keybindGroupByControl.TryGetValue(child, out string? groupKey) && !string.IsNullOrEmpty(groupKey))
			{
				child.Visible = visibleGroup.ContainsKey(groupKey);
				continue;
			}

			child.Visible = true;
		}
	}

	private void BuildKeybindSection(List<SettingDef> defs, List<Control> cachedUIs)
	{
		Dictionary<string, SettingDef> byKey = defs.ToDictionary(x => x.Key, x => x);

		foreach (var group in KeybindGroups)
		{
			List<SettingDef> groupDefs = [];
			foreach (string key in group.SettingKeys)
			{
				if (byKey.TryGetValue(key, out SettingDef? def) && def != null)
					groupDefs.Add(def);
			}

			if (groupDefs.Count == 0)
				continue;

			Label header = new()
			{
				Text = group.Label,
			};
			header.AddThemeFontSizeOverride("font_size", 15);
			_layout.AddChild(header);
			cachedUIs.Add(header);
			_keybindGroupByControl[header] = group.GroupKey;

			HSeparator separator = new();
			_layout.AddChild(separator);
			cachedUIs.Add(separator);
			_keybindGroupByControl[separator] = group.GroupKey;

			foreach (SettingDef def in groupDefs)
			{
				SettingsPropertyUI ui = CreatePropertyUI(def);
				_layout.AddChild(ui);
				cachedUIs.Add(ui);
				_keybindGroupByControl[ui] = group.GroupKey;
			}
		}
	}

	private static SettingsPropertyUI CreatePropertyUI(SettingDef def)
	{
		SettingsPropertyUI ui = Globals.CreateInstanceFromScene<SettingsPropertyUI>(SettingsPropertyPath);
		ui.Init(def, CreatorSettingsService.Instance);
		return ui;
	}

	private static bool MatchesQuery(SettingDef def, string query)
	{
		if (string.IsNullOrWhiteSpace(query))
			return true;

		return def.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| def.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| def.Key.Contains(query, StringComparison.OrdinalIgnoreCase);
	}
}

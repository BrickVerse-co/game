// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Client.Settings;
using BrickVerse.Datamodel;
using System.Linq;

namespace BrickVerse.Client.UI;

public sealed partial class UIMenuSettings : UIMenuViewBase
{
	[Export] private Control _viewContainer = null!;
	[Export] private VBoxContainer _categoryContainer = null!;

	private readonly ButtonGroup _categoryButtonGroup = new();
	private readonly System.Collections.Generic.Dictionary<string, Button> _categoryButtons = [];
	private string? _currentSectionKey;


	public override void _Ready()
	{
		BuildCategories();
		base._Ready();
	}

	public override void ShowView()
	{
		BuildCategories();
		string firstSection = ClientSettingsRegistry.Sections.OrderBy(s => s.SortOrder).First().Key;
		SwitchSection(firstSection);
		base.ShowView();
	}

	private void BuildCategories()
	{
		_categoryButtons.Clear();

		foreach (Node child in _categoryContainer.GetChildren())
		{
			child.QueueFree();
		}

		foreach (var section in ClientSettingsRegistry.Sections.OrderBy(s => s.SortOrder))
		{
			string sectionKey = section.Key;
			Button btn = CreateCategoryButton(section);
			btn.ButtonGroup = _categoryButtonGroup;

			btn.Pressed += () => SwitchSection(sectionKey);
			_categoryContainer.AddChild(btn);
			_categoryButtons[sectionKey] = btn;
		}

		if (World.Current?.GameSettings != null)
		{
			foreach (string category in World.Current.GameSettings.GetSettings().Select(x => string.IsNullOrWhiteSpace(x.Category) ? "Game" : x.Category).Distinct())
			{
				string sectionKey = "game:" + category;
				Button btn = CreateCategoryButton(category, "res://assets/textures/ui-icons/settings.svg"); btn.ButtonGroup = _categoryButtonGroup;
				btn.Pressed += () => SwitchSection(sectionKey); _categoryContainer.AddChild(btn); _categoryButtons[sectionKey] = btn;
			}
		}
	}

	private static Button CreateCategoryButton(string label, string iconPath) => CreateCategoryButton(new Shared.Settings.SettingSectionDef { Key = label, Label = label, IconPath = iconPath });

	private static Button CreateCategoryButton(Shared.Settings.SettingSectionDef section)
	{
		Button btn = new()
		{
			ToggleMode = true,
			CustomMinimumSize = new Vector2(0, 50),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseDefaultCursorShape = CursorShape.PointingHand,
			Text = string.Empty,
			FocusMode = FocusModeEnum.All
		};

		HBoxContainer layout = new()
		{
			MouseFilter = MouseFilterEnum.Ignore
		};
		layout.AddThemeConstantOverride("separation", 12);
		layout.SetAnchorsPreset(LayoutPreset.FullRect);
		layout.OffsetLeft = 16;
		layout.OffsetRight = -16;
		btn.AddChild(layout);

		if (!string.IsNullOrEmpty(section.IconPath))
		{
			TextureRect icon = new()
			{
				MouseFilter = MouseFilterEnum.Ignore,
				CustomMinimumSize = new Vector2(28, 0),
				Texture = GD.Load<Texture2D>(section.IconPath),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
			};
			layout.AddChild(icon);
		}

		Label label = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
			Text = section.Label,
			VerticalAlignment = VerticalAlignment.Center
		};
		label.AddThemeFontSizeOverride("font_size", 16);
		layout.AddChild(label);

		return btn;
	}

	private void SwitchSection(string key)
	{
		if (_currentSectionKey == key)
		{
			return;
		}

		_currentSectionKey = key;
		UpdateCategoryButtons();

		foreach (Node child in _viewContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (key.StartsWith("game:")) _viewContainer.AddChild(new GameSettingsPage { Category = key[5..] });
		else _viewContainer.AddChild(new SettingsSectionPage { SectionKey = key });
	}

	public void SwitchToGameSettings()
	{
		string? key = _categoryButtons.Keys.FirstOrDefault(x => x.Equals("game:Game", System.StringComparison.OrdinalIgnoreCase))
			?? _categoryButtons.Keys.FirstOrDefault(x => x.StartsWith("game:"));
		if (key != null) SwitchSection(key);
	}

	private void UpdateCategoryButtons()
	{
		foreach (var pair in _categoryButtons)
		{
			pair.Value.SetPressedNoSignal(pair.Key == _currentSectionKey);
		}
	}
}

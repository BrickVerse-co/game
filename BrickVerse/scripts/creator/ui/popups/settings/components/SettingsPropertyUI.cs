// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator;
using BrickVerse.Creator.Properties;
using BrickVerse.Creator.Settings;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Enums;
using BrickVerse.Shared;
using BrickVerse.Shared.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Creator.UI.Components;

public partial class SettingsPropertyUI : Control
{
	private const string SliderScenePath = "res://scenes/creator/popups/settings/components/settings_slider.tscn";
	[Export] private Label _propNameLabel = null!;
	[Export] private Control _propContainer = null!;

	public SettingDef SettingDef { get; private set; } = null!;
	public ISettingsContext SettingsContext { get; private set; } = null!;
	public bool PropertyVisible = true;

	private IProperty? _input;
	private Button? _keybindButton;
	private bool _suppressChanged;

	private static readonly HashSet<string> KeybindSettingKeys =
	[
		CreatorSettingKeys.Keybinds.ToolSelect,
		CreatorSettingKeys.Keybinds.ToolMove,
		CreatorSettingKeys.Keybinds.ToolRotate,
		CreatorSettingKeys.Keybinds.ToolScale,
		CreatorSettingKeys.Keybinds.RotateSelection,
		CreatorSettingKeys.Keybinds.TiltSelection,
		CreatorSettingKeys.Keybinds.ToggleTransformOrientation,
		CreatorSettingKeys.Keybinds.TogglePivotMode,
	];

	public void Init(SettingDef def, ISettingsContext context)
	{
		SettingDef = def;
		SettingsContext = context;
	}

	public override void _Ready()
	{
		_propNameLabel.Text = SettingDef.Label;

		if (IsPressToBindSetting())
		{
			BuildKeybindEditor();
			return;
		}

		Type valueType = SettingDef.ValueType;
		IProperty input;
		if (SettingDef.ControlKind == SettingControlKind.Slider)
		{
			SettingsSliderUI slider = GD.Load<PackedScene>(SliderScenePath).Instantiate<SettingsSliderUI>();
			slider.Configure(SettingDef);
			input = slider;
		}
		else
		{
			input = Globals.LoadProperty(valueType);
		}

		input.PropertyType = valueType;
		_propContainer.AddChild((Node)input);

		if (input is SingleProperty sp && SettingDef.UntypedMinValue != null && SettingDef.UntypedMaxValue != null)
		{
			sp.MinValue = Convert.ToSingle(SettingDef.UntypedMinValue);
			sp.MaxValue = Convert.ToSingle(SettingDef.UntypedMaxValue);
			sp.AllowGreater = false;
			sp.AllowLesser = false;
		}
		if (input is Button button)
			button.Alignment = HorizontalAlignment.Right;

		((Control)input).SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		if (SettingDef.Conditions != null)
			Visible = PropertyVisible = false;

		_input = input;
		SettingsContext.Changed += OnExternalChanged;

		Callable.From(() =>
		{
			if (!IsInstanceValid(this))
				return;

			try
			{
				// Visible at start?
				if (SettingDef.Conditions != null)
				{
					Visible = PropertyVisible = SettingDef.Conditions.Any((cond) =>
					{
						object? value = SettingsContext.GetUntyped(cond.Target);
						return cond.UntypedPredicate(value);
					});
				}

				object? currentValue = SettingsContext.GetUntyped(SettingDef.Key);
				input.SetValue(currentValue);

				input.ValueChanged += val =>
				{
					_suppressChanged = true;
					SettingsContext.Set(SettingDef.Key, val!);
					_suppressChanged = false;
				};
			}
			catch (Exception e)
			{
				BV.PrintErr($"Failed to initialize settings property UI for '{SettingDef.Key}': {e}");
			}
		}).CallDeferred();
	}

	public override void _ExitTree()
	{
		SettingsContext?.Changed -= OnExternalChanged;
		base._ExitTree();
	}

	private bool IsPressToBindSetting()
	{
		return SettingDef.ValueType == typeof(string)
			&& (SettingDef.SectionKey.StartsWith("keybinds") || KeybindSettingKeys.Contains(SettingDef.Key));
	}

	private void BuildKeybindEditor()
	{
		SettingsContext.Changed += OnExternalChanged;

		HBoxContainer row = new()
		{
			AnchorsPreset = (int)Control.LayoutPreset.FullRect,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};

		Button bindButton = new()
		{
			Text = FormatKeybindDisplay(SettingsContext.GetUntyped(SettingDef.Key)?.ToString() ?? ""),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			TooltipText = "Click to press a new key",
		};

		Button resetButton = new()
		{
			Text = "Reset",
			CustomMinimumSize = new Vector2(72, 0),
		};

		bindButton.Pressed += () =>
		{
			if (CreatorService.Interface == null)
				return;

			CreatorService.Interface.PromptBindKey(code =>
			{
				string stored = ((long)code).ToString();
				_suppressChanged = true;
				SettingsContext.Set(SettingDef.Key, stored);
				_suppressChanged = false;
				bindButton.Text = FormatKeybindDisplay(stored);
			});
		};

		resetButton.Pressed += () =>
		{
			string defaultValue = SettingDef.UntypedDefault?.ToString() ?? string.Empty;
			_suppressChanged = true;
			SettingsContext.Set(SettingDef.Key, defaultValue);
			_suppressChanged = false;
			bindButton.Text = FormatKeybindDisplay(defaultValue);
		};

		row.AddChild(bindButton);
		row.AddChild(resetButton);
		_propContainer.AddChild(row);
		_keybindButton = bindButton;

		if (SettingDef.Conditions != null)
			Visible = PropertyVisible = false;

		Callable.From(() =>
		{
			if (!IsInstanceValid(this))
				return;

			if (SettingDef.Conditions != null)
			{
				Visible = PropertyVisible = SettingDef.Conditions.Any((cond) =>
				{
					object? value = SettingsContext.GetUntyped(cond.Target);
					return cond.UntypedPredicate(value);
				});
			}

			string current = SettingsContext.GetUntyped(SettingDef.Key)?.ToString() ?? string.Empty;
			bindButton.Text = FormatKeybindDisplay(current);
		}).CallDeferred();
	}

	private static string FormatKeybindDisplay(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
			return "Unbound";

		if (long.TryParse(raw, out long numeric))
		{
			if (Enum.IsDefined(typeof(KeyCodeEnum), (int)numeric))
				return ((KeyCodeEnum)numeric).ToString();

			if (Enum.IsDefined(typeof(Key), (int)numeric))
				return ((Key)numeric).ToString();
		}

		if (Enum.TryParse(raw, true, out Key key))
			return key.ToString();

		return raw;
	}

	private void OnExternalChanged(SettingChangedEvent e)
	{
		// Recompute visibility
		if (SettingDef.Conditions != null)
		{
			var match = SettingDef.Conditions.Where(c => c.Target == e.Key);
			if (match.Any())
				Visible = PropertyVisible = match.Any(c => c.UntypedPredicate(e.NewValue));
		}

		if (_suppressChanged || e.Key != SettingDef.Key)
			return;

		if (_keybindButton != null)
		{
			_keybindButton.Text = FormatKeybindDisplay(e.NewValue?.ToString() ?? string.Empty);
			return;
		}

		_input?.SetValue(e.NewValue);
	}
}

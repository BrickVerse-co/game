using BrickVerse.Datamodel;
using BrickVerse.Enums;
using Godot;
using System;
using System.Linq;

namespace BrickVerse.Client.UI;

/// <summary>Runtime UI for settings authored as GameSetting instances.</summary>
public sealed partial class GameSettingsPage : VBoxContainer
{
	public string Category = "Game";

	public override void _Ready()
	{
		SizeFlagsHorizontal = SizeFlags.ExpandFill; AddThemeConstantOverride("separation", 12);
		foreach (GameSetting setting in World.Current!.GameSettings.GetSettings().Where(x => x.Category == Category)) AddChild(CreateRow(setting));
		base._Ready();
	}

	private static Control CreateRow(GameSetting setting)
	{
		PanelContainer panel = new(); HBoxContainer row = new(); panel.AddChild(row);
		VBoxContainer copy = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddChild(copy);
		Label title = new() { Text = setting.Title }; title.AddThemeFontSizeOverride("font_size", 24); copy.AddChild(title);
		if (!string.IsNullOrWhiteSpace(setting.Description)) copy.AddChild(new Label { Text = setting.Description, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(0.7f, 0.7f, 0.7f) });
		Control field = CreateField(setting); field.CustomMinimumSize = new Vector2(220, 0); row.AddChild(field); return panel;
	}

	private static Control CreateField(GameSetting setting) => setting.SettingType switch
	{
		GameSetting.SettingTypeEnum.Boolean => CreateBoolean(setting),
		GameSetting.SettingTypeEnum.Number => CreateNumber(setting),
		GameSetting.SettingTypeEnum.Text => CreateText(setting),
		GameSetting.SettingTypeEnum.Choice => CreateChoice(setting),
		GameSetting.SettingTypeEnum.Keybind => new GameKeybindField { Setting = setting },
		GameSetting.SettingTypeEnum.Color => CreateColor(setting),
		_ => new Label { Text = "Unsupported setting" }
	};

	private static Control CreateBoolean(GameSetting s)
	{
		CheckButton field = new() { ButtonPressed = s.GetBoolean() }; field.Toggled += s.SetBoolean; return field;
	}
	private static Control CreateNumber(GameSetting s)
	{
		HBoxContainer box = new(); HSlider slider = new() { MinValue = s.Minimum, MaxValue = s.Maximum, Step = s.Step, Value = s.GetNumber(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		Label value = new() { Text = slider.Value.ToString("0.###"), CustomMinimumSize = new Vector2(58, 0), HorizontalAlignment = HorizontalAlignment.Right };
		slider.ValueChanged += x => { value.Text = x.ToString("0.###"); s.SetNumber((float)x); }; box.AddChild(slider); box.AddChild(value); return box;
	}
	private static Control CreateText(GameSetting s)
	{
		LineEdit field = new() { Text = s.GetText(), MaxLength = Math.Max(0, s.MaxTextLength) }; field.TextChanged += s.SetText; return field;
	}
	private static Control CreateChoice(GameSetting s)
	{
		OptionButton field = new(); string current = s.GetChoice();
		for (int i = 0; i < s.Choices.Length; i++) { field.AddItem(s.Choices[i]); if (s.Choices[i] == current) field.Select(i); }
		field.ItemSelected += i => { if (i >= 0 && i < s.Choices.Length) s.SetChoice(s.Choices[i]); }; return field;
	}
	private static Control CreateColor(GameSetting s)
	{
		ColorPickerButton field = new() { Color = s.GetColor() }; field.ColorChanged += s.SetColor; return field;
	}

	private sealed partial class GameKeybindField : Button
	{
		public GameSetting Setting = null!;
		private bool _listening;
		public override void _Ready() { Text = Setting.GetKeybind().ToString(); Pressed += () => { _listening = true; Text = "Press a key…"; }; base._Ready(); }
		public override void _UnhandledKeyInput(InputEvent input)
		{
			if (!_listening || input is not InputEventKey { Pressed: true, Echo: false } key) return;
			if (key.Keycode == Key.Escape) { _listening = false; Text = Setting.GetKeybind().ToString(); GetViewport().SetInputAsHandled(); return; }
			if (Enum.TryParse(key.Keycode.ToString(), out KeyCodeEnum code)) { Setting.SetKeybind(code); Text = code.ToString(); }
			_listening = false; GetViewport().SetInputAsHandled();
		}
	}
}

using BrickVerse.Attributes;
using BrickVerse.Enums;
using BrickVerse.Scripting;
using Godot;
using System;
using System.Linq;

namespace BrickVerse.Datamodel.Services;

/// <summary>Owns developer-defined game settings and their local, per-universe values.</summary>
[Static("GameSettings")]
public sealed partial class GameSettingsService : Instance
{
	private readonly ConfigFile _config = new();
	private string Section => $"universe_{Root.UniverseID}";
	[ScriptProperty] public BVSignal<GameSetting, object> SettingChanged { get; private set; } = new();

	public override void Ready()
	{
		base.Ready();
		if (!Root.Network.IsServer) _config.Load("user://game-settings.cfg");
	}

	[ScriptMethod]
	public GameSetting[] GetSettings() => GetChildrenOfClass<GameSetting>().Where(x => x.Enabled).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Title).ToArray();

	[ScriptMethod] public GameSetting? GetSetting(string key) => GetChildrenOfClass<GameSetting>().FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
	internal bool GetBoolean(GameSetting s) => Get(s, s.DefaultBoolean).AsBool();
	internal float GetNumber(GameSetting s) => Get(s, s.DefaultNumber).AsSingle();
	internal string GetText(GameSetting s) => Get(s, s.DefaultText).AsString();
	internal string GetChoice(GameSetting s) { string value = Get(s, s.DefaultChoice).AsString(); return s.Choices.Length == 0 || s.Choices.Contains(value) ? value : s.DefaultChoice; }
	internal KeyCodeEnum GetKeybind(GameSetting s) => (KeyCodeEnum)Get(s, (int)s.DefaultKeybind).AsInt32();
	internal Color GetColor(GameSetting s) => Get(s, s.DefaultColor).AsColor();
	internal void SetBoolean(GameSetting s, bool value) => Set(s, value);
	internal void SetNumber(GameSetting s, float value) => Set(s, Mathf.Snapped(Mathf.Clamp(value, s.Minimum, s.Maximum), s.Step));
	internal void SetText(GameSetting s, string value) => Set(s, (value ?? "")[..Math.Min(value?.Length ?? 0, Math.Max(0, s.MaxTextLength))]);
	internal void SetChoice(GameSetting s, string value) { if (s.Choices.Contains(value)) Set(s, value); }
	internal void SetKeybind(GameSetting s, KeyCodeEnum value) => Set(s, (int)value);
	internal void SetColor(GameSetting s, Color value) => Set(s, value);

	internal void Reset(GameSetting s)
	{
		_config.EraseSectionKey(Section, StorageKey(s)); _config.Save("user://game-settings.cfg");
		object value = s.SettingType switch { GameSetting.SettingTypeEnum.Boolean => s.DefaultBoolean, GameSetting.SettingTypeEnum.Number => s.DefaultNumber, GameSetting.SettingTypeEnum.Text => s.DefaultText, GameSetting.SettingTypeEnum.Choice => s.DefaultChoice, GameSetting.SettingTypeEnum.Keybind => s.DefaultKeybind, _ => s.DefaultColor };
		s.InvokeChanged(value); SettingChanged.Invoke(s, value);
	}

	private Variant Get(GameSetting setting, Variant fallback) => _config.GetValue(Section, StorageKey(setting), fallback);
	private void Set(GameSetting setting, Variant value)
	{
		if (Root.Network.IsServer) return;
		_config.SetValue(Section, StorageKey(setting), value); _config.Save("user://game-settings.cfg");
		object boxed = value.Obj ?? value; setting.InvokeChanged(boxed); SettingChanged.Invoke(setting, boxed);
	}
	private static string StorageKey(GameSetting setting) => string.IsNullOrWhiteSpace(setting.Key) ? setting.Name : setting.Key.Trim();
}

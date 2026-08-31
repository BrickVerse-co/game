// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace BrickVerse.Datamodel.Services;

[Static("Localization"), ExplorerExclude, SaveIgnore]
public sealed partial class LocalizationService : Instance
{
	private readonly Dictionary<string, Dictionary<string, string>> _translations = [];

	[ScriptProperty] public string CurrentLocale { get; private set; } = "en-US";

	public override void Init()
	{
		base.Init();
		CurrentLocale = OS.GetLocale().Replace('_', '-');
		Reload();
	}

	[ScriptMethod]
	public string Translate(string key, string locale = "")
	{
		string requested = string.IsNullOrWhiteSpace(locale) ? CurrentLocale : locale.Replace('_', '-');
		if (TryValue(requested, key, out string? value)) return value!;
		int separator = requested.IndexOf('-');
		if (separator > 0 && TryValue(requested[..separator], key, out value)) return value!;
		if (TryValue("en-US", key, out value) || TryValue("en", key, out value)) return value!;
		return key;
	}

	[ScriptMethod]
	public bool HasTranslation(string key, string locale = "") => Translate(key, locale) != key;

	[ScriptMethod]
	public void Reload()
	{
		_translations.Clear();
		const string path = "res://localization.json";
		if (!Godot.FileAccess.FileExists(path)) return;
		using Godot.FileAccess file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
		Dictionary<string, Dictionary<string, string>>? loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(file.GetAsText());
		if (loaded == null) return;
		foreach ((string locale, Dictionary<string, string> entries) in loaded) _translations[locale.Replace('_', '-')] = entries;
	}

	private bool TryValue(string locale, string key, out string? value)
	{
		value = null;
		return _translations.TryGetValue(locale, out Dictionary<string, string>? entries) && entries.TryGetValue(key, out value);
	}
}

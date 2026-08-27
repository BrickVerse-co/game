// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Providers.Datastore;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Generic;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class CreatorDataToolsWindow : Window
{
	private TabContainer _tabs = null!;
	private Tree _stores = null!;
	private Tree _locales = null!;
	private ItemList _snapshots = null!;
	private RichTextLabel _diff = null!;
	private string _localizationPath = "";
	private int _initialTab;
	private const int StorePageSize = 75;
	private int _storePage;
	private string _storeQuery = "";
	private bool _creatingStoreValue;
	private string _editingStore = "";
	private string _editingKey = "";
	private ConfirmationDialog _valueEditor = null!;
	private ConfirmationDialog _deleteConfirm = null!;

	public static void Open(int tab = 0)
	{
		CreatorDataToolsWindow window = GD.Load<PackedScene>("res://scenes/creator/popups/creator_data_tools.tscn").Instantiate<CreatorDataToolsWindow>();
		window._initialTab = tab;
		CreatorService.Interface.GetTree().Root.AddChild(window);
		window.PopupCentered(new Vector2I(920, 620));
	}

	public override void _Ready()
	{
		CloseRequested += QueueFree;
		_tabs = GetNode<TabContainer>("Surface/Margin/Tabs");
		_stores = GetNode<Tree>("Surface/Margin/Tabs/Datastore/Layout/Stores");
		_locales = GetNode<Tree>("Surface/Margin/Tabs/Localization/Layout/Locales");
		_snapshots = GetNode<ItemList>("Surface/Margin/Tabs/History/Layout/Split/Snapshots");
		_diff = GetNode<RichTextLabel>("Surface/Margin/Tabs/History/Layout/Split/Diff");
		_stores.SetColumnTitle(0, "Store"); _stores.SetColumnTitle(1, "Key / test player"); _stores.SetColumnTitle(2, "Type"); _stores.SetColumnTitle(3, "Value preview");
		_locales.SetColumnTitle(0, "Locale"); _locales.SetColumnTitle(1, "Key"); _locales.SetColumnTitle(2, "Translation");
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Toolbar/Refresh").Pressed += RefreshStores;
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Toolbar/Edit").Pressed += EditStoreValue;
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Toolbar/Add").Pressed += AddStoreValue;
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Toolbar/Delete").Pressed += ConfirmDeleteStoreValue;
		GetNode<LineEdit>("Surface/Margin/Tabs/Datastore/Layout/Toolbar/Search").TextChanged += query => { _storeQuery = query.Trim(); _storePage = 0; RefreshStores(); };
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Pager/Previous").Pressed += () => { _storePage = Math.Max(0, _storePage - 1); RefreshStores(); };
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Pager/Next").Pressed += () => { _storePage++; RefreshStores(); };
		_valueEditor = GetNode<ConfirmationDialog>("ValueEditor");
		_deleteConfirm = GetNode<ConfirmationDialog>("DeleteConfirm");
		_valueEditor.Confirmed += SaveStoreValue;
		_deleteConfirm.Confirmed += DeleteStoreValue;
		GetNode<Button>("Surface/Margin/Tabs/Localization/Layout/Toolbar/Add").Pressed += AddLocaleRow;
		GetNode<Button>("Surface/Margin/Tabs/Localization/Layout/Toolbar/Save").Pressed += SaveLocales;
		GetNode<Button>("Surface/Margin/Tabs/Localization/Layout/Toolbar/Reload").Pressed += LoadLocales;
		_snapshots.ItemSelected += ShowSnapshotDiff;
		RefreshStores(); LoadLocales(); RefreshSnapshots();
		_tabs.CurrentTab = Mathf.Clamp(_initialTab, 0, _tabs.GetTabCount() - 1);
	}

	private void RefreshStores()
	{
		_stores.Clear(); TreeItem root = _stores.CreateItem();
		var rows = LocalDatastoreProvider.GetPersistedStores()
			.SelectMany(store => store.Value.Select(entry => new { Store = store.Key, Key = entry.Key, Value = entry.Value }))
			.Where(row => _storeQuery.Length == 0
				|| row.Store.Contains(_storeQuery, StringComparison.OrdinalIgnoreCase)
				|| row.Key.Contains(_storeQuery, StringComparison.OrdinalIgnoreCase)
				|| SerializeValue(row.Value, false).Contains(_storeQuery, StringComparison.OrdinalIgnoreCase))
			.OrderBy(row => row.Store).ThenBy(row => row.Key).ToList();
		int pageCount = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)StorePageSize));
		_storePage = Math.Clamp(_storePage, 0, pageCount - 1);
		foreach (var row in rows.Skip(_storePage * StorePageSize).Take(StorePageSize))
		{
			TreeItem item = root.CreateChild();
			item.SetText(0, row.Store); item.SetText(1, row.Key); item.SetText(2, ValueType(row.Value)); item.SetText(3, SerializeValue(row.Value, false));
			item.SetTooltipText(3, SerializeValue(row.Value, true)); item.SetMetadata(0, row.Store); item.SetMetadata(1, row.Key);
		}
		GetNode<Label>("Surface/Margin/Tabs/Datastore/Layout/Pager/Count").Text = $"{rows.Count:N0} keys · {StorePageSize} per page";
		GetNode<Label>("Surface/Margin/Tabs/Datastore/Layout/Pager/Page").Text = $"Page {_storePage + 1} / {pageCount}";
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Pager/Previous").Disabled = _storePage == 0;
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Pager/Next").Disabled = _storePage >= pageCount - 1;
	}

	private void EditStoreValue()
	{
		TreeItem? item = _stores.GetSelected(); if (item == null || item.GetMetadata(0).VariantType == Variant.Type.Nil) return;
		_editingStore = item.GetMetadata(0).AsString(); _editingKey = item.GetMetadata(1).AsString(); _creatingStoreValue = false;
		_valueEditor.Title = $"Edit {_editingStore}/{_editingKey}";
		_valueEditor.GetNode<LineEdit>("Layout/Store").Text = _editingStore;
		_valueEditor.GetNode<LineEdit>("Layout/Store").Editable = false;
		_valueEditor.GetNode<LineEdit>("Layout/Key").Text = _editingKey;
		_valueEditor.GetNode<LineEdit>("Layout/Key").Editable = false;
		object? value = LocalDatastoreProvider.GetPersistedStores()[_editingStore][_editingKey];
		_valueEditor.GetNode<TextEdit>("Layout/Value").Text = SerializeValue(value, true);
		_valueEditor.PopupCentered();
	}

	private void AddStoreValue()
	{
		_creatingStoreValue = true; _editingStore = ""; _editingKey = ""; _valueEditor.Title = "Create data store key";
		LineEdit store = _valueEditor.GetNode<LineEdit>("Layout/Store"); store.Editable = true; store.Text = "Default";
		LineEdit key = _valueEditor.GetNode<LineEdit>("Layout/Key"); key.Editable = true; key.Text = "";
		_valueEditor.GetNode<TextEdit>("Layout/Value").Text = "{\n  \"value\": true\n}";
		_valueEditor.PopupCentered(); key.GrabFocus();
	}

	private void SaveStoreValue()
	{
		try
		{
			string store = _valueEditor.GetNode<LineEdit>("Layout/Store").Text.Trim();
			string key = _valueEditor.GetNode<LineEdit>("Layout/Key").Text.Trim();
			if (store.Length == 0 || key.Length == 0) throw new InvalidOperationException("Store and key are required.");
			object? value = ParseJson(_valueEditor.GetNode<TextEdit>("Layout/Value").Text);
			LocalDatastoreProvider.SetPersistedValue(store, key, value); RefreshStores();
		}
		catch (Exception ex) { OS.Alert(ex.Message, "Invalid datastore value"); }
	}

	private void ConfirmDeleteStoreValue()
	{
		TreeItem? item = _stores.GetSelected(); if (item == null || item.GetMetadata(0).VariantType == Variant.Type.Nil) return;
		_editingStore = item.GetMetadata(0).AsString(); _editingKey = item.GetMetadata(1).AsString();
		_deleteConfirm.DialogText = $"Delete {_editingStore}/{_editingKey}? This cannot be undone."; _deleteConfirm.PopupCentered();
	}

	private void DeleteStoreValue() { LocalDatastoreProvider.DeletePersistedValue(_editingStore, _editingKey); RefreshStores(); }


	private void AddLocaleRow()
	{
		TreeItem root = _locales.GetRoot() ?? _locales.CreateItem(); TreeItem row = root.CreateChild();
		for (int i = 0; i < 3; i++) row.SetEditable(i, true); row.SetText(0, "en-US"); row.SetText(1, "new.key"); row.SetText(2, "Text");
	}

	private void LoadLocales()
	{
		_locales.Clear(); TreeItem root = _locales.CreateItem(); CreatorSession? session = CreatorService.CurrentSession; if (session == null) return;
		_localizationPath = Path.Combine(session.ProjectFolderPath, "localization.json"); if (!File.Exists(_localizationPath)) return;
		Dictionary<string, Dictionary<string, string>> data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(_localizationPath)) ?? [];
		foreach ((string locale, Dictionary<string, string> entries) in data) foreach ((string key, string value) in entries) { TreeItem row = root.CreateChild(); row.SetText(0, locale); row.SetText(1, key); row.SetText(2, value); for (int i = 0; i < 3; i++) row.SetEditable(i, true); }
	}

	private void SaveLocales()
	{
		if (string.IsNullOrWhiteSpace(_localizationPath)) return; Dictionary<string, Dictionary<string, string>> data = [];
		for (TreeItem? row = _locales.GetRoot()?.GetFirstChild(); row != null; row = row.GetNext()) { string locale = row.GetText(0).Trim(); string key = row.GetText(1).Trim(); if (locale.Length == 0 || key.Length == 0) continue; if (!data.TryGetValue(locale, out Dictionary<string, string>? entries)) data[locale] = entries = []; entries[key] = row.GetText(2); }
		Directory.CreateDirectory(Path.GetDirectoryName(_localizationPath)!); File.WriteAllText(_localizationPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
	}


	private void RefreshSnapshots()
	{
		_snapshots.Clear(); CreatorSession? session = CreatorService.CurrentSession; if (session == null) return; string path = Path.Combine(session.BVProjectFolderPath, "backups"); if (!Directory.Exists(path)) return;
		foreach (string dir in Directory.GetDirectories(path).OrderByDescending(value => value)) { int index = _snapshots.AddItem(Path.GetFileName(dir)); _snapshots.SetItemMetadata(index, dir); }
	}

	private void ShowSnapshotDiff(long index)
	{
		CreatorSession? session = CreatorService.CurrentSession; if (session == null) return; string snapshot = _snapshots.GetItemMetadata((int)index).AsString(); List<string> lines = [];
		string[] oldFiles = Directory.GetFiles(snapshot, "*", SearchOption.AllDirectories); HashSet<string> seen = [];
		foreach (string oldFile in oldFiles) { string relative = Path.GetRelativePath(snapshot, oldFile); seen.Add(relative); string current = Path.Combine(session.ProjectFolderPath, relative); if (!File.Exists(current)) lines.Add($"[color=#ed5c5c]Deleted[/color] {relative}"); else if (!HashesMatch(oldFile, current)) lines.Add($"[color=#f0b84b]Modified[/color] {relative}"); }
		foreach (string current in Directory.GetFiles(session.ProjectFolderPath, "*", SearchOption.AllDirectories).Where(path => !path.StartsWith(session.BVProjectFolderPath))) { string relative = Path.GetRelativePath(session.ProjectFolderPath, current); if (!seen.Contains(relative)) lines.Add($"[color=#35c978]Added[/color] {relative}"); }
		_diff.Text = $"[font_size=20]Changes since {Path.GetFileName(snapshot)}[/font_size]\n\n" + (lines.Count == 0 ? "No scene or project-file changes." : string.Join("\n", lines));
	}

	private static bool HashesMatch(string a, string b) => SHA256.HashData(File.ReadAllBytes(a)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(b)));
	private static string SerializeValue(object? value, bool indented) => JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = indented });
	private static string ValueType(object? value) => value switch { null => "null", string => "string", bool => "boolean", byte or short or int or long or float or double or decimal => "number", System.Collections.IDictionary => "object", System.Collections.IEnumerable => "array", _ => value.GetType().Name };
	private static object? ParseJson(string json) { using JsonDocument doc = JsonDocument.Parse(json); return ReadJson(doc.RootElement); }
	private static object? ReadJson(JsonElement value) => value.ValueKind switch { JsonValueKind.Null => null, JsonValueKind.String => value.GetString(), JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Number when value.TryGetInt64(out long integer) => integer, JsonValueKind.Number => value.GetDouble(), JsonValueKind.Array => value.EnumerateArray().Select(ReadJson).ToList(), JsonValueKind.Object => value.EnumerateObject().ToDictionary(item => item.Name, item => ReadJson(item.Value), StringComparer.Ordinal), _ => throw new InvalidOperationException("Unsupported JSON value.") };
}

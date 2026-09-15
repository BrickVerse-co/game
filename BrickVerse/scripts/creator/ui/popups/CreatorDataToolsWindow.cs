// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Datamodel;
using BrickVerse.Attributes;
using BrickVerse.Creator.Managers;
using BrickVerse.Shared;
using BrickVerse.Providers.Datastore;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Generic;
using BrickVerse.Creator.LSP.Schemas;
using DatamodelScript = BrickVerse.Datamodel.Script;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class CreatorDataToolsWindow : Window
{
	private TabContainer _tabs = null!;
	private Tree _stores = null!;
	private Tree _locales = null!;
	private Tree _icons = null!;
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
	private ConfirmationDialog _restoreConfirm = null!;
	private Tree _collisionMatrix = null!;
	private Tree _analysis = null!;
	private Tree _scriptActivity = null!;

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
		_icons = GetNode<Tree>("Surface/Margin/Tabs/Icons/Layout/Icons");
		_snapshots = GetNode<ItemList>("Surface/Margin/Tabs/History/Layout/Split/Snapshots");
		_diff = GetNode<RichTextLabel>("Surface/Margin/Tabs/History/Layout/Split/Diff");
		_stores.SetColumnTitle(0, "Store"); _stores.SetColumnTitle(1, "Key / test player"); _stores.SetColumnTitle(2, "Type"); _stores.SetColumnTitle(3, "Value preview");
		_locales.SetColumnTitle(0, "Locale"); _locales.SetColumnTitle(1, "Key"); _locales.SetColumnTitle(2, "Translation");
		_icons.SetColumnTitle(0, "Icon"); _icons.SetColumnTitle(1, "Scope"); _icons.SetColumnTitle(2, "Datamodel target"); _icons.SetColumnTitle(3, "Override file");
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Toolbar/Refresh").Pressed += RefreshStores;
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Toolbar/Edit").Pressed += EditStoreValue;
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Toolbar/Add").Pressed += AddStoreValue;
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Toolbar/Delete").Pressed += ConfirmDeleteStoreValue;
		GetNode<LineEdit>("Surface/Margin/Tabs/Datastore/Layout/Toolbar/Search").TextChanged += query => { _storeQuery = query.Trim(); _storePage = 0; RefreshStores(); };
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Pager/Previous").Pressed += () => { _storePage = Math.Max(0, _storePage - 1); RefreshStores(); };
		GetNode<Button>("Surface/Margin/Tabs/Datastore/Layout/Pager/Next").Pressed += () => { _storePage++; RefreshStores(); };
		_valueEditor = GetNode<ConfirmationDialog>("ValueEditor");
		_deleteConfirm = GetNode<ConfirmationDialog>("DeleteConfirm");
		_restoreConfirm = GetNode<ConfirmationDialog>("RestoreConfirm");
		_valueEditor.Confirmed += SaveStoreValue;
		_deleteConfirm.Confirmed += DeleteStoreValue;
		_restoreConfirm.Confirmed += RestoreSelectedSnapshot;
		GetNode<Button>("Surface/Margin/Tabs/Localization/Layout/Toolbar/Add").Pressed += AddLocaleRow;
		GetNode<Button>("Surface/Margin/Tabs/Localization/Layout/Toolbar/Save").Pressed += SaveLocales;
		GetNode<Button>("Surface/Margin/Tabs/Localization/Layout/Toolbar/Reload").Pressed += LoadLocales;
		GetNode<Button>("Surface/Margin/Tabs/Icons/Layout/Toolbar/Override").Pressed += OverrideSelectedIcon;
		GetNode<Button>("Surface/Margin/Tabs/Icons/Layout/Toolbar/Reset").Pressed += ResetSelectedIcon;
		GetNode<Button>("Surface/Margin/Tabs/Icons/Layout/Toolbar/Reload").Pressed += ReloadIcons;
		GetNode<Button>("Surface/Margin/Tabs/Icons/Layout/Toolbar/Folder").Pressed += OpenIconFolder;
		CreatorIconRegistry.Changed += OnIconsChanged;
		_snapshots.ItemSelected += ShowSnapshotDiff;
		GetNode<Button>("Surface/Margin/Tabs/History/Layout/Toolbar/BackupNow").Pressed += BackupNow;
		GetNode<Button>("Surface/Margin/Tabs/History/Layout/Toolbar/Refresh").Pressed += RefreshSnapshots;
		GetNode<Button>("Surface/Margin/Tabs/History/Layout/Toolbar/Rollback").Pressed += () => { if (_snapshots.IsAnythingSelected()) _restoreConfirm.PopupCentered(); };
		BuildCollisionGroupsTab(); BuildDiagnosticsTab();
		RefreshStores(); LoadLocales(); LoadIcons(); RefreshSnapshots();
		_tabs.CurrentTab = Mathf.Clamp(_initialTab, 0, _tabs.GetTabCount() - 1);
	}

	public override void _ExitTree() => CreatorIconRegistry.Changed -= OnIconsChanged;

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

	private void LoadIcons()
	{
		_icons.Clear();
		TreeItem root = _icons.CreateItem();
		CreatorSession? session = CreatorService.CurrentSession;
		if (session == null) return;
		foreach (Type type in typeof(Instance).Assembly.GetTypes()
			.Where(type => type.IsClass && !type.IsAbstract && typeof(Instance).IsAssignableFrom(type)
				&& !type.IsDefined(typeof(InternalAttribute), false))
			.OrderBy(type => type.Name, StringComparer.Ordinal))
		{
			TreeItem row = root.CreateChild();
			row.SetIcon(0, CreatorIconRegistry.ResolveClass(session, type.Name)); row.SetIconMaxWidth(0, 22);
			row.SetText(1, "Global"); row.SetText(2, type.Name);
			row.SetText(3, CreatorIconRegistry.GetClassOverride(session, type.Name) ?? "Built in");
			row.SetMetadata(0, $"class:{type.Name}");
		}
		World? world = World.Current;
		if (world == null || world.LinkedSession != session) return;
		foreach (Instance instance in new[] { world }.Concat(world.GetDescendants())
			.OrderBy(instance => instance.LuaPath, StringComparer.Ordinal))
		{
			string? overridePath = CreatorIconRegistry.GetInstanceOverride(instance);
			TreeItem row = root.CreateChild();
			row.SetIcon(0, CreatorIconRegistry.Resolve(instance)); row.SetIconMaxWidth(0, 22);
			row.SetText(1, "Individual"); row.SetText(2, instance.LuaPath);
			row.SetText(3, overridePath ?? $"Inherits {instance.ClassName}");
			row.SetMetadata(0, $"instance:{instance.ObjectID}");
		}
	}

	private void ReloadIcons()
	{
		if (CreatorService.CurrentSession is CreatorSession session) CreatorIconRegistry.Reload(session);
	}

	private void OverrideSelectedIcon()
	{
		CreatorSession? session = CreatorService.CurrentSession;
		string key = _icons.GetSelected()?.GetMetadata(0).AsString() ?? "";
		if (session == null || key.Length == 0) return;
		if (key.StartsWith("class:", StringComparison.Ordinal)) CreatorIconRegistry.PromptSetClass(session, key[6..]);
		else if (FindInstance(key) is Instance instance) CreatorIconRegistry.PromptSetInstance(instance);
	}

	private void ResetSelectedIcon()
	{
		CreatorSession? session = CreatorService.CurrentSession;
		string key = _icons.GetSelected()?.GetMetadata(0).AsString() ?? "";
		if (session == null || key.Length == 0) return;
		if (key.StartsWith("class:", StringComparison.Ordinal)) CreatorIconRegistry.ClearClass(session, key[6..]);
		else if (FindInstance(key) is Instance instance) CreatorIconRegistry.ClearInstance(instance);
	}

	private static Instance? FindInstance(string key)
	{
		if (!key.StartsWith("instance:", StringComparison.Ordinal) || World.Current == null) return null;
		string id = key[9..];
		return new[] { World.Current }.Concat(World.Current.GetDescendants()).FirstOrDefault(instance => instance.ObjectID == id);
	}

	private static void OpenIconFolder()
	{
		CreatorSession? session = CreatorService.CurrentSession;
		if (session == null) return;
		Directory.CreateDirectory(Path.Combine(session.BVProjectFolderPath, "icons"));
		OS.ShellShowInFileManager(CreatorIconRegistry.RegistryPath(session));
	}

	private void OnIconsChanged(CreatorSession session)
	{
		if (session == CreatorService.CurrentSession) Callable.From(LoadIcons).CallDeferred();
	}


	private void RefreshSnapshots()
	{
		_snapshots.Clear(); CreatorSession? session = CreatorService.CurrentSession; if (session == null) return; string path = Path.Combine(session.BVProjectFolderPath, "backups"); if (!Directory.Exists(path)) return;
		foreach (string dir in Directory.GetDirectories(path).OrderByDescending(value => value)) { int index = _snapshots.AddItem(Path.GetFileName(dir)); _snapshots.SetItemMetadata(index, dir); }
	}

	private async void BackupNow()
	{
		if (CreatorService.CurrentSession is not CreatorSession session) return;
		await session.SaveBackup(); RefreshSnapshots();
	}

	private async void RestoreSelectedSnapshot()
	{
		CreatorSession? session = CreatorService.CurrentSession;
		int[] selected = _snapshots.GetSelectedItems();
		if (session == null || selected.Length == 0) return;
		try
		{
			await session.SaveBackup();
			string snapshot = _snapshots.GetItemMetadata(selected[0]).AsString();
			await ProjectSnapshotManager.RestoreAsync(snapshot, session.ProjectFolderPath);
			CreatorService.Interface.StatusBar?.SetStatus("Backup restored. Reopen the project to reload every restored world and script.");
			RefreshSnapshots(); ShowSnapshotDiff(selected[0]);
		}
		catch (Exception ex) { OS.Alert(ex.Message, "Could not restore backup"); }
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

	private void BuildCollisionGroupsTab()
	{
		MarginContainer page = new() { Name = "Collision Groups" }; page.AddThemeConstantOverride("margin_left", 12); page.AddThemeConstantOverride("margin_top", 12); page.AddThemeConstantOverride("margin_right", 12); page.AddThemeConstantOverride("margin_bottom", 12); _tabs.AddChild(page);
		VBoxContainer layout = new(); page.AddChild(layout);
		Label title = new() { Text = "Collision Groups Editor" }; title.AddThemeFontSizeOverride("font_size", 23); layout.AddChild(title);
		Label subtitle = new() { Text = "Control which physics layers collide. Changes apply to every object assigned to each layer.", ThemeTypeVariation = "CreatorMutedLabel" }; layout.AddChild(subtitle);
		Button refresh = new() { Text = "Refresh from world", CustomMinimumSize = new Vector2(140, 30) }; refresh.Pressed += RefreshCollisionMatrix; layout.AddChild(refresh);
		_collisionMatrix = new Tree { Columns = 9, ColumnTitlesVisible = true, HideRoot = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; layout.AddChild(_collisionMatrix);
		_collisionMatrix.SetColumnTitle(0, "Group / layer"); for (int i = 1; i <= 8; i++) { _collisionMatrix.SetColumnTitle(i, i.ToString()); _collisionMatrix.SetColumnExpand(i, false); _collisionMatrix.SetColumnCustomMinimumWidth(i, 48); }
		_collisionMatrix.ItemEdited += ApplyCollisionMatrixEdit; RefreshCollisionMatrix();
	}

	private void RefreshCollisionMatrix()
	{
		if (_collisionMatrix == null) return; _collisionMatrix.Clear(); TreeItem root = _collisionMatrix.CreateItem();
		Physical[] physicals = World.Current == null ? [] : World.Current.GetDescendants().OfType<Physical>().ToArray();
		for (int layer = 0; layer < 8; layer++)
		{
			TreeItem row = root.CreateChild(); int members = physicals.Count(item => (item.CollisionLayers & (1u << layer)) != 0); row.SetText(0, $"Layer {layer + 1}  ({members} objects)"); row.SetMetadata(0, layer);
			for (int target = 0; target < 8; target++) { row.SetCellMode(target + 1, TreeItem.TreeCellMode.Check); bool collides = physicals.Where(item => (item.CollisionLayers & (1u << layer)) != 0).DefaultIfEmpty().All(item => item == null || (item.CollisionMask & (1u << target)) != 0); row.SetChecked(target + 1, collides); row.SetEditable(target + 1, true); }
		}
	}

	private void ApplyCollisionMatrixEdit()
	{
		TreeItem? row = _collisionMatrix.GetEdited(); int column = _collisionMatrix.GetEditedColumn(); if (row == null || column < 1 || World.Current == null) return;
		int layer = (int)row.GetMetadata(0); int target = column - 1; bool enabled = row.IsChecked(column);
		foreach (Physical item in World.Current.GetDescendants().OfType<Physical>().Where(item => (item.CollisionLayers & (1u << layer)) != 0)) item.SetCollisionMask(target + 1, enabled);
		foreach (Physical item in World.Current.GetDescendants().OfType<Physical>().Where(item => (item.CollisionLayers & (1u << target)) != 0)) item.SetCollisionMask(layer + 1, enabled);
		RefreshCollisionMatrix();
	}

	private void BuildDiagnosticsTab()
	{
		MarginContainer page = new() { Name = "Diagnostics" }; page.AddThemeConstantOverride("margin_left", 12); page.AddThemeConstantOverride("margin_top", 12); page.AddThemeConstantOverride("margin_right", 12); page.AddThemeConstantOverride("margin_bottom", 12); _tabs.AddChild(page);
		VBoxContainer layout = new(); page.AddChild(layout); HBoxContainer header = new(); layout.AddChild(header);
		Label title = new() { Text = "Script diagnostics", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; title.AddThemeFontSizeOverride("font_size", 23); header.AddChild(title);
		Button refresh = new() { Text = "Refresh", CustomMinimumSize = new Vector2(90, 30) }; refresh.Pressed += RefreshDiagnostics; header.AddChild(refresh);
		TabContainer views = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; layout.AddChild(views);
		_analysis = new Tree { Name = "Analysis", Columns = 4, ColumnTitlesVisible = true, HideRoot = true }; _analysis.SetColumnTitle(0, "Severity"); _analysis.SetColumnTitle(1, "File"); _analysis.SetColumnTitle(2, "Line"); _analysis.SetColumnTitle(3, "Message"); _analysis.SetColumnExpand(0, false); _analysis.SetColumnCustomMinimumWidth(0, 100); _analysis.SetColumnExpand(2, false); _analysis.SetColumnCustomMinimumWidth(2, 60); _analysis.ItemActivated += OpenDiagnostic; views.AddChild(_analysis);
		_scriptActivity = new Tree { Name = "Script Activity", Columns = 4, ColumnTitlesVisible = true, HideRoot = true }; _scriptActivity.SetColumnTitle(0, "Script"); _scriptActivity.SetColumnTitle(1, "Type"); _scriptActivity.SetColumnTitle(2, "Source"); _scriptActivity.SetColumnTitle(3, "Location"); views.AddChild(_scriptActivity);
		RefreshDiagnostics();
	}

	private void RefreshDiagnostics()
	{
		if (_analysis == null || _scriptActivity == null) return; _analysis.Clear(); _scriptActivity.Clear(); TreeItem analysisRoot = _analysis.CreateItem(); TreeItem activityRoot = _scriptActivity.CreateItem(); CreatorSession? session = CreatorService.CurrentSession;
		if (session?.LuaCompletion != null) foreach ((string file, List<LspDiagnostic> diagnostics) in session.LuaCompletion.Diagnostics) foreach (LspDiagnostic diagnostic in diagnostics)
		{
			TreeItem row = analysisRoot.CreateChild(); int severity = diagnostic.Severity ?? 3; row.SetText(0, severity switch { 1 => "Error", 2 => "Warning", 4 => "Hint", _ => "Information" }); row.SetText(1, Path.GetRelativePath(session.ProjectFolderPath, file)); row.SetText(2, (diagnostic.Range.Start.Line + 1).ToString()); row.SetText(3, diagnostic.Message); row.SetMetadata(0, file); row.SetMetadata(1, diagnostic.Range.Start.Line + 1);
		}
		if (World.Current != null) foreach (DatamodelScript script in World.Current.GetDescendants().OfType<DatamodelScript>().OrderBy(script => script.LuaPath)) { TreeItem row = activityRoot.CreateChild(); row.SetText(0, script.Name); row.SetText(1, script.ClassName); row.SetText(2, $"{script.Source.Length:N0} characters"); row.SetText(3, script.LuaPath); }
	}

	private void OpenDiagnostic()
	{
		TreeItem? row = _analysis.GetSelected(); if (row == null) return; string path = row.GetMetadata(0).AsString(); int line = (int)row.GetMetadata(1); if (CreatorService.CurrentSession is CreatorSession session) CreatorService.OpenFile(Path.GetRelativePath(session.ProjectFolderPath, path), line);
	}
	private static string SerializeValue(object? value, bool indented) => JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = indented });
	private static string ValueType(object? value) => value switch { null => "null", string => "string", bool => "boolean", byte or short or int or long or float or double or decimal => "number", System.Collections.IDictionary => "object", System.Collections.IEnumerable => "array", _ => value.GetType().Name };
	private static object? ParseJson(string json) { using JsonDocument doc = JsonDocument.Parse(json); return ReadJson(doc.RootElement); }
	private static object? ReadJson(JsonElement value) => value.ValueKind switch { JsonValueKind.Null => null, JsonValueKind.String => value.GetString(), JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Number when value.TryGetInt64(out long integer) => integer, JsonValueKind.Number => value.GetDouble(), JsonValueKind.Array => value.EnumerateArray().Select(ReadJson).ToList(), JsonValueKind.Object => value.EnumerateObject().ToDictionary(item => item.Name, item => ReadJson(item.Value), StringComparer.Ordinal), _ => throw new InvalidOperationException("Unsupported JSON value.") };
}

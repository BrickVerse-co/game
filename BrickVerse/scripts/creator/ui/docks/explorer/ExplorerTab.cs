// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Datamodel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BrickVerse.Creator.UI;

public partial class ExplorerTab : Control
{
	public World Root = null!;
	[Export] public ExplorerTree Tree = null!;
	[Export] private LineEdit _searchEdit = null!;
	[Export] private MenuButton _filterButton = null!;
	private static bool _expandHierarchyOnSelection = true;
	private static readonly List<string> SearchHistory = [];
	private const string SearchHistoryPath = "user://creator/explorer_search_history.cfg";
	private static readonly string[] SuggestedFilters =
	[
		"class:", "tag:", "Name=\"\"", "Locked=true", "Anchored=true",
		"Transparency>0", "Material=\"\"", "MeshId=\"\"", "TextureId=\"\""
	];
	private static readonly string[] SuggestedFilterIcons =
	[
		"cube.svg", "flag.svg", "edit.svg", "lock.svg", "link.svg",
		"eye.svg", "brick.svg", "cube.svg", "image-square.svg"
	];
	private static readonly Regex QueryPartRegex = new(
		"""(?<key>[\w.]+)\s*(?<op>!=|>=|<=|=|>|<|~|:)\s*(?<value>"[^"]*"|'[^']*'|\S+)|(?<bare>"[^"]*"|\S+)""",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private bool _updateSearchDirty = false;
	private bool _filterMenuConnected;

	public override void _Ready()
	{
		_searchEdit.TextChanged += OnSearch;
		_searchEdit.TextSubmitted += SaveSearch;
		LoadSearchHistory();
		BuildFilterMenu();
		base._Ready();
	}

	private void BuildFilterMenu()
	{
		PopupMenu popup = _filterButton.GetPopup();
		popup.Clear();
		popup.AddItem("Suggested filters", 900);
		popup.SetItemDisabled(popup.GetItemIndex(900), true);
		for (int i = 0; i < SuggestedFilters.Length; i++)
		{
			Texture2D? icon = GD.Load<Texture2D>(
				$"res://assets/textures/ui-icons/{SuggestedFilterIcons[i]}"
			);
			popup.AddIconItem(icon, SuggestedFilters[i], i);
		}
		if (SearchHistory.Count > 0)
		{
			popup.AddSeparator("Recent searches");
			for (int i = 0; i < SearchHistory.Count; i++) popup.AddItem(SearchHistory[i], 100 + i);
			popup.AddItem("Clear search history", 199);
		}
		popup.AddSeparator();
		popup.AddItem("Regex: /pattern/", 901);
		popup.SetItemDisabled(popup.GetItemIndex(901), true);
		popup.AddSeparator();
		popup.AddCheckItem("Expand hierarchy when selecting", 1000);
		popup.SetItemChecked(popup.GetItemIndex(1000), _expandHierarchyOnSelection);
		if (!_filterMenuConnected)
		{
			popup.IdPressed += OnFilterMenuPressed;
			_filterMenuConnected = true;
		}
	}

	private void OnFilterMenuPressed(long id)
	{
		PopupMenu popup = _filterButton.GetPopup();
			if (id >= 0 && id < SuggestedFilters.Length)
			{
				string separator = _searchEdit.Text.Length == 0 || _searchEdit.Text.EndsWith(' ') ? "" : " ";
				string suggestion = SuggestedFilters[(int)id];
				_searchEdit.Text += separator + suggestion;
				_searchEdit.CaretColumn = _searchEdit.Text.Length - (suggestion.EndsWith("\"\"") ? 1 : 0);
				_searchEdit.GrabFocus();
			}
			else if (id == 1000)
			{
				_expandHierarchyOnSelection = !_expandHierarchyOnSelection;
				popup.SetItemChecked(popup.GetItemIndex(1000), _expandHierarchyOnSelection);
			}
			else if (id >= 100 && id < 100 + SearchHistory.Count)
			{
				_searchEdit.Text = SearchHistory[(int)id - 100];
				_searchEdit.CaretColumn = _searchEdit.Text.Length;
				_searchEdit.GrabFocus();
			}
			else if (id == 199)
			{
				SearchHistory.Clear();
				SaveSearchHistoryFile();
				BuildFilterMenu();
			}
	}

	private void SaveSearch(string query)
	{
		query = query.Trim();
		if (query.Length == 0) return;
		SearchHistory.RemoveAll(value => value.Equals(query, StringComparison.OrdinalIgnoreCase));
		SearchHistory.Insert(0, query);
		if (SearchHistory.Count > 12) SearchHistory.RemoveRange(12, SearchHistory.Count - 12);
		SaveSearchHistoryFile();
		BuildFilterMenu();
	}

	private static void LoadSearchHistory()
	{
		if (SearchHistory.Count > 0) return;
		ConfigFile config = new();
		if (config.Load(SearchHistoryPath) != Error.Ok) return;
		foreach (string value in config.GetValue("history", "queries", Array.Empty<string>()).AsStringArray())
			if (!string.IsNullOrWhiteSpace(value)) SearchHistory.Add(value);
	}

	private static void SaveSearchHistoryFile()
	{
		ConfigFile config = new();
		config.SetValue("history", "queries", SearchHistory.ToArray());
		config.Save(SearchHistoryPath);
	}

	private void OnSearch(string newText)
	{
		QueueSearch();
	}

	private void QueueSearch()
	{
		_updateSearchDirty = true;
	}

	public override void _Process(double delta)
	{
		if (_updateSearchDirty)
		{
			_updateSearchDirty = false;
			Search();
		}
		base._Process(delta);
	}

	private void Search()
	{
		string query = _searchEdit.Text.Trim();
		bool isFirst = true;

		foreach (TreeItem item in Tree.InstanceToItem.Values)
		{
			if ((bool)item.GetMeta("_force_invisible", false)) continue;
			item.Deselect(0);
		}

		foreach ((Instance i, TreeItem item) in Tree.InstanceToItem)
		{
			if ((bool)item.GetMeta("_force_invisible", false)) continue;

			if (MatchesQuery(i, query))
			{
				item.Visible = true;
				RevealParents(item); // Always reveal parents

				if (isFirst)
				{
					isFirst = false;
					if (_expandHierarchyOnSelection) ExpandParents(item);
					item.Select(0);
					Tree.ScrollToItem(item);
				}
			}
			else
			{
				item.Visible = false;
			}
		}

		if (string.IsNullOrEmpty(query))
		{
			foreach ((_, TreeItem item) in Tree.InstanceToItem)
			{
				Explorer.RefreshTreeItemVisibility(item);
			}
		}
	}

	private static bool MatchesQuery(Instance instance, string query)
	{
		if (query.Length == 0) return true;
		if (query.Length > 2 && query[0] == '/' && query[^1] == '/')
		{
			try { return Regex.IsMatch(SearchText(instance), query[1..^1], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
			catch (ArgumentException) { return false; }
		}

		query = Regex.Replace(query, @"([\w.]+)\s+(?:like|contains)\s+", "$1~", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		MatchCollection parts = QueryPartRegex.Matches(query);
		if (parts.Count == 0) return SearchText(instance).Contains(query, StringComparison.OrdinalIgnoreCase);
		foreach (Match part in parts)
		{
			if (part.Groups["bare"].Success)
			{
				string bare = Unquote(part.Groups["bare"].Value);
				if (!SearchText(instance).Contains(bare, StringComparison.OrdinalIgnoreCase)) return false;
				continue;
			}
			string key = part.Groups["key"].Value;
			string op = part.Groups["op"].Value;
			string expected = Unquote(part.Groups["value"].Value);
			if (!MatchesFilter(instance, key, op, expected)) return false;
		}
		return true;
	}

	private static bool MatchesFilter(Instance instance, string key, string op, string expected)
	{
		if (key.Equals("tag", StringComparison.OrdinalIgnoreCase))
			return op is ":" or "=" or "~" && instance.Tags.Any(tag => CompareText(tag, op, expected));
		if (key.Equals("class", StringComparison.OrdinalIgnoreCase) || key.Equals("type", StringComparison.OrdinalIgnoreCase))
			return CompareText(instance.ClassName, op, expected);
		if (key.Equals("path", StringComparison.OrdinalIgnoreCase)) return CompareText(instance.LuaPath, op, expected);

		PropertyInfo? property = instance.GetType().GetProperty(key,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
		if (property == null || property.GetIndexParameters().Length != 0) return false;
		try { return CompareValue(property.GetValue(instance), op, expected); }
		catch { return false; }
	}

	private static bool CompareValue(object? actual, string op, string expected)
	{
		if (actual == null) return op == "=" && expected.Equals("null", StringComparison.OrdinalIgnoreCase)
			|| op == "!=" && !expected.Equals("null", StringComparison.OrdinalIgnoreCase);
		if (actual is System.Collections.IEnumerable sequence and not string)
			return sequence.Cast<object?>().Any(value => CompareValue(value, op, expected));
		string text = actual switch
		{
			bool boolean => boolean ? "true" : "false",
			Instance linked => linked.Name,
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
			_ => actual.ToString() ?? ""
		};
		if ((op is ">" or "<" or ">=" or "<=") && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double left)
			&& double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out double right))
			return op switch { ">" => left > right, "<" => left < right, ">=" => left >= right, "<=" => left <= right, _ => false };
		return CompareText(text, op, expected);
	}

	private static bool CompareText(string actual, string op, string expected) => op switch
	{
		"=" or ":" => actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
		"!=" => !actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
		"~" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
		_ => false
	};

	private static string SearchText(Instance instance) => $"{instance.Name} {instance.ClassName} {instance.LuaPath} {string.Join(' ', instance.Tags)}";
	private static string Unquote(string value) => value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')) ? value[1..^1] : value;

	public void RevealSelection(TreeItem item)
	{
		if (_expandHierarchyOnSelection) ExpandParents(item);
	}

	private static void RevealParents(TreeItem item)
	{
		TreeItem parent = item.GetParent();
		while (parent != null)
		{
			if ((bool)parent.GetMeta("_force_invisible", false)) break;
			parent.Visible = true;
			parent = parent.GetParent();
		}
	}

	private static void ExpandParents(TreeItem item)
	{
		TreeItem parent = item.GetParent();
		while (parent != null)
		{
			parent.Collapsed = false;
			parent = parent.GetParent();
		}
	}
}

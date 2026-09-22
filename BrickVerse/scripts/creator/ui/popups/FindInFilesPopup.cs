// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class FindInFilesPopup : PopupWindowBase
{
	private sealed record Match(string RelativePath, int Line, string Preview);
	private const string ScenePath = "res://scenes/creator/popups/find_in_files.tscn";
	private static readonly HashSet<string> SearchableExtensions = new(StringComparer.OrdinalIgnoreCase)
		{ ".lua", ".luau", ".json", ".md", ".txt", ".csv", ".xml", ".toml", ".cfg" };
	[Export] private LineEdit _query = null!;
	[Export] private LineEdit _replace = null!;
	[Export] private CheckButton _matchCase = null!;
	[Export] private CheckButton _wholeWord = null!;
	[Export] private Button _searchButton = null!;
	[Export] private Button _replaceSelectedButton = null!;
	[Export] private Button _replaceAllButton = null!;
	[Export] private ItemList _results = null!;
	[Export] private Label _status = null!;
	private readonly List<Match> _matches = [];
	private CancellationTokenSource? _searchCancellation;
	private EditorLoadingSkeleton _loading = null!;
	private static FindInFilesPopup? _instance;

	public static void Open()
	{
		if (CreatorService.CurrentSession == null) return;
		if (_instance != null && IsInstanceValid(_instance) && _instance.IsInsideTree() && _instance.Visible)
		{
			_instance.GrabFocus();
			_instance._query.GrabFocus();
			return;
		}
		_instance = null;
		FindInFilesPopup popup = GD.Load<PackedScene>(ScenePath).Instantiate<FindInFilesPopup>();
		_instance = popup;
		CreatorService.Interface.PopupWindow(popup);
	}

	public override void _Ready()
	{
		_searchButton.Pressed += Search;
		_query.TextSubmitted += _ => Search();
		_results.ItemActivated += OpenResult;
		_replaceSelectedButton.Pressed += ReplaceSelected;
		_replaceAllButton.Pressed += ReplaceAll;
		_loading = new EditorLoadingSkeleton(7);
		_loading.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 10);
		_loading.Visible = false;
		_results.AddChild(_loading);
		_query.GrabFocus();
		base._Ready();
	}

	public override void _ExitTree()
	{
		_searchCancellation?.Cancel();
		_searchCancellation?.Dispose();
		if (_instance == this) _instance = null;
		base._ExitTree();
	}

	private async void Search()
	{
		string query = _query.Text;
		CreatorSession? session = CreatorService.CurrentSession;
		if (session == null || string.IsNullOrWhiteSpace(query)) return;
		_searchCancellation?.Cancel();
		_searchCancellation?.Dispose();
		_searchCancellation = new CancellationTokenSource();
		CancellationToken token = _searchCancellation.Token;
		_searchButton.Disabled = true;
		_loading.Visible = true;
		_results.Clear();
		_matches.Clear();
		_status.Text = "Searching project…";
		try
		{
			Match[] matches = await Task.Run(() => Scan(session.ProjectFolderPath, query, _matchCase.ButtonPressed, _wholeWord.ButtonPressed, token), token);
			if (token.IsCancellationRequested || !IsInstanceValid(this)) return;
			_matches.AddRange(matches);
			foreach (Match match in matches)
				_results.AddItem($"{match.RelativePath}:{match.Line}    {match.Preview}");
			_status.Text = matches.Length >= 500 ? "Showing the first 500 matches" : $"{matches.Length} match{(matches.Length == 1 ? "" : "es")}";
		}
		catch (OperationCanceledException) { }
		catch (Exception error) { _status.Text = "Search failed: " + error.Message; }
		finally
		{
			if (IsInstanceValid(this))
			{
				_searchButton.Disabled = false;
				_loading.Visible = false;
			}
		}
	}

	private static Match[] Scan(string projectRoot, string query, bool matchCase, bool wholeWord, CancellationToken token)
	{
		List<Match> matches = [];
		StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
		Regex? wordPattern = wholeWord ? new Regex($@"\b{Regex.Escape(query)}\b", matchCase ? RegexOptions.None : RegexOptions.IgnoreCase) : null;
		foreach (string path in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories))
		{
			token.ThrowIfCancellationRequested();
			string relative = Path.GetRelativePath(projectRoot, path).SanitizePath();
			if (relative.StartsWith(".bvproject/", StringComparison.OrdinalIgnoreCase)
				|| !SearchableExtensions.Contains(Path.GetExtension(path))
				|| new FileInfo(path).Length > 2 * 1024 * 1024) continue;
			int lineNumber = 0;
			foreach (string line in File.ReadLines(path))
			{
				lineNumber++;
				bool found = wordPattern?.IsMatch(line) ?? line.Contains(query, comparison);
				if (!found) continue;
				matches.Add(new(relative, lineNumber, line.Trim().Replace('\t', ' ')));
				if (matches.Count >= 500) return [.. matches];
			}
		}
		return [.. matches];
	}

	private void OpenResult(long index)
	{
		if (index < 0 || index >= _matches.Count) return;
		Match match = _matches[(int)index];
		CreatorService.OpenFile(match.RelativePath, match.Line);
	}

	private void ReplaceSelected()
	{
		int[] selected = _results.GetSelectedItems();
		if (selected.Length == 0 || selected[0] >= _matches.Count) return;
		ReplaceMatches([_matches[selected[0]]]);
	}

	private void ReplaceAll() => ReplaceMatches([.. _matches]);

	private void ReplaceMatches(IEnumerable<Match> matches)
	{
		CreatorSession? session = CreatorService.CurrentSession;
		if (session == null || string.IsNullOrEmpty(_query.Text)) return;
		int changed = 0;
		foreach (IGrouping<string, Match> fileMatches in matches.GroupBy(match => match.RelativePath, StringComparer.OrdinalIgnoreCase))
		{
			string path = Path.GetFullPath(Path.Combine(session.ProjectFolderPath, fileMatches.Key));
			if (!path.StartsWith(Path.GetFullPath(session.ProjectFolderPath), StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
			string[] lines = File.ReadAllLines(path);
			foreach (Match match in fileMatches.OrderByDescending(match => match.Line))
			{
				int line = match.Line - 1; if (line < 0 || line >= lines.Length) continue;
				string replaced = ReplaceText(lines[line], _query.Text, _replace.Text, _matchCase.ButtonPressed, _wholeWord.ButtonPressed);
				if (replaced == lines[line]) continue; lines[line] = replaced; changed++;
			}
			File.WriteAllLines(path, lines);
		}
		_status.Text = $"Replaced {changed} match{(changed == 1 ? "" : "es")}";
		Search();
	}

	private static string ReplaceText(string input, string query, string replacement, bool matchCase, bool wholeWord)
	{
		RegexOptions options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
		string pattern = wholeWord ? $@"\b{Regex.Escape(query)}\b" : Regex.Escape(query);
		return Regex.Replace(input, pattern, _ => replacement, options);
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Scripting;
using System.Collections.Generic;
using System.Text;
using static BrickVerse.Scripting.LogDispatcher;

namespace BrickVerse.Client.UI;

public partial class DebugConsoleTab : Control
{
	private const int MaxLogLength = 16384;
	public const string ErrorColorHex = "#F95D5D";
	public const string WarningColorHex = "#FFBC58";
	public const string ServerColorHex = "#0097FF";
	public const string ClientColorHex = "#F95D5D";

	private readonly StringBuilder _textBuilder = new(MaxLogLength * 100);

	// How many logs from the unfiltered list have been rendered
	private int _lastRenderedIndex = 0;
	private bool _needsFullRebuild = false;
	private bool _hasPendingAppend = false;
	private string _search = "";
	private bool _showInfo = true;
	private bool _showWarnings = true;
	private bool _showErrors = true;

	public List<LogData> Logs = [];
	public HashSet<string> ShownLogs = [];

	[Export] public RichTextLabel TextLabel = null!;
	public LogDispatcher Logger = null!;

	public override void _Ready()
	{
		Logger = CoreUIRoot.Singleton.Root.ScriptService.Logger;
		TextLabel.Text = "";
		Control originalParent = (Control)TextLabel.GetParent();
		originalParent.RemoveChild(TextLabel);
		VBoxContainer layout = new();
		originalParent.AddChild(layout);
		HBoxContainer toolbar = new();
		layout.AddChild(toolbar);
		LineEdit search = new() { PlaceholderText = "Search output…", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		search.TextChanged += value => { _search = value; _needsFullRebuild = true; };
		toolbar.AddChild(search);
		AddFilter(toolbar, "Info", true, value => _showInfo = value);
		AddFilter(toolbar, "Warnings", true, value => _showWarnings = value);
		AddFilter(toolbar, "Errors", true, value => _showErrors = value);
		Button clear = new() { Text = "Clear" };
		clear.Pressed += () => { Logs.Clear(); ShownLogs.Clear(); _needsFullRebuild = true; };
		toolbar.AddChild(clear);
		TextLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
		layout.AddChild(TextLabel);
		Logger.NewLog += OnNewLog;
		Logger.LogSynchronized += OnLogSynchronized;
	}

	private void AddFilter(Control parent, string label, bool enabled, System.Action<bool> setter)
	{
		CheckButton filter = new() { Text = label, ButtonPressed = enabled };
		filter.Toggled += value => { setter(value); _needsFullRebuild = true; };
		parent.AddChild(filter);
	}

	public override void _Process(double delta)
	{
		if (!IsVisibleInTree())
		{
			base._Process(delta);
			return;
		}

		if (_needsFullRebuild)
		{
			FullRebuild();
		}
		else if (_hasPendingAppend)
		{
			AppendPendingLogs();
		}

		base._Process(delta);
	}

	private void OnLogSynchronized(LogData[] logs)
	{
		foreach (LogData item in logs)
		{
			OnNewLog(item);
		}
	}

	private void OnNewLog(LogData data)
	{
		if (ShownLogs.Contains(data.ID)) return;

		ShownLogs.Add(data.ID);

		// Binary search insertion to maintain sorted order
		int index = Logs.BinarySearch(data, Comparer<LogData>.Create((a, b) => a.LoggedAt.CompareTo(b.LoggedAt)));
		if (index < 0) index = ~index;
		Logs.Insert(index, data);

		// Trim old logs if exceeding limit
		if (Logs.Count > MaxLogLength)
		{
			int removeCount = Logs.Count - MaxLogLength;
			for (int i = 0; i < removeCount; i++)
			{
				ShownLogs.Remove(Logs[0].ID);
				Logs.RemoveAt(0);
			}

			// Trimming invalidates _lastRenderedIndex, must start over
			_needsFullRebuild = true;
			_hasPendingAppend = false;
			return;
		}

		// If the log was inserted before the end, full rebuild required
		if (index < Logs.Count - 1)
		{
			_needsFullRebuild = true;
			_hasPendingAppend = false;
			return;
		}

		// Fast path: log appended to the end
		if (IsVisibleInTree())
			AppendSingleLog(data);
		else
			_hasPendingAppend = true;
	}

	private void AppendPendingLogs()
	{
		for (int i = _lastRenderedIndex; i < Logs.Count; i++)
			AppendSingleLog(Logs[i]);

		_hasPendingAppend = false;
	}

	private void AppendSingleLog(LogData item)
	{
		if (!ShouldShow(item)) { _lastRenderedIndex++; return; }
		_textBuilder.Clear();
		BuildLogLine(_textBuilder, item);
		TextLabel.AppendText(_textBuilder.ToString());
		_lastRenderedIndex++;
	}

	private void FullRebuild()
	{
		_textBuilder.Clear();

		foreach (LogData item in Logs)
			if (ShouldShow(item)) BuildLogLine(_textBuilder, item);

		TextLabel.Text = _textBuilder.ToString();
		_lastRenderedIndex = Logs.Count;
		_needsFullRebuild = false;
		_hasPendingAppend = false;
	}

	private bool ShouldShow(LogData item)
	{
		if (item.LogType == LogTypeEnum.Info && !_showInfo) return false;
		if (item.LogType == LogTypeEnum.Warning && !_showWarnings) return false;
		if (item.LogType == LogTypeEnum.Error && !_showErrors) return false;
		return _search.Length == 0
			|| item.Content.Contains(_search, System.StringComparison.OrdinalIgnoreCase)
			|| item.Source.Contains(_search, System.StringComparison.OrdinalIgnoreCase);
	}

	private static void BuildLogLine(StringBuilder sb, LogData item)
	{
		string dotColor = (item.LogFrom == LogFromEnum.Client) ? ClientColorHex : ServerColorHex;

		sb.Append("[color=")
			.Append(dotColor)
			.Append("]•[/color] ");

		if (item.LogType == LogTypeEnum.Error)
			sb.Append("[color=").Append(ErrorColorHex).Append(']');
		else if (item.LogType == LogTypeEnum.Warning)
			sb.Append("[color=").Append(WarningColorHex).Append(']');

		sb.Append('[')
			.Append(item.LoggedAt.ToLongTimeString())
			.Append("] ");

		if (!string.IsNullOrWhiteSpace(item.Source))
			sb.Append('[').Append(item.Source).Append("] ");

		sb
			.Append(item.Content);

		if (item.LogType == LogTypeEnum.Error || item.LogType == LogTypeEnum.Warning)
			sb.Append("[/color]");

		sb.Append('\n');
	}
}

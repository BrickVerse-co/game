// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.LSP;
using BrickVerse.Creator.LSP.Schemas;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using BrickVerse.Shared.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace BrickVerse.Creator.UI.TextEditor;

public partial class TextEditorRoot : Node
{
	public static event Action<string>? ExternalFileChanged;
	public static void NotifyExternalFileChanged(string absolutePath) => ExternalFileChanged?.Invoke(absolutePath);
	private const string CodeCompletionIconPath = "res://assets/textures/creator/tabs/text_editor/code_completion/";
	private const int DiagDelay = 75;

	[Export] public TextEditorField CodeEditor = null!;
	public TextEditorContainer Container = null!;
	public bool Saved = false;

	public event Action<bool>? SavedChanged;

	public void ReloadFromDiskIfCurrent(string absolutePath)
	{
		if (!Path.GetFullPath(Container.TargetFilePathAbsolute).Equals(
			Path.GetFullPath(absolutePath), StringComparison.OrdinalIgnoreCase)) return;
		if (!File.Exists(absolutePath)) return;
		string source = File.ReadAllText(absolutePath);
		if (CodeEditor.Text == source) return;
		CodeEditor.Text = source;
		CodeEditor.ClearUndoHistory();
		_oldText = source;
		Saved = true;
		SavedChanged?.Invoke(true);
		if (_completion != null) _ = _completion.UpdateScriptChangeAsync(absolutePath, source);
	}

	public void OpenInExternalEditor()
	{
		string path = Path.GetFullPath(Container.TargetFilePathAbsolute);
		PreferredEditorEnum editor = CreatorSettingsService.Instance.Get<PreferredEditorEnum>(CreatorSettingKeys.CodeEditor.PreferredEditor);
		try
		{
			if (editor == PreferredEditorEnum.VSCode)
				Process.Start(new ProcessStartInfo("code", $"--goto \"{path}:{CodeEditor.GetCaretLine() + 1}:{CodeEditor.GetCaretColumn() + 1}\"") { UseShellExecute = true });
			else if (editor == PreferredEditorEnum.Zed)
				Process.Start(new ProcessStartInfo("zed", $"\"{path}\"") { UseShellExecute = true });
			else
				OS.ShellOpen(path);
		}
		catch (Exception ex)
		{
			CreatorService.Interface.PopupAlert($"Could not open the external editor: {ex.Message}", "External editor");
		}
	}

	public void GoToLine(int oneBasedLine)
	{
		int line = Mathf.Clamp(oneBasedLine - 1, 0, Mathf.Max(0, CodeEditor.GetLineCount() - 1));
		CodeEditor.SetCaretLine(line);
		CodeEditor.SetCaretColumn(0);
		CodeEditor.CenterViewportToCaret();
		CodeEditor.GrabFocus();
	}

	[Export] private TextEditorFind _finder = null!;
	[Export] private Label _diagLabel = null!;
	[Export] private Label _statusBar = null!;

	public static Color ColorDanger { get; private set; } = Color.FromString("D77C79", Colors.White);
	public static Color ColorOrange { get; private set; } = Color.FromString("E6A472", Colors.White);
	public static Color ColorWarn { get; private set; } = Color.FromString("F4CF86", Colors.White);
	public static Color ColorSuccess { get; private set; } = Color.FromString("C2C77B", Colors.White);
	public static Color ColorPurple { get; private set; } = Color.FromString("C0A7C7", Colors.White);
	public static Color ColorGrey { get; private set; } = Color.FromString("A7A8A7", Colors.White);
	public static Color ColorWhite { get; private set; } = Colors.White;

	private string _oldText = "";
	private CodeHighlighter _highlighter = null!;
	private EditorPalette _palette;
	private LuaCompletionService? _completion = null!;

	private Godot.Timer _autoCompleteTimer = null!;
	private Godot.Timer _autoSaveTimer = null!;
	private Godot.Timer _hoverTimer = null!;
	private PopupPanel _hoverPopup = null!;
	private RichTextLabel _hoverText = null!;
	private Vector2 _hoverPosition;
	private CancellationTokenSource? _hoverCts;
	private CancellationTokenSource? _diagCts;
	private HashSet<string>? _editorPropertySet;
	private readonly Dictionary<int, List<EditorDiagnosticDecoration>> _diagnosticsByLine = [];

	private readonly record struct EditorPalette(
		string Background, string Foreground, string Keyword, string Builtin,
		string String, string Comment, string Number, string Function,
		string Member, string Symbol, string Selection, string CurrentLine
	);

	public override void _EnterTree()
	{
		_finder.Root = this;
		base._EnterTree();
	}

	public override async void _ExitTree()
	{
		AutoSaveNow();
		ExternalFileChanged -= ReloadFromDiskIfCurrent;
		_hoverCts?.Cancel();
		_hoverCts?.Dispose();
		if (_completion != null)
		{
			await _completion.CloseScriptAsync(Container.TargetFilePathAbsolute);
			_completion.PublishDiagnostics -= OnPublishDiagnostics;
		}
		CreatorSettingsService.Instance.Changed -= OnCreatorSettingChanged;
		base._ExitTree();
	}

	public override async void _Ready()
	{
		ExternalFileChanged += ReloadFromDiskIfCurrent;
		AddChild(_autoCompleteTimer = new());
		_autoCompleteTimer.OneShot = true;
		AddChild(_autoSaveTimer = new());
		_autoSaveTimer.OneShot = true;
		_autoSaveTimer.WaitTime = 0.75;
		_autoSaveTimer.Timeout += AutoSaveNow;
		_autoCompleteTimer.Timeout += OnCompletionRequest;
		AddChild(_hoverTimer = new());
		_hoverTimer.OneShot = true;
		_hoverTimer.WaitTime = 0.45;
		_hoverTimer.Timeout += RequestHoverDocumentation;
		AddChild(_hoverPopup = new PopupPanel
		{
			Unresizable = true,
			// Documentation is informational. A regular popup takes keyboard focus,
			// which prevents the CodeEdit from receiving arrows, Enter, and other
			// editing keys while the popup is visible.
			Unfocusable = true,
		});
		_hoverPopup.AddChild(_hoverText = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			CustomMinimumSize = new Vector2(420, 72),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		if (Container.CodeCompletion == FileTypeEnum.Lua)
		{
			_completion = Container.TargetSession.LuaCompletion;
			_completion?.PublishDiagnostics += OnPublishDiagnostics;

			CodeEditor.CodeCompletionPrefixes = [".", ":", "\n", ",", " ", "("];
			CodeEditor.CodeCompletionEnabled = true;
			CodeEditor.CodeCompletionRequested += OnCompletionRequest;
			WarmStyLuaInstallation();
		}

		CodeEditor.Text = File.ReadAllText(Container.TargetFilePathAbsolute);
		CodeEditor.ClearUndoHistory();
		CodeEditor.TextChanged += OnCodeEditTextChanged;
		InitSyntaxHighlighter(Container.CodeCompletion);

		CreatorSettingsService.Instance.Changed += OnCreatorSettingChanged;
		ApplyIndentSettings();
		ApplyEditorViewSettings();

		CodeEditor.GuiInput += OnCodeEditGUIInput;

		CodeEditor.GuttersDrawLineNumbers = true;

		CodeEditor.AddGutter(0);
		CodeEditor.SetGutterWidth(0, 20);
		CodeEditor.SetGutterType(0, CodeEdit.GutterType.Icon);
		CodeEditor.SetGutterName(0, "diagnostics");
		CodeEditor.GutterClicked += OnDiagnosticGutterClicked;

		CodeEditor.AutoBraceCompletionEnabled = true;
		CodeEditor.AutoBraceCompletionHighlightMatching = true;
		CodeEditor.IndentAutomatic = true;
		CodeEditor.LineFolding = true;
		CodeEditor.GuttersDrawFoldGutter = true;
		CodeEditor.LineLengthGuidelines = [100, 120];
		ApplyCompletionTheme();

		CodeEditor.Root = this;

		// TODO: Can be made into TextEditorRoot.GrabFocus() ?
		// Needs to be call deferred to be the last to grab
		BV.CallDeferred(CodeEditor.GrabFocus);

		if (_completion != null)
		{
			await _completion.OpenScriptAsync(Container.TargetFilePathAbsolute);
		}

		UpdateStatusBar();
	}

	private void OnCreatorSettingChanged(SettingChangedEvent e)
	{
		if (e.Key == CreatorSettingKeys.CodeEditor.IndentationMode || e.Key == CreatorSettingKeys.CodeEditor.IndentationSize)
		{
			ApplyIndentSettings();
		}

		if (
			e.Key == CreatorSettingKeys.CodeEditor.ShowLineNumbers
			|| e.Key == CreatorSettingKeys.CodeEditor.HighlightCurrentLine
			|| e.Key == CreatorSettingKeys.CodeEditor.WordWrap
			|| e.Key == CreatorSettingKeys.CodeEditor.ShowWhitespace
			|| e.Key == CreatorSettingKeys.CodeEditor.MinimapEnabled
			|| e.Key == CreatorSettingKeys.CodeEditor.CursorBlink
			|| e.Key == CreatorSettingKeys.CodeEditor.CursorBlinkSpeed
			|| e.Key == CreatorSettingKeys.CodeEditor.CursorWidth
			|| e.Key == CreatorSettingKeys.CodeEditor.FontSize
		)
		{
			ApplyEditorViewSettings();
		}

		if (e.Key == CreatorSettingKeys.CodeEditor.ColorTheme)
			InitSyntaxHighlighter(Container.CodeCompletion);

		if (e.Key == CreatorSettingKeys.CodeEditor.InlineSuggestions
			&& !CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.CodeEditor.InlineSuggestions))
			CodeEditor.SetInlineSuggestion(string.Empty);

		if (e.Key == CreatorSettingKeys.CodeEditor.HoverDocumentation
			&& !CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.CodeEditor.HoverDocumentation))
			HideHoverDocumentation();

		if (e.Key == CreatorSettingKeys.CodeEditor.AutoSave
			&& !CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.CodeEditor.AutoSave))
			_autoSaveTimer.Stop();
	}

	private void ApplyIndentSettings()
	{
		IndentationModeEnum indentationMode = CreatorSettingsService.Instance.Get<IndentationModeEnum>(CreatorSettingKeys.CodeEditor.IndentationMode);
		int indentationSize = CreatorSettingsService.Instance.Get<int>(CreatorSettingKeys.CodeEditor.IndentationSize);
		CodeEditor.IndentUseSpaces = indentationMode == IndentationModeEnum.Spaces;
		CodeEditor.IndentSize = indentationSize;
	}

	private void ApplyEditorViewSettings()
	{
		var settings = CreatorSettingsService.Instance;

		CodeEditor.GuttersDrawLineNumbers = settings.Get<bool>(CreatorSettingKeys.CodeEditor.ShowLineNumbers);
		TrySetEditorProperty("highlight_current_line", settings.Get<bool>(CreatorSettingKeys.CodeEditor.HighlightCurrentLine));
		TrySetEditorProperty("draw_tabs", settings.Get<bool>(CreatorSettingKeys.CodeEditor.ShowWhitespace));
		TrySetEditorProperty("draw_spaces", settings.Get<bool>(CreatorSettingKeys.CodeEditor.ShowWhitespace));
		TrySetEditorProperty("draw_minimap", settings.Get<bool>(CreatorSettingKeys.CodeEditor.MinimapEnabled));
		TrySetEditorProperty("caret_blink", settings.Get<bool>(CreatorSettingKeys.CodeEditor.CursorBlink));
		TrySetEditorProperty("caret_blink_interval", settings.Get<float>(CreatorSettingKeys.CodeEditor.CursorBlinkSpeed));
		CodeEditor.AddThemeConstantOverride("caret_width", settings.Get<int>(CreatorSettingKeys.CodeEditor.CursorWidth));
		CodeEditor.AddThemeFontSizeOverride("font_size", settings.Get<int>(CreatorSettingKeys.CodeEditor.FontSize));

		bool wrap = settings.Get<bool>(CreatorSettingKeys.CodeEditor.WordWrap);
		// TextEdit line wrapping mode: 0 = None, 1 = Boundary.
		TrySetEditorProperty("wrap_mode", wrap ? 1 : 0);
	}

	private void TrySetEditorProperty(string propertyName, Variant value)
	{
		_editorPropertySet ??= [.. CodeEditor.GetPropertyList().Select(p => p["name"].ToString() ?? string.Empty)];
		if (_editorPropertySet.Contains(propertyName))
		{
			CodeEditor.Set(propertyName, value);
		}
	}

	private async void OnPublishDiagnostics(string path, List<LspDiagnostic> diagnostics)
	{
		// If not the right path, return
		if (path != Container.TargetFilePathAbsolute) return;

		// Cancel the previous pending update
		_diagCts?.Cancel();
		_diagCts = new CancellationTokenSource();
		CancellationToken token = _diagCts.Token;

		try
		{
			await Task.Delay(DiagDelay, token);

			ApplyDiagnostics(diagnostics);
		}
		catch (TaskCanceledException) { }
	}

	private void ApplyDiagnostics(List<LspDiagnostic> diagnostics)
	{
		ClearDiagnostics();

		int errors = 0;
		int warnings = 0;
		int information = 0;
		int hints = 0;

		foreach (LspDiagnostic diag in diagnostics.OrderBy(static item => item.Range.Start.Line).ThenBy(static item => item.Range.Start.Character))
		{
			int line = Mathf.Clamp(diag.Range.Start.Line, 0, Math.Max(0, CodeEditor.GetLineCount() - 1));
			int startColumn = Math.Max(0, diag.Range.Start.Character);
			int endColumn = Math.Max(startColumn + 1, diag.Range.End.Character);
			EditorDiagnosticSeverity severity = diag.Severity switch
			{
				1 => EditorDiagnosticSeverity.Error,
				2 => EditorDiagnosticSeverity.Warning,
				3 => EditorDiagnosticSeverity.Information,
				_ => EditorDiagnosticSeverity.Hint,
			};

			switch (severity)
			{
				case EditorDiagnosticSeverity.Error: errors++; break;
				case EditorDiagnosticSeverity.Warning: warnings++; break;
				case EditorDiagnosticSeverity.Information: information++; break;
				default: hints++; break;
			}

			if (!_diagnosticsByLine.TryGetValue(line, out List<EditorDiagnosticDecoration>? lineDiagnostics))
			{
				lineDiagnostics = [];
				_diagnosticsByLine[line] = lineDiagnostics;
			}

			lineDiagnostics.Add(new EditorDiagnosticDecoration(line, startColumn, endColumn, diag.Message, severity));

			Color background = severity switch
			{
				EditorDiagnosticSeverity.Error => Color.FromHtml("#F8514918"),
				EditorDiagnosticSeverity.Warning => Color.FromHtml("#D2992214"),
				EditorDiagnosticSeverity.Information => Color.FromHtml("#58A6FF10"),
				_ => Color.FromHtml("#8B949E0C"),
			};
			CodeEditor.SetLineBackgroundColor(line, background);

			Texture2D? gutterIcon = severity switch
			{
				EditorDiagnosticSeverity.Error => TryLoadDiagnosticIcon("error.svg"),
				EditorDiagnosticSeverity.Warning => TryLoadDiagnosticIcon("warning.svg") ?? TryLoadDiagnosticIcon("error.svg"),
				EditorDiagnosticSeverity.Information => TryLoadDiagnosticIcon("info.svg"),
				_ => null,
			};
			CodeEditor.SetLineGutterIcon(line, 0, gutterIcon);
			CodeEditor.SetLineGutterMetadata(line, 0, string.Join("\n", lineDiagnostics.Select(static item => item.Message)));
		}

		CodeEditor.SetDiagnostics(_diagnosticsByLine);

		int total = errors + warnings + information + hints;
		if (total > 0)
		{
			List<string> summary = [];
			if (errors > 0) summary.Add($"{errors} error{(errors == 1 ? string.Empty : "s")}");
			if (warnings > 0) summary.Add($"{warnings} warning{(warnings == 1 ? string.Empty : "s")}");
			if (information > 0) summary.Add($"{information} info");
			if (hints > 0) summary.Add($"{hints} hint{(hints == 1 ? string.Empty : "s")}");
			_diagLabel.Text = string.Join("  •  ", summary);
			_diagLabel.TooltipText = string.Join("\n", diagnostics.Take(20).Select(diag =>
				$"{diag.Range.Start.Line + 1}:{diag.Range.Start.Character + 1}  {diag.Message}"));
			_diagLabel.Visible = true;
		}
	}

	private static Texture2D? TryLoadDiagnosticIcon(string fileName)
	{
		return ResourceLoader.Exists("res://assets/textures/creator/tabs/text_editor/" + fileName)
			? GD.Load<Texture2D>("res://assets/textures/creator/tabs/text_editor/" + fileName)
			: null;
	}

	private void OnDiagnosticGutterClicked(long line, long gutter)
	{
		int lineIndex = checked((int)line);
		int gutterIndex = checked((int)gutter);

		if (
			gutterIndex != 0
			|| !_diagnosticsByLine.TryGetValue(
				lineIndex,
				out List<EditorDiagnosticDecoration>? diagnostics
			)
			|| diagnostics.Count == 0
		)
		{
			return;
		}

		CodeEditor.SetCaretLine(lineIndex);
		CodeEditor.SetCaretColumn(
			Math.Min(
				diagnostics[0].StartColumn,
				CodeEditor.GetLine(lineIndex).Length
			)
		);

		CodeEditor.CenterViewportToCaret();

		_diagLabel.Text = string.Join(
			"\n",
			diagnostics.Select(static item => item.Message)
		);

		_diagLabel.TooltipText = _diagLabel.Text;
		_diagLabel.Visible = true;
	}

	private void ClearDiagnostics()
	{
		_diagnosticsByLine.Clear();
		CodeEditor.ClearDiagnostics();
		_diagLabel.Text = "";
		_diagLabel.TooltipText = "";
		_diagLabel.Visible = false;
		Color transparent = new(0, 0, 0, 0);
		for (int i = 0; i < CodeEditor.GetLineCount(); i++)
		{
			CodeEditor.SetLineBackgroundColor(i, transparent);
			CodeEditor.SetLineGutterIcon(i, 0, null);
			CodeEditor.SetLineGutterMetadata(i, 0, default);
		}
	}

	private void ApplyCompletionTheme()
	{
		CodeEditor.AddThemeColorOverride("completion_background_color", Color.FromHtml("#161B22"));
		CodeEditor.AddThemeColorOverride("completion_selected_color", Color.FromHtml("#264F78"));
		CodeEditor.AddThemeColorOverride("completion_existing_color", Color.FromHtml("#8B949E"));
		CodeEditor.AddThemeColorOverride("completion_font_color", Color.FromHtml("#E6EDF3"));
		CodeEditor.AddThemeColorOverride("completion_scroll_color", Color.FromHtml("#30363D"));
		CodeEditor.AddThemeColorOverride("brace_mismatch_color", Color.FromHtml("#FF7B72"));
		CodeEditor.AddThemeColorOverride("code_folding_color", Color.FromHtml("#8B949E"));
		CodeEditor.AddThemeConstantOverride("completion_lines", 10);
		CodeEditor.AddThemeConstantOverride("completion_max_width", 50);
	}


	private async void OnCodeEditGUIInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion)
		{
			_hoverPosition = motion.Position;
			HideHoverDocumentation();
			if (_completion != null && CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.CodeEditor.HoverDocumentation))
				_hoverTimer.Start();
		}
		else if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab, CtrlPressed: false, AltPressed: false }
			&& CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.CodeEditor.InlineSuggestions)
			&& !CodeEditor.HasActiveCodeCompletion()
			&& CodeEditor.AcceptInlineSuggestion())
		{
			CodeEditor.AcceptEvent();
		}
		else if (@event is InputEventKey { Pressed: true, Echo: false, AltPressed: true, ShiftPressed: true, Keycode: Key.F }
			or InputEventKey { Pressed: true, Echo: false, CtrlPressed: true, ShiftPressed: true, Keycode: Key.F })
		{
			CodeEditor.AcceptEvent();
			FormatDocument();
		}
		else if (@event is InputEventKey { Pressed: true, Echo: false, CtrlPressed: true, Keycode: Key.Space })
		{
			CodeEditor.AcceptEvent();
			OnCompletionRequest();
		}
		else if (@event is InputEventKey { Pressed: true, Echo: false, CtrlPressed: true, ShiftPressed: false, Keycode: Key.D })
		{
			CodeEditor.AcceptEvent();
			DuplicateCurrentLines();
		}
		else if (@event is InputEventKey { Pressed: true, Echo: false, CtrlPressed: true, ShiftPressed: true, Keycode: Key.K })
		{
			CodeEditor.AcceptEvent();
			DeleteCurrentLines();
		}
		else if (@event is InputEventKey { Pressed: true, Echo: false, CtrlPressed: true, ShiftPressed: false, Keycode: Key.L })
		{
			CodeEditor.AcceptEvent();
			int line = CodeEditor.GetCaretLine();
			CodeEditor.Select(line, 0, line, CodeEditor.GetLine(line).Length);
		}
		else if (@event is InputEventKey { Pressed: true, Echo: false, AltPressed: true, Keycode: Key.Up })
		{
			CodeEditor.AcceptEvent();
			MoveCurrentLine(-1);
		}
		else if (@event is InputEventKey { Pressed: true, Echo: false, AltPressed: true, Keycode: Key.Down })
		{
			CodeEditor.AcceptEvent();
			MoveCurrentLine(1);
		}
		else if (@event.IsActionPressed("save"))
		{
			CodeEditor.AcceptEvent();
			if (Container.CodeCompletion == FileTypeEnum.Lua
				&& CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.CodeEditor.FormatOnSave))
				await FormatRangeAsync(0, CodeEditor.GetLineCount() - 1, "document");
			Save();
			Saved = true;
			SavedChanged?.Invoke(true);
			CreatorService.Interface.StatusBar?.SetStatus("Text file saved to " + Container.TargetFilePath + " at " + DateTime.Now.ToString("HH:mm:ss"));
		}
		else if (@event.IsActionPressed("textedit_find") || @event.IsActionPressed("textedit_replace"))
		{
			CodeEditor.AcceptEvent();
			_finder.Open(CodeEditor.GetSelectedText());
		}
		else if (@event.IsActionPressed("textedit_toggle_comment"))
		{
			CodeEditor.AcceptEvent();
			ToggleComment();
		}
		else if (@event.IsActionPressed("ui_cancel"))
		{
			CodeEditor.AcceptEvent();
			_finder.Close();
		}
		else
		{
			if (@event is InputEventKey { Pressed: true }) HideHoverDocumentation();
			UpdateStatusBar();
		}
	}

	private async void WarmStyLuaInstallation()
	{
		await StyLuaInstaller.EnsureInstalledAsync();
	}

	public void DuplicateCurrentLines()
	{
		int first = CodeEditor.HasSelection() ? CodeEditor.GetSelectionFromLine() : CodeEditor.GetCaretLine();
		int last = CodeEditor.HasSelection() ? CodeEditor.GetSelectionToLine() : first;
		string[] copies = Enumerable.Range(first, last - first + 1).Select(CodeEditor.GetLine).ToArray();
		CodeEditor.BeginComplexOperation();
		for (int index = 0; index < copies.Length; index++) CodeEditor.InsertLineAt(last + 1 + index, copies[index]);
		CodeEditor.EndComplexOperation();
		CodeEditor.SetCaretLine(last + copies.Length);
	}

	public void DeleteCurrentLines()
	{
		int first = CodeEditor.HasSelection() ? CodeEditor.GetSelectionFromLine() : CodeEditor.GetCaretLine();
		int last = CodeEditor.HasSelection() ? CodeEditor.GetSelectionToLine() : first;
		CodeEditor.BeginComplexOperation();
		for (int line = last; line >= first; line--) CodeEditor.RemoveLineAt(line);
		CodeEditor.EndComplexOperation();
		CodeEditor.SetCaretLine(Math.Min(first, CodeEditor.GetLineCount() - 1));
	}

	public void MoveCurrentLine(int direction)
	{
		if (CodeEditor.HasSelection()) return;
		int line = CodeEditor.GetCaretLine();
		int target = line + direction;
		if (target < 0 || target >= CodeEditor.GetLineCount()) return;
		CodeEditor.BeginComplexOperation();
		CodeEditor.SwapLines(line, target);
		CodeEditor.EndComplexOperation();
		CodeEditor.SetCaretLine(target);
	}

	private async void RequestHoverDocumentation()
	{
		if (_completion == null || !CodeEditor.GetGlobalRect().HasPoint(CodeEditor.GetGlobalMousePosition())) return;

		Vector2I position = CodeEditor.GetLineColumnAtPos((Vector2I)_hoverPosition);
		if (position.X < 0 || position.Y < 0) return;

		_hoverCts?.Cancel();
		_hoverCts?.Dispose();
		_hoverCts = new CancellationTokenSource();
		try
		{
			// TextEdit returns column in X and line in Y.
			string? documentation = await _completion.GetHoverAsync(
				Container.TargetFilePathAbsolute, position.Y, position.X, _hoverCts.Token);
			if (string.IsNullOrWhiteSpace(documentation) || _hoverCts.IsCancellationRequested) return;

			_hoverText.Text = MarkdownToBbcode(documentation);
			Vector2 screenPosition = CodeEditor.GetScreenPosition() + _hoverPosition + new Vector2(14, 20);
			_hoverPopup.Popup(new Rect2I((Vector2I)screenPosition, new Vector2I(480, 120)));
		}
		catch (OperationCanceledException) { }
	}

	private static string MarkdownToBbcode(string markdown)
	{
		string text = markdown.Replace("\r\n", "\n").Replace("[", "[lb]");
		text = Regex.Replace(text, "```(?:[A-Za-z0-9_+-]+)?\\s*\\n?(.*?)```", "[code]$1[/code]", RegexOptions.Singleline);
		text = Regex.Replace(text, "`([^`\\n]+)`", "[code]$1[/code]");
		text = Regex.Replace(text, "\\*\\*([^*]+)\\*\\*", "[b]$1[/b]");
		text = Regex.Replace(text, "(?<!\\*)\\*([^*\\n]+)\\*(?!\\*)", "[i]$1[/i]");
		return text.Trim();
	}

	private void HideHoverDocumentation()
	{
		_hoverTimer?.Stop();
		_hoverCts?.Cancel();
		if (_hoverPopup?.Visible == true) _hoverPopup.Hide();
	}

	public async void FormatDocument() =>
		await FormatRangeAsync(0, CodeEditor.GetLineCount() - 1, "document");

	public async void FormatSelection()
	{
		if (!CodeEditor.HasSelection()) return;
		await FormatRangeAsync(CodeEditor.GetSelectionFromLine(), CodeEditor.GetSelectionToLine(), "selection");
	}

	private async Task FormatRangeAsync(int fromLine, int toLine, string target)
	{
		fromLine = Math.Max(0, fromLine);
		toLine = Math.Min(CodeEditor.GetLineCount() - 1, toLine);
		if (fromLine > toLine) return;

		if (Container.CodeCompletion == FileTypeEnum.Lua)
		{
			string source = string.Join("\n", Enumerable.Range(fromLine, toLine - fromLine + 1)
				.Select(CodeEditor.GetLine));
			string? formatted = await FormatLuauWithStyluaAsync(source);
			if (formatted != null)
			{
				CodeEditor.BeginComplexOperation();
				CodeEditor.Select(fromLine, 0, toLine, CodeEditor.GetLine(toLine).Length);
				CodeEditor.InsertTextAtCaret(formatted.TrimEnd('\r', '\n'));
				CodeEditor.EndComplexOperation();
				CreatorService.Interface.StatusBar?.SetStatus("Formatted " + target + " with StyLua");
				return;
			}
		}

		FormatLinesFallback(fromLine, toLine);
		CreatorService.Interface.StatusBar?.SetStatus(
			"StyLua is unavailable; applied basic formatting to " + target
		);
	}

	private static async Task<string?> FormatLuauWithStyluaAsync(string source)
	{
		try
		{
			string? executable = await StyLuaInstaller.EnsureInstalledAsync();
			if (executable == null) return null;
			ProcessStartInfo startInfo = new()
			{
				FileName = executable,
				Arguments = "--stdin-filepath script.luau -",
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			};
			Process? process = Process.Start(startInfo);
			if (process == null) return null;
			using (process)
			{
				Task<string> output = process.StandardOutput.ReadToEndAsync();
				Task<string> errors = process.StandardError.ReadToEndAsync();
				await process.StandardInput.WriteAsync(source);
				process.StandardInput.Close();
				await process.WaitForExitAsync();
				string result = await output;
				string error = await errors;
				if (process.ExitCode == 0) return result;
				BV.PrintWarn("StyLua could not format the selection: ", error.Trim());
			}
		}
		catch (System.ComponentModel.Win32Exception)
		{
			// StyLua is optional for development builds; the caller uses the
			// deterministic indentation fallback until it is packaged/installed.
		}
		catch (Exception ex)
		{
			BV.PrintWarn("StyLua formatter failed: ", ex.Message);
		}
		return null;
	}

	private void FormatLinesFallback(int fromLine, int toLine)
	{
		fromLine = Math.Max(0, fromLine);
		toLine = Math.Min(CodeEditor.GetLineCount() - 1, toLine);
		int indent = fromLine > 0 ? CountLeadingIndent(CodeEditor.GetLine(fromLine - 1)) : 0;
		for (int line = fromLine; line <= toLine; line++)
		{
			string trimmed = CodeEditor.GetLine(line).Trim();
			if (trimmed.Length == 0)
			{
				CodeEditor.SetLine(line, "");
				continue;
			}

			if (Container.CodeCompletion == FileTypeEnum.Lua && ClosesLuauBlock(trimmed))
				indent = Math.Max(0, indent - 1);
			CodeEditor.SetLine(line, new string('\t', indent) + trimmed);
			if (Container.CodeCompletion == FileTypeEnum.Lua && OpensLuauBlock(trimmed))
				indent++;
		}
	}

	private static int CountLeadingIndent(string line)
	{
		int count = 0;
		foreach (char character in line)
		{
			if (character == '\t') count++;
			else if (character != ' ') break;
		}
		return count;
	}

	private static bool ClosesLuauBlock(string line) =>
		line == "end" || line.StartsWith("end ") || line == "until"
		|| line.StartsWith("until ") || line == "else" || line.StartsWith("elseif ");

	private static bool OpensLuauBlock(string line)
	{
		string code = line.Split("--", 2)[0].TrimEnd();
		return code == "do" || code == "repeat" || code == "else"
			|| code.EndsWith(" then") || code.EndsWith(" do")
			|| code.StartsWith("function ") || code.Contains(" function(")
			|| code.Contains(" function (");
	}

	private void InitSyntaxHighlighter(FileTypeEnum fileType)
	{
		_palette = GetEditorPalette(CreatorSettingsService.Instance.Get<CodeEditorColorThemeEnum>(CreatorSettingKeys.CodeEditor.ColorTheme));
		CodeEditor.ClearStringDelimiters();
		_highlighter = new();
		CodeEditor.SyntaxHighlighter = _highlighter;
		_highlighter.FunctionColor = Color.FromHtml(_palette.Function);
		_highlighter.MemberVariableColor = Color.FromHtml(_palette.Member);
		_highlighter.NumberColor = Color.FromHtml(_palette.Number);
		_highlighter.SymbolColor = Color.FromHtml(_palette.Symbol);
		CodeEditor.AddThemeColorOverride("font_color", Color.FromHtml(_palette.Foreground));
		CodeEditor.AddThemeColorOverride("font_selected_color", Color.FromHtml(_palette.Foreground));
		CodeEditor.AddThemeColorOverride("selection_color", Color.FromHtml(_palette.Selection));
		CodeEditor.AddThemeColorOverride("current_line_color", Color.FromHtml(_palette.CurrentLine));
		StyleBoxFlat editorBackground = new() { BgColor = Color.FromHtml(_palette.Background) };
		CodeEditor.AddThemeStyleboxOverride("normal", editorBackground);

		if (fileType == FileTypeEnum.Lua)
		{
			foreach (string item in LuaCompletionService.LuaKeywords)
			{
				_highlighter.AddKeywordColor(item, Color.FromHtml(_palette.Keyword));
			}

			foreach (string builtin in new[]
			{
				"assert", "error", "getmetatable", "ipairs", "next", "pairs", "pcall", "print",
				"rawequal", "rawget", "rawlen", "rawset", "require", "select", "setmetatable",
				"tonumber", "tostring", "type", "typeof", "unpack", "warn", "xpcall",
				"world", "script", "game", "self"
			})
			{
				_highlighter.AddKeywordColor(builtin, Color.FromHtml(_palette.Builtin));
			}

			foreach (string typeName in new[]
			{
				"any", "boolean", "buffer", "CFrame", "Color3", "Instance", "never", "nil",
				"number", "string", "table", "thread", "unknown", "Vector2", "Vector3"
			})
			{
				_highlighter.AddKeywordColor(typeName, Color.FromHtml(_palette.Builtin));
			}

			_highlighter.AddColorRegion("\"", "\"", Color.FromHtml(_palette.String));
			_highlighter.AddColorRegion("'", "'", Color.FromHtml(_palette.String));
			_highlighter.AddColorRegion("`", "`", Color.FromHtml(_palette.String));
			_highlighter.AddColorRegion("[[", "]]", Color.FromHtml(_palette.String));
			_highlighter.AddColorRegion("--[[", "]]", Color.FromHtml(_palette.Comment));
			_highlighter.AddColorRegion("--", "", Color.FromHtml(_palette.Comment));

			CodeEditor.AddStringDelimiter("\"", "\"", true);
			CodeEditor.AddStringDelimiter("'", "'", true);
			CodeEditor.AddStringDelimiter("[[", "]]", false);
			return;
		}

		string extension = Path.GetExtension(Container.TargetFilePathAbsolute).ToLowerInvariant();
		switch (extension)
		{
			case ".json":
			case ".jsonc":
			case ".bvxw":
			case ".bvxl":
			case ".bvproject":
			case ".bvanim":
			case ".bvmodel":
			case ".bvxm":
				AddKeywords(["true", "false", "null"], _palette.Keyword);
				AddStrings();
				if (extension == ".jsonc") AddCStyleComments();
				break;
			case ".yaml":
			case ".yml":
				AddKeywords(["true", "false", "null", "yes", "no", "on", "off", "~"], _palette.Keyword);
				AddStrings();
				_highlighter.AddColorRegion("#", "", Color.FromHtml(_palette.Comment));
				break;
			case ".toml":
			case ".ini":
			case ".cfg":
			case ".env":
				AddKeywords(["true", "false", "null"], _palette.Keyword);
				AddStrings();
				_highlighter.AddColorRegion("#", "", Color.FromHtml(_palette.Comment));
				_highlighter.AddColorRegion(";", "", Color.FromHtml(_palette.Comment));
				break;
			case ".cs":
				AddKeywords([
					"abstract", "as", "async", "await", "base", "bool", "break", "byte", "case",
					"catch", "char", "class", "const", "continue", "decimal", "default", "delegate",
					"do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
					"fixed", "float", "for", "foreach", "if", "implicit", "in", "int", "interface",
					"internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator",
					"out", "override", "params", "private", "protected", "public", "readonly", "record",
					"ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
					"string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint",
					"ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void",
					"volatile", "when", "where", "while", "yield"
				], _palette.Keyword);
				AddStrings(includeBackticks: false);
				AddCStyleComments();
				break;
			case ".js":
			case ".jsx":
			case ".ts":
			case ".tsx":
				AddKeywords([
					"async", "await", "break", "case", "catch", "class", "const", "continue", "debugger",
					"default", "delete", "do", "else", "enum", "export", "extends", "false", "finally",
					"for", "from", "function", "if", "implements", "import", "in", "instanceof",
					"interface", "let", "new", "null", "of", "private", "protected", "public", "return",
					"static", "super", "switch", "this", "throw", "true", "try", "type", "typeof",
					"undefined", "var", "void", "while", "with", "yield"
				], _palette.Keyword);
				AddStrings();
				AddCStyleComments();
				break;
			case ".py":
				AddKeywords([
					"and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del",
					"elif", "else", "except", "False", "finally", "for", "from", "global", "if", "import",
					"in", "is", "lambda", "None", "nonlocal", "not", "or", "pass", "raise", "return",
					"True", "try", "while", "with", "yield"
				], _palette.Keyword);
				AddStrings(includeBackticks: false);
				_highlighter.AddColorRegion("\"\"\"", "\"\"\"", Color.FromHtml(_palette.String));
				_highlighter.AddColorRegion("'''", "'''", Color.FromHtml(_palette.String));
				_highlighter.AddColorRegion("#", "", Color.FromHtml(_palette.Comment));
				break;
			case ".xml":
			case ".html":
			case ".htm":
			case ".svg":
				AddStrings(includeBackticks: false);
				_highlighter.AddColorRegion("<!--", "-->", Color.FromHtml(_palette.Comment));
				break;
			case ".css":
			case ".scss":
				AddKeywords(["@import", "@media", "@keyframes", "important"], _palette.Keyword);
				AddStrings(includeBackticks: false);
				AddCStyleComments();
				break;
			case ".md":
			case ".markdown":
				_highlighter.AddColorRegion("`", "`", Color.FromHtml(_palette.String));
				_highlighter.AddColorRegion("<!--", "-->", Color.FromHtml(_palette.Comment));
				break;
			case ".sh":
			case ".bash":
			case ".ps1":
				AddKeywords(["break", "case", "continue", "do", "done", "else", "elseif", "esac", "fi", "for", "foreach", "function", "if", "in", "return", "switch", "then", "until", "while"], _palette.Keyword);
				AddStrings();
				_highlighter.AddColorRegion("#", "", Color.FromHtml(_palette.Comment));
				break;
			default:
				_highlighter.FunctionColor = ColorWhite;
				_highlighter.MemberVariableColor = ColorWhite;
				_highlighter.NumberColor = ColorWhite;
				_highlighter.SymbolColor = ColorWhite;
				break;
		}
	}

	private void AddKeywords(IEnumerable<string> keywords, string color)
	{
		Color keywordColor = Color.FromHtml(color);
		foreach (string keyword in keywords)
			_highlighter.AddKeywordColor(keyword, keywordColor);
	}

	private void AddStrings(bool includeBackticks = true)
	{
		Color stringColor = Color.FromHtml(_palette.String);
		_highlighter.AddColorRegion("\"", "\"", stringColor);
		_highlighter.AddColorRegion("'", "'", stringColor);
		CodeEditor.AddStringDelimiter("\"", "\"", true);
		CodeEditor.AddStringDelimiter("'", "'", true);
		if (includeBackticks)
		{
			_highlighter.AddColorRegion("`", "`", stringColor);
			CodeEditor.AddStringDelimiter("`", "`", true);
		}
	}

	private void AddCStyleComments()
	{
		Color commentColor = Color.FromHtml(_palette.Comment);
		_highlighter.AddColorRegion("//", "", commentColor);
		_highlighter.AddColorRegion("/*", "*/", commentColor);
	}

	private static EditorPalette GetEditorPalette(CodeEditorColorThemeEnum theme) => theme switch
	{
		CodeEditorColorThemeEnum.VisualStudioDark => new("#1E1E1E", "#D4D4D4", "#C586C0", "#4EC9B0", "#CE9178", "#6A9955", "#B5CEA8", "#DCDCAA", "#9CDCFE", "#D4D4D4", "#264F78", "#252526"),
		CodeEditorColorThemeEnum.Dracula => new("#282A36", "#F8F8F2", "#FF79C6", "#8BE9FD", "#F1FA8C", "#6272A4", "#BD93F9", "#50FA7B", "#8BE9FD", "#F8F8F2", "#44475A", "#30323F"),
		CodeEditorColorThemeEnum.Light => new("#FAFAFA", "#24292F", "#CF222E", "#0550AE", "#0A3069", "#6E7781", "#0550AE", "#8250DF", "#953800", "#24292F", "#B6D7FF", "#EEF3F8"),
		CodeEditorColorThemeEnum.HighContrast => new("#000000", "#FFFFFF", "#FFFF00", "#00FFFF", "#FFB000", "#7CFC00", "#00FF00", "#00FFFF", "#FFFFFF", "#FFFFFF", "#005A9C", "#191919"),
		_ => new("#0D1117", "#E6EDF3", "#FF7B72", "#79C0FF", "#A5D6FF", "#8B949E", "#D2A8FF", "#D2A8FF", "#79C0FF", "#C9D1D9", "#1F6FEB66", "#161B22")
	};

	public void Save()
	{
		File.WriteAllText(Container.TargetFilePathAbsolute, CodeEditor.Text);
	}

	public async void SaveDocument()
	{
		if (Container.CodeCompletion == FileTypeEnum.Lua
			&& CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.CodeEditor.FormatOnSave))
			await FormatRangeAsync(0, CodeEditor.GetLineCount() - 1, "document");
		Save();
		Saved = true;
		SavedChanged?.Invoke(true);
		CreatorService.Interface.StatusBar?.SetStatus("Text file saved to " + Container.TargetFilePath + " at " + DateTime.Now.ToString("HH:mm:ss"));
	}

	public void OpenFind() => _finder.Open(CodeEditor.GetSelectedText());

	private void AutoSaveNow()
	{
		if (!CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.CodeEditor.AutoSave) || Saved)
			return;
		Save();
		Saved = true;
		SavedChanged?.Invoke(true);
	}

	private async void OnCodeEditTextChanged()
	{
		CodeEditor.SetInlineSuggestion(string.Empty);
		string curText = CodeEditor.Text;
		Saved = false;
		SavedChanged?.Invoke(false);
		if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.CodeEditor.AutoSave))
			_autoSaveTimer.Start();
		if (_completion != null)
		{
			await _completion.UpdateScriptChangeAsync(Container.TargetFilePathAbsolute, curText);
			if (_oldText != curText)
			{
				_oldText = curText;

				if (IsCompletionTrigger())
				{
					OnCompletionRequest();
				}
			}
		}
	}

	private bool IsCompletionTrigger()
	{
		int line = CodeEditor.GetCaretLine();
		int col = CodeEditor.GetCaretColumn();
		string lineText = CodeEditor.GetLine(line);

		if (string.IsNullOrWhiteSpace(lineText)) return false;

		if (col > 0)
		{
			char prevChar = lineText[col - 1];

			// Don't trigger on space, equals, or commas
			if (prevChar == ' ' || prevChar == '=' || prevChar == ',')
				return false;

			// Don't trigger on newlines/tabs
			if (prevChar == '\n' || prevChar == '\t')
				return false;
		}

		return true;
	}

	public async void OnCompletionRequest()
	{
		if (_completion == null) return;
		CodeEditCompletionContext context = new()
		{
			ScriptPath = Container.TargetFilePathAbsolute,
			Content = CodeEditor.Text,
			CursorLine = CodeEditor.GetCaretLine(),
			CursorColumn = CodeEditor.GetCaretColumn(),
		};

		List<CodeEditCompletionItem> items = await _completion.GetCompletionsAsync(context);

		string wcaret = GetWordBeforeCaret();

		foreach (CodeEditCompletionItem item in items)
		{
			if (wcaret == item.InsertText)
			{
				return;
			}
		}

		if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.CodeEditor.InlineSuggestions))
		{
			CodeEditCompletionItem inlineItem = items.FirstOrDefault(item =>
				item.InsertText.Length > wcaret.Length
				&& item.InsertText.StartsWith(wcaret, StringComparison.OrdinalIgnoreCase));
			CodeEditor.SetInlineSuggestion(string.IsNullOrEmpty(inlineItem.InsertText)
				? string.Empty
				: inlineItem.InsertText[wcaret.Length..]);
		}

		foreach (CodeEditCompletionItem item in items)
		{
			string? iconTxt = item.Kind switch
			{
				CodeEdit.CodeCompletionKind.Member => "Property",
				CodeEdit.CodeCompletionKind.Function => "Method",
				_ => "None"
			};
			Texture2D? icon = null;
			if (iconTxt != null)
			{
				icon = GD.Load<Texture2D>(CodeCompletionIconPath.PathJoin(iconTxt + ".svg"));
			}
			string display = string.IsNullOrWhiteSpace(item.Detail)
				? item.DisplayText
				: $"{item.DisplayText}    {item.Detail.Replace('\n', ' ')}";
			CodeEditor.AddCodeCompletionOption(item.Kind, display, item.InsertText, icon: icon, location: -1);
		}
		CodeEditor.UpdateCodeCompletionOptions(false);
	}

	private void UpdateStatusBar()
	{
		int lineIndex = CodeEditor.GetCaretLine() + 1;
		int column = CodeEditor.GetCaretColumn() + 1;
		string language = Container.CodeCompletion == FileTypeEnum.Lua
			? "Luau"
			: Path.GetExtension(Container.TargetFilePathAbsolute).TrimStart('.').ToUpperInvariant();
		if (string.IsNullOrWhiteSpace(language)) language = "Plain Text";
		_statusBar.Text = $"{language}  •  Ln {lineIndex}, Col {column}  •  {Container.OriginTabName}";
	}

	public string GetWordBeforeCaret()
	{
		int lineIndex = CodeEditor.GetCaretLine();
		int column = CodeEditor.GetCaretColumn();
		string lineText = CodeEditor.GetLine(lineIndex);

		if (column == 0) return string.Empty;

		int startPos = column;

		while (startPos > 0)
		{
			char c = lineText[startPos - 1];

			if (char.IsLetterOrDigit(c) || c == '_')
			{
				startPos--;
			}
			else
			{
				break;
			}
		}

		return lineText[startPos..column];
	}

	public IEnumerable<int> GetSelectedLines()
	{
		for (int caretIdx = 0; caretIdx < CodeEditor.GetCaretCount(); caretIdx++)
		{
			for (int lineIdx = CodeEditor.GetSelectionFromLine(caretIdx); lineIdx <= CodeEditor.GetSelectionToLine(caretIdx); lineIdx++)
			{
				yield return lineIdx;
			}
		}
	}

	private bool IsSelectionCommented()
	{
		foreach (int lineIdx in GetSelectedLines())
		{
			string lineText = CodeEditor.GetLine(lineIdx);
			if (!lineText.StartsWith("--"))
			{
				return false;
			}
		}
		return true;
	}

	public void ToggleComment()
	{
		if (IsSelectionCommented())
		{
			foreach (int lineIdx in GetSelectedLines())
			{
				string lineText = CodeEditor.GetLine(lineIdx);
				CodeEditor.SetLine(lineIdx, lineText[2..]);
			}
		}
		else
		{
			foreach (int lineIdx in GetSelectedLines())
			{
				string lineText = CodeEditor.GetLine(lineIdx);
				CodeEditor.SetLine(lineIdx, "--" + lineText);
			}
		}
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Creator.UI.TextEditor;

public enum EditorDiagnosticSeverity
{
	Error = 1,
	Warning = 2,
	Information = 3,
	Hint = 4,
}

public sealed record EditorDiagnosticDecoration(
	int Line,
	int StartColumn,
	int EndColumn,
	string Message,
	EditorDiagnosticSeverity Severity);

public sealed partial class TextEditorField : CodeEdit
{
	private const int FontSizeStep = 2;
	private const int MinFontSize = 8;
	private const int MaxFontSize = 72;

	public TextEditorRoot Root = null!;

	private int _currentFontSize = 16;
	private readonly Dictionary<int, List<EditorDiagnosticDecoration>> _diagnostics = [];
	private const int FormatDocumentMenuId = 10001;
	private const int FormatSelectionMenuId = 10002;

	public override void _Ready()
	{
		int size = GetThemeFontSize("font_size", "Label");
		_currentFontSize = size > 0 ? size : 16;
		TextChanged += QueueRedraw;
		CaretChanged += QueueRedraw;
		GetVScrollBar().ValueChanged += _ => QueueRedraw();
		GetHScrollBar().ValueChanged += _ => QueueRedraw();
		PopupMenu menu = GetMenu();
		menu.AddSeparator();
		menu.AddItem("Format Document", FormatDocumentMenuId);
		menu.AddItem("Format Selection", FormatSelectionMenuId);
		menu.IdPressed += OnEditorMenuPressed;
		menu.AboutToPopup += () =>
			menu.SetItemDisabled(menu.GetItemIndex(FormatSelectionMenuId), !HasSelection());
		base._Ready();
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (mb.CtrlPressed && mb.ButtonIndex == MouseButton.WheelUp)
			{
				_currentFontSize = Mathf.Clamp(_currentFontSize + FontSizeStep, MinFontSize, MaxFontSize);
				AddThemeFontSizeOverride("font_size", _currentFontSize);
				AcceptEvent();
			}
			else if (mb.CtrlPressed && mb.ButtonIndex == MouseButton.WheelDown)
			{
				_currentFontSize = Mathf.Clamp(_currentFontSize - FontSizeStep, MinFontSize, MaxFontSize);
				AddThemeFontSizeOverride("font_size", _currentFontSize);
				AcceptEvent();
			}
		}

		base._GuiInput(@event);
	}

	private void OnEditorMenuPressed(long id)
	{
		if (id == FormatDocumentMenuId) Root.FormatDocument();
		else if (id == FormatSelectionMenuId) Root.FormatSelection();
	}

	public void SetDiagnostics(IReadOnlyDictionary<int, List<EditorDiagnosticDecoration>> diagnostics)
	{
		_diagnostics.Clear();
		foreach ((int line, List<EditorDiagnosticDecoration> items) in diagnostics)
			_diagnostics[line] = [.. items];
		QueueRedraw();
	}

	public void ClearDiagnostics()
	{
		_diagnostics.Clear();
		TooltipText = string.Empty;
		QueueRedraw();
	}

	public override void _Draw()
	{
		base._Draw();
		if (_diagnostics.Count == 0) return;

		Font font = GetThemeFont("font");
		int fontSize = Math.Max(10, GetThemeFontSize("font_size") - 2);
		const float rightPadding = 14f;

		foreach ((int line, List<EditorDiagnosticDecoration> items) in _diagnostics)
		{
			if (line < 0 || line >= GetLineCount() || items.Count == 0) continue;

			string lineText = GetLine(line);
			int firstColumn = Math.Min(Math.Max(0, items.Min(static item => item.StartColumn)), lineText.Length);
			int lastColumn = Math.Min(Math.Max(firstColumn + 1, items.Max(static item => item.EndColumn)), lineText.Length);
			Rect2I startRect = GetRectAtLineColumn(line, firstColumn);
			Rect2I endRect = GetRectAtLineColumn(line, Math.Max(firstColumn, lastColumn - 1));
			if (startRect.Position.X < 0 || startRect.Position.Y < 0) continue;

			EditorDiagnosticDecoration primary = items.OrderBy(static item => item.Severity).First();
			Color color = GetDiagnosticColor(primary.Severity);
			float underlineY = startRect.Position.Y + startRect.Size.Y - 2;
			float underlineStart = startRect.Position.X;
			float underlineEnd = Math.Max(underlineStart + 8, endRect.Position.X + Math.Max(6, endRect.Size.X));
			DrawSquiggle(new Vector2(underlineStart, underlineY), underlineEnd - underlineStart, color);

			Rect2I endOfLine = GetRectAtLineColumn(line, Math.Max(0, lineText.Length - 1));
			float codeEndX = lineText.Length == 0 ? startRect.Position.X : endOfLine.Position.X + endOfLine.Size.X;
			string message = items.Count == 1 ? primary.Message : $"{primary.Message}  (+{items.Count - 1})";
			message = CollapseWhitespace(message);
			float available = Size.X - codeEndX - 38f;
			if (available < 90f) continue;

			string fitted = FitMessage(font, fontSize, message, available);
			if (string.IsNullOrEmpty(fitted)) continue;

			float textWidth = font.GetStringSize(fitted, HorizontalAlignment.Left, -1, fontSize).X;
			float lensX = Math.Min(codeEndX + 24f, Size.X - textWidth - rightPadding);
			float lensY = startRect.Position.Y + startRect.Size.Y - 3f;
			DrawString(font, new Vector2(lensX, lensY), fitted, HorizontalAlignment.Left, -1, fontSize, color);
		}
	}

	private void DrawSquiggle(Vector2 start, float width, Color color)
	{
		const float segment = 3f;
		Vector2 previous = start;
		bool up = true;
		for (float x = segment; x <= width + segment; x += segment)
		{
			Vector2 next = new(start.X + Math.Min(x, width), start.Y + (up ? -2f : 0f));
			DrawLine(previous, next, color, 1.25f, true);
			previous = next;
			up = !up;
		}
	}

	private static Color GetDiagnosticColor(EditorDiagnosticSeverity severity) => severity switch
	{
		EditorDiagnosticSeverity.Error => Color.FromHtml("#FF7B72"),
		EditorDiagnosticSeverity.Warning => Color.FromHtml("#D29922"),
		EditorDiagnosticSeverity.Information => Color.FromHtml("#58A6FF"),
		_ => Color.FromHtml("#8B949E"),
	};

	private static string CollapseWhitespace(string value)
	{
		return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
	}

	private static string FitMessage(Font font, int fontSize, string message, float availableWidth)
	{
		if (font.GetStringSize(message, HorizontalAlignment.Left, -1, fontSize).X <= availableWidth)
			return message;

		const string ellipsis = "…";
		int low = 0;
		int high = message.Length;
		while (low < high)
		{
			int mid = (low + high + 1) / 2;
			string candidate = message[..mid] + ellipsis;
			if (font.GetStringSize(candidate, HorizontalAlignment.Left, -1, fontSize).X <= availableWidth)
				low = mid;
			else
				high = mid - 1;
		}
		return low < 4 ? string.Empty : message[..low] + ellipsis;
	}


	public override void _ConfirmCodeCompletion(bool replace)
	{
		int index = GetCodeCompletionSelectedIndex();
		if (index == -1) return;

		var selectedOption = GetCodeCompletionOption(index);
		string insertText = (string)selectedOption["insert_text"];

		// Referenced from https://github.com/godotengine/godot/blob/c742d107e29b2c858ef8930760479deb413c68bc/scene/gui/code_edit.cpp#L2367C16-L2367C39
		string completionBase = GetCompletionPrefix();

		int line = GetCaretLine();
		int column = GetCaretColumn();

		BeginComplexOperation();

		if (replace)
		{
			string lineText = GetLine(line);
			int caretCol = column;
			int caretRemoveLine = line;
			bool mergeText = true;

			if (IsInString(line, column) != -1)
			{
				Vector2I stringEnd = (Vector2I)GetDelimiterEndPosition(line, column);
				if (stringEnd.X != -1)
				{
					mergeText = false;
					caretRemoveLine = stringEnd.Y;
					caretCol = stringEnd.X;
				}
			}

			if (mergeText)
			{
				while (caretCol < lineText.Length && !IsSymbol(lineText[caretCol]))
				{
					caretCol++;
				}
			}

			RemoveText(line, column - completionBase.Length, caretRemoveLine, caretCol);
			InsertTextAtCaret(insertText);
		}
		else
		{
			string lineText = GetLine(line);
			int caretCol = column;
			int matchingChars = completionBase.Length;

			while (matchingChars < insertText.Length)
			{
				if (caretCol >= lineText.Length || lineText[caretCol] != insertText[matchingChars])
					break;

				caretCol++;
				matchingChars++;
			}

			RemoveText(line, column - completionBase.Length, line, column);
			InsertTextAtCaret(insertText[..completionBase.Length]);
			SetCaretColumn(caretCol);
			InsertTextAtCaret(insertText[matchingChars..]);
		}

		// Handle parentheses
		if (insertText.EndsWith("()"))
		{
			SetCaretColumn(GetCaretColumn() - 1);
		}

		EndComplexOperation();
		CancelCodeCompletion();
	}

	private string GetCompletionPrefix()
	{
		string lineText = GetLine(GetCaretLine());
		int column = GetCaretColumn();
		int start = column;

		while (start > 0 && !IsSymbol(lineText[start - 1]) && !char.IsWhiteSpace(lineText[start - 1]))
		{
			start--;
		}

		return lineText[start..column];
	}

	private static bool IsSymbol(char c)
	{
		return "!\"#$%&'()*+,-./:;<=>?@[\\]^`{|}~".Contains(c);
	}
}

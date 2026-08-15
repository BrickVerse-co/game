// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileMarkdown : RichTextLabel
{
	public void SetMarkdown(string markdown) => Text = Render(markdown ?? "");

	public override void _Ready()
	{
		BbcodeEnabled = true;
		FitContent = true;
		MetaClicked += meta =>
		{
			string url = meta.AsString();
			if (url.StartsWith("http://") || url.StartsWith("https://")) OS.ShellOpen(url);
		};
	}

	private static string Render(string source)
	{
		List<(string Label, string Url)> links = [];
		string text = Regex.Replace(source, "\\[([^]]+)]\\((https?://[^)]+)\\)", match =>
		{
			links.Add((match.Groups[1].Value, match.Groups[2].Value));
			return $"\u0001LINK{links.Count - 1}\u0002";
		});
		text = text.Replace("[", "[lb]");
		text = Regex.Replace(text, "```(?:\\w+)?\\n?([\\s\\S]*?)```", "[bgcolor=#17202b][font_size=14]$1[/font_size][/bgcolor]");
		text = Regex.Replace(text, "`([^`]+)`", "[bgcolor=#17202b]$1[/bgcolor]");
		text = Regex.Replace(text, "^### (.+)$", "[font_size=19][b]$1[/b][/font_size]", RegexOptions.Multiline);
		text = Regex.Replace(text, "^## (.+)$", "[font_size=22][b]$1[/b][/font_size]", RegexOptions.Multiline);
		text = Regex.Replace(text, "^# (.+)$", "[font_size=26][b]$1[/b][/font_size]", RegexOptions.Multiline);
		text = Regex.Replace(text, "\\*\\*([^*]+)\\*\\*", "[b]$1[/b]");
		text = Regex.Replace(text, "(?<!\\*)\\*([^*]+)\\*", "[i]$1[/i]");
		text = Regex.Replace(text, "~~([^~]+)~~", "[s]$1[/s]");
		text = Regex.Replace(text, @"\b(https?://[^\s<]+)", "[url=$1][color=#58a6ff]$1[/color][/url]");
		text = Regex.Replace(text, "^[-*] (.+)$", "• $1", RegexOptions.Multiline);
		for (int index = 0; index < links.Count; index++)
		{
			(string label, string url) = links[index];
			text = text.Replace($"\u0001LINK{index}\u0002", $"[url={url}][color=#58a6ff]{label.Replace("[", "[lb]")}[/color][/url]");
		}
		return text;
	}
}

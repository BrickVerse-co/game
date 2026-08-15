using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileForumReply : PanelContainer
{
	public void Configure(string author, string timestamp, string markdown, bool nested = false)
	{
		GetNode<Label>("Padding/Layout/Header/Author").Text = author;
		GetNode<Label>("Padding/Layout/Header/Time").Text = timestamp;
		GetNode<MobileMarkdown>("Padding/Layout/Body").SetMarkdown(markdown);
		GetNode<MarginContainer>("Padding").AddThemeConstantOverride("margin_left", nested ? 28 : 12);
	}
}

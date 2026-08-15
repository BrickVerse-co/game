using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileForumCard : Button
{
	private TextureRect _icon = null!;
	private Label _title = null!;
	private Label _meta = null!;
	private Label _detail = null!;

	public override void _Ready()
	{
		_icon = GetNode<TextureRect>("Content/Icon");
		_title = GetNode<Label>("Content/Copy/Title");
		_meta = GetNode<Label>("Content/Copy/Meta");
		_detail = GetNode<Label>("Content/Copy/Detail");
		MobileMotion.Bind(this);
	}

	public void Configure(string title, string meta, string detail, string icon = "")
	{
		_title.Text = title;
		_meta.Text = meta;
		_detail.Text = detail;
		_detail.Visible = !string.IsNullOrWhiteSpace(detail);
		_icon.Texture = GD.Load<Texture2D>(IconFor(icon));
	}

	private static string IconFor(string icon)
	{
		// The website stores Font Awesome class strings (for example
		// "fa-solid fa-gamepad"), while older categories use short names.
		string value = icon.Trim().ToLowerInvariant();
		if (value.Contains("megaphone") || value.Contains("bullhorn")) return "res://assets/textures/ui-icons/flag.svg";
		if (value.Contains("controller") || value.Contains("joystick") || value.Contains("gamepad")) return "res://assets/textures/ui-icons/gamepad.svg";
		if (value.Contains("tools") || value.Contains("wrench")) return "res://assets/textures/ui-icons/tools.svg";
		if (value.Contains("question") || value.Contains("help")) return "res://assets/textures/ui-icons/book.svg";
		if (value.Contains("chat") || value.Contains("message") || value.Contains("comment")) return "res://assets/textures/ui-icons/comments.svg";
		if (value.Contains("bug")) return "res://assets/textures/ui-icons/bug.svg";
		return "res://assets/textures/ui-icons/folder.svg";
	}
}

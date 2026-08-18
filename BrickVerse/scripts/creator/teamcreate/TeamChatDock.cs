using Godot;

namespace BrickVerse.Creator.TeamCreate;

public sealed partial class TeamChatDock : VBoxContainer
{
	private VBoxContainer _messages = null!;
	private LineEdit _input = null!;
	private Label _emptyState = null!;
	private ScrollContainer _scroll = null!;

	public override void _Ready()
	{
		_messages = GetNode<VBoxContainer>("MessagesScroll/Margin/Messages");
		_input = GetNode<LineEdit>("Composer/Margin/Row/Input");
		_emptyState = GetNode<Label>("MessagesScroll/Margin/Messages/Empty");
		_scroll = GetNode<ScrollContainer>("MessagesScroll");
		GetNode<Button>("Composer/Margin/Row/Send").Pressed += Send;
		_input.TextSubmitted += _ => Send();
		_input.TextChanged += text => GetNode<Button>("Composer/Margin/Row/Send").Disabled = string.IsNullOrWhiteSpace(text);
		if (TeamCreateService.Instance != null) TeamCreateService.Instance.TeamChatMessage += OnMessage;
	}

	private void Send()
	{
		string message = _input.Text.Trim();
		if (string.IsNullOrWhiteSpace(message)) return;
		TeamCreateService.Instance?.SendTeamChat(message);
		_input.Clear();
	}

	private void OnMessage(string sender, string message)
	{
		_emptyState.Hide();
		string username = TeamCreateService.Instance?.ResolveChatUsername(sender) ?? "Unknown member";
		PanelContainer card = new();
		StyleBoxFlat surface = new() { BgColor = new Color("101925"), CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 };
		card.AddThemeStyleboxOverride("panel", surface);
		VBoxContainer content = new(); card.AddChild(content);
		Label author = new() { Text = username, Modulate = new Color("38a9ff") };
		author.AddThemeFontSizeOverride("font_size", 11);
		content.AddChild(author);
		content.AddChild(new Label { Text = message, AutowrapMode = TextServer.AutowrapMode.WordSmart });
		_messages.AddChild(card);
		CallDeferred(MethodName.ScrollToLatest);
	}

	private void ScrollToLatest() => _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
	public override void _ExitTree() { if (TeamCreateService.Instance != null) TeamCreateService.Instance.TeamChatMessage -= OnMessage; base._ExitTree(); }
}

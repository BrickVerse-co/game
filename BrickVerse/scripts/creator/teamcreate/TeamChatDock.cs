using Godot;

namespace BrickVerse.Creator.TeamCreate;

public sealed partial class TeamChatDock : VBoxContainer
{
	private static readonly PackedScene MessageScene = GD.Load<PackedScene>("res://scenes/creator/components/team_chat_message.tscn");
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
		TeamChatMessage card = MessageScene.Instantiate<TeamChatMessage>();
		_messages.AddChild(card);
		card.Setup(username, message, TeamCreateService.Instance?.ResolveChatHeadshot(sender) ?? "");
		CallDeferred(MethodName.ScrollToLatest);
	}

	private void ScrollToLatest() => _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
	public override void _ExitTree() { if (TeamCreateService.Instance != null) TeamCreateService.Instance.TeamChatMessage -= OnMessage; base._ExitTree(); }
}

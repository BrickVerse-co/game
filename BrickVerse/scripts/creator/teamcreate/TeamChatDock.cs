using Godot;

namespace BrickVerse.Creator.TeamCreate;

public sealed partial class TeamChatDock : VBoxContainer
{
	private VBoxContainer _messages = null!; private LineEdit _input = null!;
	public TeamChatDock() { Name = "Team Chat"; }
	public override void _Ready()
	{
		Label title = new() { Text = "TEAM CREATE CHAT", Modulate = new Color("9aa8ba") }; AddChild(title);
		ScrollContainer scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill }; AddChild(scroll);
		_messages = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(_messages);
		HBoxContainer composer = new(); AddChild(composer); _input = new LineEdit { PlaceholderText = "Message collaborators…", SizeFlagsHorizontal = SizeFlags.ExpandFill }; composer.AddChild(_input);
		Button send = new() { Text = "Send" }; composer.AddChild(send); send.Pressed += Send; _input.TextSubmitted += _ => Send();
		if (TeamCreateService.Instance != null) TeamCreateService.Instance.TeamChatMessage += OnMessage;
	}
	private void Send() { if (string.IsNullOrWhiteSpace(_input.Text)) return; TeamCreateService.Instance?.SendTeamChat(_input.Text); _input.Clear(); }
	private void OnMessage(string sender, string message) { _messages.AddChild(new Label { Text = $"{sender}: {message}", AutowrapMode = TextServer.AutowrapMode.WordSmart }); }
	public override void _ExitTree() { if (TeamCreateService.Instance != null) TeamCreateService.Instance.TeamChatMessage -= OnMessage; base._ExitTree(); }
}

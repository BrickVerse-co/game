using Godot;
using BrickVerse.Creator.UI;

namespace BrickVerse.Creator.TeamCreate;

public sealed partial class TeamCreateSessionWindow : Window
{
	private readonly TeamCreateService _service;
	private readonly VBoxContainer _memberList = new();
	private readonly Label _status = new();

	public TeamCreateSessionWindow(TeamCreateService service)
	{
		_service = service;
		Title = "Team Create Session";
		Size = new(430, 390);
		MinSize = new(360, 300);
		CloseRequested += Hide;

		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 18);
		margin.AddThemeConstantOverride("margin_top", 18);
		margin.AddThemeConstantOverride("margin_right", 18);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		AddChild(margin);

		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 12);
		margin.AddChild(layout);

		Label title = new() { Text = "Session Info" };
		title.AddThemeFontSizeOverride("font_size", 20);
		layout.AddChild(title);
		layout.AddChild(_status);
		Button stopFollowing = new()
		{
			Text = "Detach Camera",
			TooltipText = "Stop following a collaborator camera",
		};
		stopFollowing.Pressed += _service.StopFollowing;
		layout.AddChild(stopFollowing);

		PanelContainer warning = new();
		MarginContainer warningMargin = new();
		warningMargin.AddThemeConstantOverride("margin_left", 12);
		warningMargin.AddThemeConstantOverride("margin_top", 10);
		warningMargin.AddThemeConstantOverride("margin_right", 12);
		warningMargin.AddThemeConstantOverride("margin_bottom", 10);
		warning.AddChild(warningMargin);
		Label warningText = new()
		{
			Text = "Privacy: traffic is relayed through BrickVerse servers. Collaborators do not receive your IP address.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		warningMargin.AddChild(warningText);
		layout.AddChild(warning);

		Label usersTitle = new() { Text = "Connected collaborators" };
		usersTitle.AddThemeFontSizeOverride("font_size", 15);
		layout.AddChild(usersTitle);
		ScrollContainer scroll = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		scroll.AddChild(_memberList);
		_memberList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		layout.AddChild(scroll);
	}

	public void Refresh()
	{
		_status.Text = _service.Connected
			? $"Connected via private relay • {_service.Members.Count} user(s)"
			: "Team Create is off or not connected for this universe.";
		foreach (Node child in _memberList.GetChildren()) child.QueueFree();

		foreach (TeamCreateMember member in _service.Members)
		{
			HBoxContainer row = new();
			row.AddThemeConstantOverride("separation", 8);
			Label name = new()
			{
				Text = "●  " + member.Username,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			row.AddChild(name);
			Button follow = new()
			{
				Text = _service.FollowedMemberId == member.Id ? "Following" : "View Camera",
				Disabled = member.Camera == null,
				TooltipText = "Attach the Creator camera to this collaborator's latest camera",
			};
			string memberId = member.Id;
			follow.Pressed += () => _service.FollowMember(memberId);
			row.AddChild(follow);
			_memberList.AddChild(row);
		}

		if (_service.Members.Count == 0)
			_memberList.AddChild(new Label { Text = "No collaborators are currently connected." });
	}
}

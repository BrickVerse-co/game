// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Text.Json;
using BrickVerse.Shared;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class GuildMembersDialog : Window
{
	private VBoxContainer _rankGroups = null!;
	private Button _loadMore = null!;
	private Label _status = null!;
	private PackedScene _memberCardScene = null!;
	private string _guildId = "";
	private int _page = 1;
	private bool _loading;

	public override void _Ready()
	{
		_rankGroups = GetNode<VBoxContainer>("Layout/Scroll/RankGroups");
		_loadMore = GetNode<Button>("Layout/LoadMore");
		_status = GetNode<Label>("Layout/Status");
		_memberCardScene = GD.Load<PackedScene>("res://scenes/mobile/components/home/user_headshot_card.tscn");
		_loadMore.Pressed += () => { _page++; _ = LoadPage(); };
		GetNode<Button>("Layout/Header/Close").Pressed += QueueFree;
		CloseRequested += QueueFree;
	}

	public static GuildMembersDialog Open(Node owner, string guildId)
	{
		GuildMembersDialog dialog = GD.Load<PackedScene>("res://scenes/mobile/views/guild_members.tscn").Instantiate<GuildMembersDialog>();
		owner.GetTree().Root.AddChild(dialog);
		dialog._guildId = guildId;
		dialog.PopupCentered();
		_ = dialog.LoadPage();
		return dialog;
	}

	private async System.Threading.Tasks.Task LoadPage()
	{
		if (_loading || string.IsNullOrWhiteSpace(_guildId)) return;
		_loading = true;
		_loadMore.Disabled = true;
		_status.Text = _page == 1 ? "Loading members…" : "Loading more members…";
		try
		{
			using JsonDocument document = await BVAPI.GetJson($"/v3/social/guilds/{Uri.EscapeDataString(_guildId)}/members?limit=24&page={_page}");
			if (document.RootElement.TryGetProperty("members", out JsonElement members) && members.ValueKind == JsonValueKind.Array)
				foreach (JsonElement member in members.EnumerateArray()) AddMember(member);
			bool hasNext = document.RootElement.TryGetProperty("pagination", out JsonElement pagination)
				&& pagination.TryGetProperty("hasNextPage", out JsonElement next) && next.ValueKind == JsonValueKind.True;
			_loadMore.Visible = hasNext;
			_status.Text = _rankGroups.GetChildCount() == 0 ? "No members found." : "";
		}
		catch (Exception exception)
		{
			_page = Math.Max(1, _page - 1);
			_status.Text = "Members could not be loaded. Try again.";
			BV.PrintErr(exception);
		}
		finally { _loading = false; _loadMore.Disabled = false; }
	}

	private void AddMember(JsonElement member)
	{
		string userId = member.TryGetProperty("user", out JsonElement user) && user.TryGetProperty("id", out JsonElement id) ? id.ToString() : "";
		if (string.IsNullOrWhiteSpace(userId)) return;
		string username = user.TryGetProperty("username", out JsonElement usernameNode) ? usernameNode.GetString() ?? "Member" : "Member";
		string rank = member.TryGetProperty("rank", out JsonElement rankNode) && rankNode.TryGetProperty("name", out JsonElement rankName) ? rankName.GetString() ?? "Member" : "Member";
		UserHeadshotCard card = _memberCardScene.Instantiate<UserHeadshotCard>();
		card.UserID = userId;
		card.InitialUsername = username;
		GetOrCreateRank(rank).AddChild(card);
	}

	private HFlowContainer GetOrCreateRank(string rankName)
	{
		foreach (Node child in _rankGroups.GetChildren())
			if (child.GetMeta("rank_name", "").AsString() == rankName) return child.GetNode<HFlowContainer>("Members");
		VBoxContainer section = new();
		section.SetMeta("rank_name", rankName);
		section.AddThemeConstantOverride("separation", 8);
		Label title = new() { Text = rankName };
		title.AddThemeFontSizeOverride("font_size", 20);
		HFlowContainer flow = new() { Name = "Members" };
		flow.AddThemeConstantOverride("h_separation", 10);
		flow.AddThemeConstantOverride("v_separation", 12);
		section.AddChild(title);
		section.AddChild(flow);
		_rankGroups.AddChild(section);
		return flow;
	}
}

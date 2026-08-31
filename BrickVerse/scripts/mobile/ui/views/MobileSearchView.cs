// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Text.Json;
using BrickVerse.Shared;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileSearchView : MobileViewBase
{
	private LineEdit _query = null!;
	private VBoxContainer _results = null!;
	private Label _status = null!;
	private PackedScene _cardScene = null!;
	private int _version;
	private bool _friendsOnly;

	public override void _Ready()
	{
		_query = GetNode<LineEdit>("Layout/Search");
		_results = GetNode<VBoxContainer>("Layout/Scroll/Results");
		_status = GetNode<Label>("Layout/Status");
		_cardScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/list_card.tscn");
		_query.TextChanged += text => { if (text.Trim().Length >= 3) _ = Search(text); else ResetResults(); };
		GetNode<Button>("Layout/Header/Back").Pressed += () => MobileUI.Singleton.SwitchTo(MobileViewEnum.Home);
	}

	public override void ShowView(object? args)
	{
		_friendsOnly = MobileUI.Singleton.CurrentView == MobileViewEnum.AddFriend;
		GetNode<Label>("Layout/Header/Title").Text = _friendsOnly ? "Add friends" : "Search";
		_query.PlaceholderText = _friendsOnly ? "Search for users" : "Search users, worlds, guilds, and more";
		_query.GrabFocus();
	}

	private void ResetResults()
	{
		_version++;
		ClearResults();
		_status.Text = _query.Text.Length == 0 ? "Search BrickVerse" : "Type at least 3 characters";
	}

	private async System.Threading.Tasks.Task Search(string rawQuery)
	{
		int version = ++_version;
		_status.Text = "Searching…";
		try
		{
			using JsonDocument document = await BVAPI.GetJson($"/v3/search?q={Uri.EscapeDataString(rawQuery.Trim())}&limit=20");
			if (version != _version) return;
			ClearResults();
			int shown = 0;
			if (document.RootElement.TryGetProperty("results", out JsonElement results) && results.ValueKind == JsonValueKind.Array)
				foreach (JsonElement result in results.EnumerateArray())
				{
					string type = Read(result, "type");
					if (_friendsOnly && type != "user") continue;
					AddResult(result, type);
					shown++;
				}
			_status.Text = shown == 0 ? "No matching results." : "";
		}
		catch (Exception exception) { _status.Text = "Search failed. Try again."; BV.PrintErr(exception); }
	}

	private void AddResult(JsonElement result, string type)
	{
		string id = Read(result, "id");
		string name = Read(result, "name");
		string thumbnail = Read(result, "thumbnail");
		MobileListCard card = _cardScene.Instantiate<MobileListCard>();
		_results.AddChild(card);
		card.Configure(string.IsNullOrWhiteSpace(name) ? "Result" : name, TypeLabel(type), _friendsOnly ? "Tap to view profile and add friend" : "", thumbnail);
		card.Pressed += () => OpenResult(type, id);
		MobileMotion.Enter(card, _results.GetChildCount() - 1);
	}

	private void OpenResult(string type, string id)
	{
		switch (type)
		{
			case "user": MobileUI.Singleton.SwitchTo(MobileViewEnum.Profile, id); break;
			case "world" when long.TryParse(id, out long worldId): MobileUI.Singleton.SwitchTo(MobileViewEnum.PlaceInfo, worldId); break;
			case "guild": MobileUI.Singleton.SwitchTo(MobileViewEnum.GuildDetail, new MobileRecordDetailArgs("Loading guild…", "Guild", "Loading guild details…", "", MobileViewEnum.Search, id)); break;
			case "marketplace": MobileUI.Singleton.SwitchTo(MobileViewEnum.MarketplaceItem, id); break;
			default: OS.ShellOpen(Globals.MainEndpoint.PathJoin($"/{type}s/{Uri.EscapeDataString(id)}")); break;
		}
	}

	private void ClearResults() { foreach (Node child in _results.GetChildren()) child.QueueFree(); }
	private static string Read(JsonElement item, string name) => item.TryGetProperty(name, out JsonElement value) ? value.ToString() : "";
	private static string TypeLabel(string type) => type switch { "user" => "User", "world" => "World", "guild" => "Guild", "asset" => "Asset", "marketplace" => "Marketplace", _ => type };
}

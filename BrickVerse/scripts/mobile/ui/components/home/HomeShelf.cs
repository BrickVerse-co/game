// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Text.Json;
using BrickVerse.Shared;
using BrickVerse.Mobile.Utils;
using BrickVerse.Utils;
using BrickVerse.Schemas.API;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class HomeShelf : VBoxContainer
{
	[Export] public bool RecentWorlds { get; set; }
	private PackedScene _placeCardScene = null!;
	private PackedScene _friendCardScene = null!;
	private PackedScene _skeletonScene = null!;

	public override void _Ready()
	{
		_placeCardScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/place_card.tscn");
		_friendCardScene = GD.Load<PackedScene>("res://scenes/mobile/components/home/user_headshot_card.tscn");
		_skeletonScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/skeleton_card.tscn");
		BVMobileAuthAPI.UserAuthenticated += user => _ = LoadAsync();
		if (BVMobileAuthAPI.IsAuthenticated) _ = LoadAsync();
		else Visible = false;
	}

	private async System.Threading.Tasks.Task LoadAsync()
	{
		HBoxContainer items = GetNode<HBoxContainer>("ScrollContainer/HBoxContainer2");
		Label count = GetNode<Label>("VBoxContainer/Label2");
		foreach (Node child in items.GetChildren()) child.QueueFree();
		Visible = true;
		for (int index = 0; index < 3; index++) items.AddChild(_skeletonScene.Instantiate());
		try
		{
			using JsonDocument document = await BVAPI.GetJson(
				RecentWorlds ? "/v3/worlds/recent" : "/v3/social/friends/online?limit=12"
			);
			string propertyName = RecentWorlds ? "worlds" : "friends";
			JsonElement records = document.RootElement.GetProperty(propertyName);
			foreach (Node child in items.GetChildren()) child.QueueFree();
			count.Text = RecentWorlds ? "Continue Playing" : $"Friends Online ({records.GetArrayLength()})";
			foreach (JsonElement record in records.EnumerateArray())
			{
				JsonElement data = !RecentWorlds && record.TryGetProperty("user", out JsonElement user) ? user : record;
				string id = data.TryGetProperty("id", out JsonElement idNode) ? idNode.ToString() : "";
				if (RecentWorlds && long.TryParse(id, out long worldId))
				{
					PlaceCard card = _placeCardScene.Instantiate<PlaceCard>();
					card.PlaceData = new APIWorldsData { Id = worldId, Name = ReadString(data, "name"), Playing = 0, Rating = null };
					card.ThumbnailUrl = ReadString(data, "thumbnailUrl");
					if (string.IsNullOrWhiteSpace(card.ThumbnailUrl))
					{
						string universeIdText = ReadString(data, "universeId");
						if (long.TryParse(universeIdText, out long universeId)) card.ThumbnailUrl = await BVAPI.GetUniverseThumbnailUrl(universeId);
					}
					items.AddChild(card);
				}
				else if (!RecentWorlds && !string.IsNullOrWhiteSpace(id))
				{
					UserHeadshotCard card = _friendCardScene.Instantiate<UserHeadshotCard>();
					card.UserID = id;
					items.AddChild(card);
				}
			}
			Visible = records.GetArrayLength() > 0;
		}
		catch (Exception exception)
		{
			BV.PrintErr("Could not load home shelf: ", exception);
			Visible = false;
		}
	}

	public void Refresh() => _ = LoadAsync();

	private static string ReadString(JsonElement item, string name) => item.TryGetProperty(name, out JsonElement value)
		? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()
		: "";
}

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Text.Json;
using System.Threading.Tasks;
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
	private PackedScene _friendSkeletonScene = null!;

	public override void _Ready()
	{
		_placeCardScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/place_card.tscn");
		_friendCardScene = GD.Load<PackedScene>("res://scenes/mobile/components/home/user_headshot_card.tscn");
		_skeletonScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/skeleton_card.tscn");
		_friendSkeletonScene = GD.Load<PackedScene>("res://scenes/mobile/components/home/friend_skeleton.tscn");
		BVMobileAuthAPI.UserAuthenticated += user => _ = LoadAsync();
		if (BVMobileAuthAPI.IsAuthenticated) _ = LoadAsync();
		else Visible = false;
	}

	private async System.Threading.Tasks.Task LoadAsync()
	{
		HBoxContainer items = GetNode<HBoxContainer>("ScrollContainer/HBoxContainer2");
		Label count = GetNodeOrNull<Label>("VBoxContainer/TitleRow/Label2") ?? GetNode<Label>("VBoxContainer/Label2");
		Button? viewAll = GetNodeOrNull<Button>("VBoxContainer/TitleRow/ViewAll");
		if (viewAll != null && !viewAll.HasMeta("bound"))
		{
			viewAll.SetMeta("bound", true);
			viewAll.Pressed += () => MobileUI.Singleton.SwitchTo(MobileViewEnum.Friends, MobileViewEnum.Friends);
			MobileMotion.Bind(viewAll);
		}
		ClearChildren(items);
		Visible = true;
		for (int index = 0; index < 3; index++) items.AddChild((RecentWorlds ? _skeletonScene : _friendSkeletonScene).Instantiate());
		try
		{
			using JsonDocument document = await BVAPI.GetJson(
				RecentWorlds ? "/v3/worlds/recent" : "/v3/social/friends/online?limit=12"
			);
			string propertyName = RecentWorlds ? "worlds" : "friends";
			JsonElement records = document.RootElement.GetProperty(propertyName);
			await RunOnMainThread(() =>
			{
				if (!IsInstanceValid(items) || !IsInstanceValid(count)) return;
				ClearChildren(items);
				count.Text = RecentWorlds ? "Continue Playing" : $"Friends Online ({records.GetArrayLength()})";
			});
			foreach (JsonElement record in records.EnumerateArray())
			{
				JsonElement data = !RecentWorlds && record.TryGetProperty("user", out JsonElement user) ? user : record;
				string id = data.TryGetProperty("id", out JsonElement idNode) ? idNode.ToString() : "";
				if (RecentWorlds && long.TryParse(id, out long worldId))
				{
					string worldName = ReadString(data, "name");
					string thumbnailUrl = ReadString(data, "thumbnailUrl");
					if (string.IsNullOrWhiteSpace(thumbnailUrl))
					{
						string universeIdText = ReadString(data, "universeId");
						if (long.TryParse(universeIdText, out long universeId)) thumbnailUrl = await BVAPI.GetUniverseThumbnailUrl(universeId);
					}
					await RunOnMainThread(() =>
					{
						if (!IsInstanceValid(items)) return;
						PlaceCard card = _placeCardScene.Instantiate<PlaceCard>();
						card.PlaceData = new APIWorldsData { Id = worldId, Name = worldName, Playing = 0, Rating = null };
						card.ThumbnailUrl = thumbnailUrl;
						items.AddChild(card);
					});
				}
				else if (!RecentWorlds && !string.IsNullOrWhiteSpace(id))
				{
					string username = ReadString(data, "username");
					bool isVerified = data.TryGetProperty("isVerified", out JsonElement verified) && verified.ValueKind == JsonValueKind.True;
					bool isAdmin = data.TryGetProperty("isStaff", out JsonElement staff) && staff.ValueKind == JsonValueKind.True;
					string presence = ReadString(record, "state");
					long joinWorldId = 0;
					if (record.TryGetProperty("currentGame", out JsonElement currentGame) && currentGame.ValueKind == JsonValueKind.Object)
					{
						string worldIdText = ReadString(currentGame, "worldId");
						if (!long.TryParse(worldIdText, out joinWorldId))
						{
							string universeIdText = ReadString(currentGame, "universeId");
							long.TryParse(universeIdText, out joinWorldId);
						}
					}
					await RunOnMainThread(() =>
					{
						if (!IsInstanceValid(items)) return;
						UserHeadshotCard card = _friendCardScene.Instantiate<UserHeadshotCard>();
						card.UserID = id;
						card.InitialUsername = username;
						card.IsVerified = isVerified;
						card.IsAdmin = isAdmin;
						card.InitialPresence = presence;
						card.JoinWorldID = joinWorldId;
						items.AddChild(card);
					});
				}
			}
			await RunOnMainThread(() => { if (IsInstanceValid(this)) Visible = records.GetArrayLength() > 0; });
		}
		catch (Exception exception)
		{
			BV.PrintErr("Could not load home shelf: ", exception);
			await RunOnMainThread(() => { if (IsInstanceValid(this)) Visible = false; });
		}
	}

	public void Refresh() => _ = LoadAsync();

	private static void ClearChildren(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			parent.RemoveChild(child);
			child.QueueFree();
		}
	}

	private static string ReadString(JsonElement item, string name) => item.TryGetProperty(name, out JsonElement value)
		? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()
		: "";

	private static Task RunOnMainThread(Action action)
	{
		TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		BV.CallOnMainThread(() =>
		{
			try { action(); completion.SetResult(); }
			catch (Exception exception) { completion.SetException(exception); }
		});
		return completion.Task;
	}
}

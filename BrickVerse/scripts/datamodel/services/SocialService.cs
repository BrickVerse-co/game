// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Client.WebAPI;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Networking;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace BrickVerse.Datamodel.Services;

[Static("Social")]
[ExplorerExclude]
[SaveIgnore]
public sealed partial class SocialService : Instance
{
	private readonly BVHttpClient _client = new();
	public readonly Dictionary<string, FileLinkAsset> FileLinks = [];

	public void LocalSendFriendshipRequest(Player recipient, FriendshipRequestType req)
	{
		RpcId(1, nameof(NetRecvFriendshipRequest), recipient.UserID, (int)req);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private async void NetRecvFriendshipRequest(string recipientID, int req)
	{
		FriendshipRequestType reqType = (FriendshipRequestType)req;
		Player? from = Root.Players.GetPlayerFromPeerID(RemoteSenderId);
		Player? to = Root.Players.GetPlayerByID(recipientID);

		if (from != null && to != null)
		{
			try
			{
				await WebSendFriendshipRequest(from.UserID, to.UserID, reqType);
				if (reqType == FriendshipRequestType.Friend)
				{
					RpcId(from.PeerID, nameof(RecvFriendRequestSuccess), to.UserID);
					RpcId(to.PeerID, nameof(RecvFriendRequestNotify), from.UserID);
				}
			}
			catch (Exception ex)
			{
				GD.PushError(ex);
				RpcId(from.PeerID, nameof(RecvFriendRequestFailure));
			}
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private async void RecvFriendRequestSuccess(string toUserID)
	{
		Player? to = Root.Players.GetPlayerByID(toUserID);
		if (to != null)
		{
			Root.CoreUI.CoreUI.NotificationCenter.FireMessage("You just sent a friend request to " + to.Name, "Friend Request Sent!");
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private async void RecvFriendRequestFailure()
	{
		Root.CoreUI.CoreUI.NotificationCenter.FireMessage("Something went wrong, please try again.", "Cannot send friend request");
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private async void RecvFriendRequestNotify(string fromUserID)
	{
		Player? from = Root.Players.GetPlayerByID(fromUserID);
		if (from != null)
		{
			Root.CoreUI.CoreUI.NotificationCenter.FireMessage(from.Name + " just send you a friend request!", "Friend Request");
		}
	}

	public async Task WebSendFriendshipRequest(string senderID, string recipientID, FriendshipRequestType req)
	{
		_client.DefaultRequestHeaders["Authorization"] = ServerAPI.GetAuthorizationHeaderValue();

		if (req == FriendshipRequestType.Friend)
		{
			string body = JsonSerializer.Serialize(new
			{
				friendId = recipientID,
				turnstileToken = "world-server",
			});

			HttpRequestMessage msg = new(HttpMethod.Post, Globals.ApiEndpoint.PathJoin("/v3/social/friends/request"));
			msg.Content = new StringContent(body, new MediaTypeHeaderValue("application/json"));
			await _client.SendAsync(msg);
			return;
		}

		if (req == FriendshipRequestType.Unfriend)
		{
			HttpRequestMessage msg = new(HttpMethod.Delete, Globals.ApiEndpoint.PathJoin("/v3/social/friends/" + recipientID));
			msg.Content = new StringContent("{\"turnstileToken\":\"world-server\"}", new MediaTypeHeaderValue("application/json"));
			await _client.SendAsync(msg);
			return;
		}

		throw new NotSupportedException("Unsupported relationship type");
	}

	public async Task<bool> WebCheckAreFriends(string fromID, string toID)
	{
		string data = await _client.GetStringAsync(Globals.ApiEndpoint.PathJoin($"/v3/social/friends/user/{fromID}"));
		using JsonDocument doc = JsonDocument.Parse(data);
		if (!doc.RootElement.TryGetProperty("friends", out JsonElement friends) || friends.ValueKind != JsonValueKind.Array)
		{
			return false;
		}

		foreach (JsonElement friend in friends.EnumerateArray())
		{
			if (friend.TryGetProperty("id", out JsonElement idNode) && idNode.GetString() == toID.ToString())
			{
				return true;
			}
		}

		return false;
	}

	public enum FriendshipRequestType
	{
		Friend,
		Unfriend,
		Block
	}
}

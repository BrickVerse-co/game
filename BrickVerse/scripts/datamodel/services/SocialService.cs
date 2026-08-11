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
using System.Diagnostics.CodeAnalysis;

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

	[RequiresUnreferencedCode("Serializing friend request payloads relies on reflection-driven JSON metadata.")]
	[RequiresDynamicCode("Serializing friend request payloads may require runtime code generation.")]
	public async Task WebSendFriendshipRequest(string senderID, string recipientID, FriendshipRequestType req)
	{
		_client.DefaultRequestHeaders["Authorization"] = ServerAPI.GetAuthorizationHeaderValue();
		string body = JsonSerializer.Serialize(
			new SocialFriendRequest { SenderId = senderID, RecipientId = recipientID },
			ServerAPIGenerationContext.Default.SocialFriendRequest
		);

		if (req == FriendshipRequestType.Friend)
		{
			using HttpRequestMessage msg = new(HttpMethod.Post, Globals.ApiEndpoint.PathJoin("/v3/world/server/social/friends/request"));
			msg.Content = new StringContent(body, new MediaTypeHeaderValue("application/json"));
			using HttpResponseMessage response = await _client.SendAsync(msg);
			response.EnsureSuccessStatusCode();
			return;
		}

		if (req == FriendshipRequestType.Unfriend)
		{
			using HttpRequestMessage msg = new(HttpMethod.Delete, Globals.ApiEndpoint.PathJoin("/v3/world/server/social/friends"));
			msg.Content = new StringContent(body, new MediaTypeHeaderValue("application/json"));
			using HttpResponseMessage response = await _client.SendAsync(msg);
			response.EnsureSuccessStatusCode();
			return;
		}

		throw new NotSupportedException("Unsupported relationship type");
	}

	public async Task<bool> WebCheckAreFriends(string fromID, string toID)
	{
		_client.DefaultRequestHeaders["Authorization"] = ServerAPI.GetAuthorizationHeaderValue();
		string data = await _client.GetStringAsync(Globals.ApiEndpoint.PathJoin(
			$"/v3/world/server/social/are-friends?userId={Uri.EscapeDataString(fromID)}&otherUserId={Uri.EscapeDataString(toID)}"
		));
		using JsonDocument doc = JsonDocument.Parse(data);
		return doc.RootElement.TryGetProperty("areFriends", out JsonElement areFriends)
			&& areFriends.ValueKind == JsonValueKind.True;
	}

	public enum FriendshipRequestType
	{
		Friend,
		Unfriend,
		Block
	}
}

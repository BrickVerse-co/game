// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Data;
using BrickVerse.Networking;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class NetworkEvent : Instance
{
	private bool _reliable;
	private bool _rateLimitEnabled;
	private int _rateLimitMaxRequests = 30;
	private float _rateLimitWindowSeconds = 1f;
	private RateLimitScopeEnum _rateLimitScope;
	private bool _logRateLimitRejections;
	private readonly Dictionary<int, Queue<ulong>> _requestTimesByPeer = [];
	private readonly Queue<ulong> _requestTimesForServer = new();

	/// <summary>Enables server-side limiting of client invocations. Disabled by default.</summary>
	[Editable, ScriptProperty, DefaultValue(false)]
	public bool RateLimitEnabled
	{
		get => _rateLimitEnabled;
		set
		{
			_rateLimitEnabled = value;
			OnPropertyChanged();
		}
	}

	/// <summary>Maximum accepted requests during each configured rate-limit window.</summary>
	[Editable, ScriptProperty, DefaultValue(30)]
	public int RateLimitMaxRequests
	{
		get => _rateLimitMaxRequests;
		set { _rateLimitMaxRequests = Math.Clamp(value, 1, 10000); OnPropertyChanged(); }
	}

	/// <summary>Length of the sliding rate-limit window, in seconds.</summary>
	[Editable, ScriptProperty, DefaultValue(1f)]
	public float RateLimitWindowSeconds
	{
		get => _rateLimitWindowSeconds;
		set { _rateLimitWindowSeconds = Math.Clamp(value, 0.1f, 60f); OnPropertyChanged(); }
	}

	/// <summary>Whether request capacity is tracked separately per player or shared by the server.</summary>
	[Editable, ScriptProperty, DefaultValue(RateLimitScopeEnum.PerPlayer)]
	public RateLimitScopeEnum RateLimitScope
	{
		get => _rateLimitScope;
		set { _rateLimitScope = value; OnPropertyChanged(); }
	}

	/// <summary>Writes a warning when a request is rejected. Disabled to avoid log spam by default.</summary>
	[Editable, ScriptProperty, DefaultValue(false)]
	public bool LogRateLimitRejections
	{
		get => _logRateLimitRejections;
		set { _logRateLimitRejections = value; OnPropertyChanged(); }
	}

	/// <summary>
	/// Fires when the server receives a message from the client.
	/// </summary>
	[ScriptProperty] public BVSignal<Player, NetMessage> InvokedServer { get; private set; } = new();
	/// <summary>
	/// Fires when the client receives a message from the server.
	/// </summary>
	[ScriptProperty] public BVSignal<NetMessage> InvokedClient { get; private set; } = new();

	/// <summary>
	/// Fires when the client receives a message from the server.
	/// </summary>
	[ScriptLegacyProperty("InvokedClient")] public BVSignal LegacyInvokedClient { get; private set; } = new();

	/// <summary>
	/// Determine whether this network event should send messages reliably. It's recommended to enable this option when sending a large number of messages.
	/// </summary>
	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Reliable
	{
		get => _reliable;
		set
		{
			_reliable = value;
			OnPropertyChanged();
		}
	}

	/// <summary>
	/// Sends a network event to the server from the client.
	/// </summary>
	/// <param name="msg"></param>
	[ScriptMethod]
	public void InvokeServer(object? payload = null, object? _ = null)
	{
		if (Root.Network.IsServer) throw new System.InvalidOperationException("InvokeServer can only be called from client");
		NetMessage msg = NetMessage.FromObject(payload);

		if (Reliable)
		{
			RpcId(1, nameof(NetServerRecvMsg), msg.Serialize());
		}
		else
		{
			RpcId(1, nameof(NetServerRecvMsgUnreliable), msg.Serialize());
		}
	}

	/// <summary>
	/// Sends a network event to a specific player from the server
	/// </summary>
	/// <param name="msg">message</param>
	/// <param name="player">player</param>
	/// <exception cref="System.InvalidOperationException"></exception>
	[ScriptMethod]
	public void InvokeClient(object? payload = null, Player? player = null)
	{
		if (!Root.Network.IsServer) throw new System.InvalidOperationException("InvokeClient can only be called from server");
		ArgumentNullException.ThrowIfNull(player);
		NetMessage msg = NetMessage.FromObject(payload);

		if (Reliable)
		{
			RpcId(player.PeerID, nameof(NetClientRecvMsg), msg.Serialize());
		}
		else
		{
			RpcId(player.PeerID, nameof(NetClientRecvMsgUnreliable), msg.Serialize());
		}
	}

	/// <summary>
	/// Sends a network event to all players from the server.
	/// </summary>
	/// <param name="msg">NetMessage to send</param>
	/// <exception cref="System.InvalidOperationException"></exception>
	[ScriptMethod]
	public void InvokeClients(object? payload = null)
	{
		if (!Root.Network.IsServer) throw new System.InvalidOperationException("InvokeClients can only be called from server");
		NetMessage msg = NetMessage.FromObject(payload);

		if (Reliable)
		{
			Rpc(nameof(NetClientRecvMsg), msg.Serialize());
		}
		else
		{
			Rpc(nameof(NetClientRecvMsgUnreliable), msg.Serialize());
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetClientRecvMsg(byte[] rawdata)
	{
		RecvMsg(rawdata, RemoteSenderId);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetClientRecvMsgUnreliable(byte[] rawdata)
	{
		RecvMsg(rawdata, RemoteSenderId);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetServerRecvMsg(byte[] rawdata)
	{
		RecvMsg(rawdata, RemoteSenderId);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetServerRecvMsgUnreliable(byte[] rawdata)
	{
		RecvMsg(rawdata, RemoteSenderId);
	}

	private async void RecvMsg(byte[] rawdata, int sentBy)
	{
		try
		{
			if (Root.Network.IsServer && !AcceptClientRequest(sentBy)) return;
			NetMessage msg = await NetMessage.Deserialize(rawdata);

			if (Root.Network.IsServer)
			{
				Player? plr = Root.Players.GetPlayerFromPeerID(sentBy);
				if (plr != null)
				{
					InvokedServer.Invoke(plr, msg);
				}
			}
			else
			{
				LegacyInvokedClient.Invoke(null, msg);
				InvokedClient.Invoke(msg);
			}
		}
		catch (Exception e)
		{
			GD.PushError(e);
		}
	}

	private bool AcceptClientRequest(int peerId)
	{
		if (!RateLimitEnabled) return true;
		ulong now = Time.GetTicksMsec();
		Queue<ulong> requests;
		if (RateLimitScope == RateLimitScopeEnum.PerServer)
			requests = _requestTimesForServer;
		else if (!_requestTimesByPeer.TryGetValue(peerId, out requests!))
			_requestTimesByPeer[peerId] = requests = new Queue<ulong>();
		ulong windowMilliseconds = (ulong)Math.Max(100, RateLimitWindowSeconds * 1000f);
		while (requests.Count > 0 && now - requests.Peek() >= windowMilliseconds) requests.Dequeue();
		if (requests.Count >= RateLimitMaxRequests)
		{
			if (LogRateLimitRejections)
				BV.PrintWarn($"NetworkEvent {LuaPath} rejected a {RateLimitScope} rate-limited request from peer {peerId}.");
			return false;
		}
		requests.Enqueue(now);
		return true;
	}

	[ScriptEnum("NetworkEventRateLimitScope")]
	public enum RateLimitScopeEnum
	{
		PerPlayer,
		PerServer,
	}
}

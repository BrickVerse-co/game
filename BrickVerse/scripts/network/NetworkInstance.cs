// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Networking;

/// <summary>
/// ENet network instance
/// </summary>
public class NetworkInstance
{
	private const float SilenceTimeoutSeconds = 5.0f;
	private const int DataChannelAuthTimeoutMs = 10000;
	private const ENetConnection.CompressionMode CompressionMode = ENetConnection
		.CompressionMode
		.Zlib;
	private const int BandwidthInLimit = 0;

	// A small advertised bandwidth also shrinks ENet's reliable window. Keep
	// protocol acknowledgements flowing independently of gameplay rate limits.
	private const int BandwidthOutLimit = 0;
	private const int BandwidthPerPlayer = 200 * 1024; // 200 KB/s per player
	private long _lastMessageTicks = DateTime.UtcNow.Ticks;
	private readonly Dictionary<int, string> _dataServerTokens = [];

	private const int DefaultCapacity = 67;
	private const int DefaultPort = 21441;
	private const int MinimumTimeout = 5;
	private const int ActionsPerPoll = 32;
	private const int EventsPerPoll = 256;
	private const int FramesPerPoll = 32;
	private readonly Dictionary<
		(ENetPacketPeer Peer, int Channel),
		Queue<PendingSend>
	> _pendingSends = [];
	private int _nextSendChannel;
	private readonly NetworkPacketFraming _framing = new();
	private int _nextMessageId;
	private long _lastStatisticsPoll;
	private readonly ConcurrentDictionary<ENetConnection.HostStatistic, double> _statistics = new();
	private static readonly ENetConnection.HostStatistic[] HostStatistics =
		Enum.GetValues<ENetConnection.HostStatistic>();

	private sealed class PendingSend(IEnumerator<byte[]> frames, TransferMode mode)
	{
		public IEnumerator<byte[]> Frames { get; } = frames;
		public TransferMode Mode { get; } = mode;
		public bool HasFrame;
	}

	private readonly ENetConnection _peer;

	private readonly ConcurrentQueue<Action> _actionQueue = new();
	private Task? _networkLoopTask;
	internal readonly ConcurrentDictionary<int, ENetPacketPeer> IdToPeer = [];
	internal readonly ConcurrentDictionary<ENetPacketPeer, int> PeerToId = [];

	private readonly ConcurrentQueue<DeferredNetworkEvent> _mainThreadEventQueue = new();
	private int _mainThreadDrainScheduled = 0;

	public ICollection<int> PeerIds => IdToPeer.Keys;

	private int _peerCounter = 1;

	public event Action<int>? PeerConnected;
	public event Action<int>? PeerDisconnected;
	public event Action? ClientConnected;
	public event Action? ClientDisconnected;
	public event Action<NetInstanceErrorEnum>? ClientError;
	public event MessageReceivedHandler? MessageReceived;

	public bool IsSilence { get; private set; } = false;
	public bool IsServer { get; private set; } = false;
	private int _shutdownState;
	private bool IsShutdown => Volatile.Read(ref _shutdownState) != 0;

	public NetworkInstance()
	{
		_peer = new();
	}

	public void CreateServer(int port = DefaultPort, int maxChannels = 3)
	{
		Error e = _peer.CreateHostBound("*", port, DefaultCapacity, maxChannels);
		_peer.Compress(CompressionMode);

		if (e != Error.Ok)
		{
			throw new InvalidOperationException(
				$"Couldn't bind ENet server to UDP port {port}: {e}"
			);
		}

		IsServer = true;

		PostPeerCreate();
	}

	public async Task CreateClient(string address, int port, int maxChannels = 3)
	{
		Error e = _peer.CreateHost(DefaultCapacity, maxChannels);
		_peer.BandwidthLimit(BandwidthInLimit, BandwidthOutLimit);
		_peer.Compress(CompressionMode);

		if (e != Error.Ok)
		{
			BV.PrintErr("Couldn't create host: ", e);
			return;
		}

		_peer.ConnectToHost(address, port);

		PostPeerCreate();
	}

	/// <summary>
	/// Adapt server bandwidth to player count
	/// </summary>
	/// <param name="_">used to be player count</param>
	public void AdaptBandwidth(int _)
	{
		if (!IsShutdown)
			_actionQueue.Enqueue(() => _peer.BandwidthLimit(0, 0));
	}

	private void PostPeerCreate()
	{
		_networkLoopTask = Task.Run(NetworkLoop);
	}

	internal bool VerifyDataServerToken(int peerID, string token)
	{
		if (_dataServerTokens.TryGetValue(peerID, out var val))
		{
			if (val == token)
			{
				// DataServer Token success, remove the token too
				_dataServerTokens.Remove(peerID);
				return true;
			}
		}
		return false;
	}

	public ENetPacketPeer? GetPacketPeerFromId(int id)
	{
		if (IdToPeer.TryGetValue(id, out ENetPacketPeer? p))
		{
			return p;
		}
		return null;
	}

	public void SendMessage(
		int targetID,
		byte[] data,
		TransferMode transferMode,
		int transferChannel = 0
	)
	{
		if (IsShutdown)
			return;
		_actionQueue.Enqueue(() =>
		{
			ENetPacketPeer? peer = GetPacketPeerFromId(targetID);
			if (peer == null)
			{
				GD.PushWarning(targetID, " doesn't exist");
				return;
			}
			SendFramedMessage(peer, data, transferMode, transferChannel);
		});
	}

	public void DisconnectPeer(int targetID, bool force = false)
	{
		if (IsShutdown)
			return;
		_actionQueue.Enqueue(() =>
		{
			ENetPacketPeer? peer = GetPacketPeerFromId(targetID);
			if (peer == null)
			{
				GD.PushWarning(targetID, " doesn't exist");
				return;
			}
			if (force)
			{
				peer.PeerDisconnectNow();
			}
			else
			{
				peer.PeerDisconnect();
			}
		});
	}

	public void Shutdown()
	{
		if (Interlocked.Exchange(ref _shutdownState, 1) != 0)
			return;
		// ENetConnection is not safe to destroy while Service/Flush is running.
		// Let the worker observe shutdown and exit before touching the peer here.
		try
		{
			_networkLoopTask?.GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			GD.PushError("Network loop failed during shutdown: ", ex);
		}
		foreach ((_, ENetPacketPeer pk) in IdToPeer)
		{
			pk.PeerDisconnect();
		}
		_peer.Flush();
		_peer.Destroy();
		IdToPeer.Clear();
		PeerToId.Clear();
		foreach (var queue in _pendingSends.Values)
			while (queue.TryDequeue(out var message))
				message.Frames.Dispose();
		_pendingSends.Clear();
		_dataServerTokens.Clear();
		while (_actionQueue.TryDequeue(out _)) { }
		while (_mainThreadEventQueue.TryDequeue(out _)) { }
		Interlocked.Exchange(ref _mainThreadDrainScheduled, 0);
		_networkLoopTask = null;
	}

	public void BroadcastMessage(
		byte[] data,
		TransferMode transferMode,
		int transferChannel = 0,
		int[]? except = null
	)
	{
		if (IsShutdown)
			return;
		_actionQueue.Enqueue(() =>
		{
			foreach ((int id, ENetPacketPeer? peer) in IdToPeer)
			{
				if (!peer.IsActive())
					continue;
				if (except != null && except.Contains(id))
					continue;
				SendFramedMessage(peer, data, transferMode, transferChannel);
			}
		});
	}

	private void SendFramedMessage(ENetPacketPeer peer, byte[] data, TransferMode mode, int channel)
	{
		if (data.Length == 0 || data.Length > NetworkPacketFraming.MaxMessageSize)
			throw new InvalidOperationException("Invalid outbound network message length.");
		uint id = unchecked((uint)Interlocked.Increment(ref _nextMessageId));
		var key = (peer, channel);
		if (!_pendingSends.TryGetValue(key, out var queue))
		{
			queue = new();
			_pendingSends.Add(key, queue);
		}
		queue.Enqueue(new PendingSend(NetworkPacketFraming.Encode(data, id).GetEnumerator(), mode));
	}

	private void ProcessOutgoingFrames()
	{
		var channels = _pendingSends.Keys.ToArray();
		if (channels.Length == 0)
			return;
		for (int work = 0; work < FramesPerPoll && !IsShutdown; work++)
		{
			int index = _nextSendChannel++ % channels.Length;
			if (_nextSendChannel == int.MaxValue)
				_nextSendChannel = 0;
			var key = channels[index];
			if (!_pendingSends.TryGetValue(key, out var queue))
				continue;
			var message = queue.Peek();
			if (!key.Peer.IsActive() || key.Peer.GetState() != ENetPacketPeer.PeerState.Connected)
			{
				DiscardPendingSends(key.Peer);
				continue;
			}
			if (!message.HasFrame && !message.Frames.MoveNext())
			{
				message.Frames.Dispose();
				queue.Dequeue();
				if (queue.Count == 0)
					_pendingSends.Remove(key);
				continue;
			}
			message.HasFrame = true;
			// Preserve message order within each peer/channel while polling the
			// socket between bounded batches, even for multi-megabyte messages.
			_peer.Flush();
			Error error = key.Peer.Send(key.Channel, message.Frames.Current, (int)message.Mode);
			if (error != Error.Ok)
			{
				GD.PushError($"ENet send failed: {error}");
				DiscardPendingSends(key.Peer);
				key.Peer.PeerDisconnect();
				continue; // Always return to Service so disconnects can complete.
			}
			_peer.Flush();
			message.HasFrame = false;
		}
	}

	private void DiscardPendingSends(ENetPacketPeer peer)
	{
		foreach (var key in _pendingSends.Keys.Where(key => key.Peer == peer).ToArray())
		{
			while (_pendingSends[key].TryDequeue(out var pending))
				pending.Frames.Dispose();
			_pendingSends.Remove(key);
		}
	}

	private void NetworkLoop()
	{
		while (true)
		{
			if (IsShutdown)
				return;
			if (!GodotObject.IsInstanceValid(_peer))
				return;
			try
			{
				ProcessActionQueue();
				ProcessOutgoingFrames();
				ProcessNetwork();
				CheckSilence();
				_peer.Flush();
				PollStatistics();
			}
			catch (Exception ex)
			{
				GD.PushError(ex);
			}
		}
	}

	public double PopStatistic(ENetConnection.HostStatistic statistic)
	{
		// UI readers consume cached counters; only the network worker may call
		// ENetConnection methods while Service/Flush are active.
		while (_statistics.TryGetValue(statistic, out double value))
			if (_statistics.TryUpdate(statistic, 0, value))
				return value;
		return 0;
	}

	private void PollStatistics()
	{
		long now = System.Environment.TickCount64;
		if (now - _lastStatisticsPoll < 250)
			return;
		_lastStatisticsPoll = now;
		_framing.Expire(now);
		foreach (var statistic in HostStatistics)
		{
			double value = _peer.PopStatistic(statistic);
			_statistics.AddOrUpdate(statistic, value, (_, previous) => previous + value);
		}
	}

	private void ProcessNetwork()
	{
		for (int processed = 0; processed < EventsPerPoll && !IsShutdown; processed++)
		{
			Godot.Collections.Array serviceData = _peer.Service(
				processed == 0 ? MinimumTimeout : 0
			);
			ENetConnection.EventType eventType = (ENetConnection.EventType)(int)serviceData[0];
			if (eventType == ENetConnection.EventType.None)
				break;
			ENetPacketPeer? fromPeer = (ENetPacketPeer?)serviceData[1];
			int peerID = 0;
			if (fromPeer != null)
			{
				if (PeerToId.TryGetValue(fromPeer, out int p))
				{
					peerID = p;
				}
			}

			if (eventType == ENetConnection.EventType.Connect)
			{
				if (fromPeer == null)
				{
					BV.PrintWarn("Connect received but peer is null, return");
					return;
				}

				if (!IsServer)
				{
					peerID = 1;
				}
				else
				{
					_peerCounter++;
					peerID = _peerCounter;
				}

				IdToPeer[peerID] = fromPeer;
				PeerToId[fromPeer] = peerID;
				// Let the application's 30-second progress watchdog decide stalled
				// joins; exponential transport backoff must not kill recoverable gaps.
				fromPeer.SetTimeout(32, 30_000, 60_000);

				if (IsServer)
				{
					EnqueueEvent(new PeerConnectedEvent(peerID));
				}
				else
				{
					EnqueueEvent(new ClientConnectedEvent());
				}
			}
			else if (eventType == ENetConnection.EventType.Disconnect)
			{
				if (fromPeer == null)
				{
					BV.PrintWarn("Disconnect received but peer is null, return");
					return;
				}
				IdToPeer.TryRemove(peerID, out _);
				PeerToId.TryRemove(fromPeer, out _);
				_framing.RemovePeer(peerID);
				if (IsServer)
				{
					EnqueueEvent(new PeerDisconnectedEvent(peerID));
				}
				else
				{
					EnqueueEvent(new ClientDisconnectedEvent());
				}
			}
			else if (eventType == ENetConnection.EventType.Receive)
			{
				Interlocked.Exchange(ref _lastMessageTicks, DateTime.UtcNow.Ticks);
				if (fromPeer == null)
				{
					BV.PrintWarn("Message received but peer is null, return");
					return;
				}
				while (fromPeer.GetAvailablePacketCount() > 0)
				{
					int pkf = fromPeer.GetPacketFlags();
					TransferMode m = pkf switch
					{
						(int)ENetPacketPeer.FlagReliable => TransferMode.Reliable,
						(int)ENetPacketPeer.FlagUnreliableFragment =>
							TransferMode.UnreliableOrdered,
						(int)ENetPacketPeer.FlagUnsequenced => TransferMode.Unreliable,
						_ => TransferMode.Unreliable,
					};
					byte[] packet = fromPeer.GetPacket();
					if (!NetworkPacketFraming.IsCompatible(packet))
					{
						GD.PushError($"Incompatible network framing from peer {peerID}.");
						if (IsServer)
							fromPeer.PeerDisconnect();
						else
							EnqueueEvent(
								new ClientErrorEvent(NetInstanceErrorEnum.ProtocolMismatch)
							);
						return;
					}
					byte[]? data;
					try
					{
						data = _framing.Decode(peerID, packet, m, System.Environment.TickCount64);
					}
					catch (System.IO.InvalidDataException exception)
					{
						GD.PushError(
							$"Invalid network frame from peer {peerID}: {exception.Message}"
						);
						_framing.RemovePeer(peerID);
						if (IsServer)
							fromPeer.PeerDisconnect();
						else
							EnqueueEvent(new ClientErrorEvent(NetInstanceErrorEnum.NetworkError));
						return;
					}
					if (data != null)
						EnqueueEvent(new MessageReceivedEvent(peerID, data, m));
				}
			}
			else if (eventType == ENetConnection.EventType.Error)
			{
				BV.PrintErr("Client error");
				EnqueueEvent(new ClientErrorEvent(NetInstanceErrorEnum.NetworkError));
				Thread.Sleep(MinimumTimeout);
				return; // A persistent host error must not trap the send/receive pump.
			}
			else if (eventType == ENetConnection.EventType.None)
				return;
		}
	}

	private void CheckSilence()
	{
		// Only check silence in client
		if (IsServer || IdToPeer.IsEmpty)
			return;

		long lastTicks = Interlocked.Read(ref _lastMessageTicks);
		double elapsedSeconds = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - lastTicks).TotalSeconds;

		bool currentlySilent = elapsedSeconds > SilenceTimeoutSeconds;

		if (currentlySilent != IsSilence)
		{
			IsSilence = currentlySilent;
			if (IsSilence)
			{
				BV.PrintErr("[!] Network connection has gone silent");
			}
			else
			{
				BV.Print("[i] Network connection resumed.");
			}
		}
	}

	private void ProcessActionQueue()
	{
		for (
			int processed = 0;
			processed < ActionsPerPoll
				&& !IsShutdown
				&& _actionQueue.TryDequeue(out Action? action);
			processed++
		)
		{
			try
			{
				action?.Invoke();
			}
			catch (Exception ex)
			{
				GD.PushError("Error processing queued action: ", ex);
			}
		}
	}

	private void EnqueueEvent(DeferredNetworkEvent e)
	{
		_mainThreadEventQueue.Enqueue(e);
		if (Interlocked.CompareExchange(ref _mainThreadDrainScheduled, 1, 0) == 0)
		{
			Callable.From(DrainEvents).CallDeferred();
		}
	}

	private void DrainEvents()
	{
		if (IsShutdown)
		{
			while (_mainThreadEventQueue.TryDequeue(out _)) { }
			Interlocked.Exchange(ref _mainThreadDrainScheduled, 0);
			return;
		}
		try
		{
			while (_mainThreadEventQueue.TryDequeue(out DeferredNetworkEvent? e))
			{
				switch (e)
				{
					case PeerConnectedEvent connected:
						string dataToken = Guid.NewGuid().ToString() + Guid.NewGuid().ToString();
						_dataServerTokens[connected.PeerID] = dataToken;
						PeerConnected?.Invoke(connected.PeerID);
						break;
					case PeerDisconnectedEvent disconnected:
						_dataServerTokens.Remove(disconnected.PeerID);
						PeerDisconnected?.Invoke(disconnected.PeerID);
						break;
					case ClientConnectedEvent:
						ClientConnected?.Invoke();
						break;
					case ClientDisconnectedEvent:
						ClientDisconnected?.Invoke();
						break;
					case ClientErrorEvent error:
						ClientError?.Invoke(error.Error);
						break;
					case MessageReceivedEvent msg:
						MessageReceived?.Invoke(msg.PeerID, msg.Data, msg.TransferMode);
						break;
				}
			}
		}
		finally
		{
			Interlocked.Exchange(ref _mainThreadDrainScheduled, 0);

			if (
				!_mainThreadEventQueue.IsEmpty
				&& Interlocked.CompareExchange(ref _mainThreadDrainScheduled, 1, 0) == 0
			)
			{
				Callable.From(DrainEvents).CallDeferred();
			}
		}
	}

	public bool IsPeerConnected(int peerID)
	{
		return IdToPeer.ContainsKey(peerID);
	}

	public delegate void MessageReceivedHandler(int peerID, byte[] data, TransferMode transferMode);

	public enum NetInstanceErrorEnum
	{
		DataChannelConnectFailure,
		DataChannelAuthFailure,
		NetworkError,
		ProtocolMismatch,
	}

	private abstract record DeferredNetworkEvent;

	private record PeerConnectedEvent(int PeerID) : DeferredNetworkEvent;

	private record PeerDisconnectedEvent(int PeerID) : DeferredNetworkEvent;

	private record ClientConnectedEvent : DeferredNetworkEvent;

	private record ClientDisconnectedEvent : DeferredNetworkEvent;

	private record ClientErrorEvent(NetInstanceErrorEnum Error) : DeferredNetworkEvent;

	private record MessageReceivedEvent(int PeerID, byte[] Data, TransferMode TransferMode)
		: DeferredNetworkEvent;
}

public enum AuthorityMode
{
	Server,
	Authority,
	Any,
}

public enum TransferMode
{
	Reliable = (int)ENetPacketPeer.FlagReliable,
	UnreliableOrdered = (int)ENetPacketPeer.FlagUnreliableFragment,
	Unreliable = (int)ENetPacketPeer.FlagUnsequenced,
}

public class NetworkException(string err) : Exception(err) { }

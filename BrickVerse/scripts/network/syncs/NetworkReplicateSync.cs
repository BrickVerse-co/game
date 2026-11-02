// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Services;
using BrickVerse.Shared;
using BrickVerse.Utils;
using BrickVerse.Utils.Compression;
using Godot;
using static BrickVerse.Datamodel.Services.NetworkService;

namespace BrickVerse.Networking.Synchronizers;

[Internal]
public sealed partial class NetworkReplicateSync : Instance
{
	private const int PlaceReplicationChunkSize = 300;
	private const int MaxPlaceReplicationChunkBytes = 256 * 1024;
	private const int MaxCompressedPlaceChunkBytes = 1000;
	private const int MaxIncomingChunkBytes = MaxPlaceReplicationChunkBytes;
	private const int MaxDecompressedChunkBytes = 512 * 1024;
	private const int MaxQueuedIncomingBytes = 16 * 1024 * 1024;
	private const int MaxPlaceReplicationObjects = 1_000_000;
	private const int MaxSingleReplicateBytes = 512 * 1024;
	private const int MaxPendingReplicateParents = 4096;
	private const int MaxPendingReplicatesPerParent = 1024;
	private const int PlaceChunksPerFrame = 4;
	private const int PlaceChunksInFlight = 8;
	private const int StablePlaceReplicationPolls = 2;
	private const ulong PlaceReplicationPollIntervalMs = 50;
	private const ulong PlaceReplicationStallTimeoutMs = 30_000;
	private const ulong PlaceReplicationMaximumDurationMs = 300_000;
	private const double StreamingPollIntervalSeconds = 0.25;
	internal NetworkService NetService = null!;

	// Queue for pending replicate data (waiting for node to spawn)
	private readonly Dictionary<string, List<NetReplicateData>> _pendingReplicates = [];

	public int InstanceLoadedCount = 1;
	public int InstanceToBeLoadedCount = 0;
	public event Action<int, int>? InstanceLoadedProgress;
	private readonly HashSet<string> _removedRef = [];
	private readonly HashSet<NetReplicateData> _worldReplicateSet = [];
	private readonly Queue<(
		byte[] Bytes,
		bool IsPlaceReplication,
		int Round,
		int Index,
		int Count,
		int ObjectCount,
		bool IsFinal
	)> _incomingChunks = new();
	private int _queuedIncomingBytes;
	private readonly Dictionary<int, PlaceReplicationSendSession> _placeReplicationSendSessions =
	[];
	private readonly Dictionary<int, StreamingPeerState> _streamingPeers = [];
	private double _streamingPollElapsed;
	private bool _processingIncomingChunk;
	private bool _hasProcessedIncomingChunk;
	private int _placeChunksReceived;
	private int _placeChunksExpected;
	private int _placeRound = -1;
	private int _placeRoundChunksReceived;
	private int _placeRoundChunksExpected;
	private bool _placeFinalRoundReceived;
	private ulong _placeReplicationStartedAtMsec;
	private ulong _lastReplicationProgressAtMsec;
	internal int PlaceChunksReceived => _placeChunksReceived;
	internal int PlaceChunksExpected => _placeChunksExpected;

	private sealed class PlaceReplicationSendSession(Player player, Action? completed)
	{
		public Player Player { get; } = player;
		public int PeerID => Player.PeerID;
		public byte[][] Chunks { get; set; } = [];
		public int ObjectCount { get; set; }
		public int Round { get; set; } = -1;
		public int NextChunkIndex { get; set; }
		public int AcknowledgedChunkCount { get; set; }
		public int StablePollCount { get; set; }
		public ulong NextPollAtMsec { get; set; }
		public bool IsFinalRound { get; set; }
		public HashSet<string> SentNetworkIDs { get; } = [];
		public Action? Completed { get; } = completed;
	}

	private sealed class StreamingPeerState(Player player, HashSet<string> residentIDs)
	{
		public Player Player { get; } = player;
		public HashSet<string> ResidentIDs { get; } = residentIDs;
	}

	private static readonly bool _useNetworkLog = false;

	static NetworkReplicateSync()
	{
		_useNetworkLog = OS.HasFeature("netlog");
	}

	public override void Init()
	{
		SetProcess(true);
		base.Init();
	}

	public void SyncPlaceToPlayer(Player plr, Action? completed = null)
	{
		NetService.Root.NetSendAllPropUpdate(plr.PeerID);

		PlaceReplicationSendSession session = new(plr, completed);
		_placeReplicationSendSessions[plr.PeerID] = session;
		PollPlaceReplication(session);
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvChunk(
		byte[] rawBytes,
		bool isPlaceReplicate,
		int round,
		int chunkIndex,
		int chunkCount,
		int objectCount,
		bool isFinal
	)
	{
		if (rawBytes == null || rawBytes.Length == 0 || rawBytes.Length > MaxIncomingChunkBytes)
		{
			RejectIncomingChunk($"invalid compressed size {rawBytes?.Length ?? 0}");
			return;
		}

		if (
			chunkCount <= 0
			|| chunkIndex < 0
			|| chunkIndex >= chunkCount
			|| objectCount < 0
			|| objectCount > MaxPlaceReplicationObjects
			|| chunkCount > Math.Max(1, objectCount)
		)
		{
			RejectIncomingChunk(
				$"invalid metadata index={chunkIndex}, count={chunkCount}, objects={objectCount}"
			);
			return;
		}

		if (isPlaceReplicate)
		{
			int queuedForRound = _incomingChunks.Count(chunk =>
				chunk.IsPlaceReplication && chunk.Round == round
			);
			if (
				round == _placeRound + 1
				&& chunkIndex == 0
				&& _placeRoundChunksReceived == _placeRoundChunksExpected
			)
			{
				_placeRound = round;
				_placeRoundChunksReceived = 0;
				_placeRoundChunksExpected = chunkCount;
				_placeChunksExpected += chunkCount;
				InstanceToBeLoadedCount += objectCount;
			}

			int expectedIndex = _placeRoundChunksReceived + queuedForRound;
			if (
				round != _placeRound
				|| chunkCount != _placeRoundChunksExpected
				|| chunkIndex != expectedIndex
			)
			{
				RejectIncomingChunk(
					$"inconsistent or out-of-order metadata round={round}, index={chunkIndex}, expectedRound={_placeRound}, expectedIndex={expectedIndex}, count={chunkCount}, objects={objectCount}"
				);
				return;
			}

			if (_placeReplicationStartedAtMsec == 0)
			{
				_placeReplicationStartedAtMsec = Time.GetTicksMsec();
				_lastReplicationProgressAtMsec = _placeReplicationStartedAtMsec;
			}
		}

		if (_queuedIncomingBytes > MaxQueuedIncomingBytes - rawBytes.Length)
		{
			RejectIncomingChunk("queued replication data exceeded the 16 MiB limit");
			return;
		}

		_incomingChunks.Enqueue(
			(rawBytes, isPlaceReplicate, round, chunkIndex, chunkCount, objectCount, isFinal)
		);
		_queuedIncomingBytes += rawBytes.Length;
		if (isPlaceReplicate)
			_lastReplicationProgressAtMsec = Time.GetTicksMsec();
	}

	private void RejectIncomingChunk(string reason)
	{
		BV.PrintErr("Rejected world replication chunk: ", reason);
		NetService.DisconnectSelf(
			"The server sent invalid world data. Please retry.",
			DisconnectionCodeEnum.ConnectionFailure
		);
	}

	private async void ProcessNextIncomingChunk()
	{
		if (_processingIncomingChunk || _incomingChunks.Count == 0)
			return;

		_processingIncomingChunk = true;
		try
		{
			// Let the scene finish replacing the loading world before applying objects.
			if (!_hasProcessedIncomingChunk)
			{
				await Globals.Singleton.WaitFrame();
				await Globals.Singleton.WaitFrame();
			}
			int processedObjects = 0;
			while (_incomingChunks.Count > 0 && processedObjects < PlaceReplicationChunkSize)
			{
				var incoming = _incomingChunks.Dequeue();
				_queuedIncomingBytes -= incoming.Bytes.Length;
				processedObjects += ProcessIncomingChunk(
					incoming.Bytes,
					incoming.IsPlaceReplication
				);
				_hasProcessedIncomingChunk = true;
				if (incoming.IsPlaceReplication)
				{
					_placeChunksReceived++;
					_placeRoundChunksReceived++;
					_placeFinalRoundReceived |= incoming.IsFinal;
					_lastReplicationProgressAtMsec = Time.GetTicksMsec();
					RpcId(1, nameof(NetAckPlaceReplicationChunk), incoming.Round, incoming.Index);
				}
			}
		}
		catch (Exception ex)
		{
			string reportID = Guid.NewGuid().ToString("N")[..8];
			BV.PrintErr(
				$"[JoinFailure:{reportID}] Failed to apply world data after {_placeChunksReceived}/{_placeChunksExpected} chunks: {ex}"
			);
			NetService.DisconnectSelf(
				$"Could not load world data. Please retry. If this keeps happening, share report ID {reportID} with support.",
				DisconnectionCodeEnum.ConnectionFailure
			);
		}
		finally
		{
			_processingIncomingChunk = false;
		}

		TryCompletePlaceReplication();
	}

	private int ProcessIncomingChunk(byte[] rawBytes, bool isPlaceReplicate)
	{
		NetReplicateData[] netObjsData = SerializeUtils.Deserialize<NetReplicateData[]>(
			ZstdCompressionUtils.Decompress(rawBytes, MaxDecompressedChunkBytes)
		)!;
		NetReplicateData[][] chunked = ArrayUtils.Chunk(netObjsData, PlaceReplicationChunkSize);

		foreach (NetReplicateData data in netObjsData)
		{
			// If already exists, continue
			if (Root.NetworkObjects.ContainsKey(data.NetworkID))
				continue;
			if (isPlaceReplicate)
			{
				_worldReplicateSet.Add(data);
			}
		}

		foreach (NetReplicateData[] chunk in chunked)
		{
			foreach (NetReplicateData data in chunk)
			{
				if (_useNetworkLog)
				{
					BV.Print($"[Net] [{NetService.LocalPeerID}] on the way {data.NodePath}");
				}
				if (isPlaceReplicate && Root.NetworkObjects.ContainsKey(data.NetworkID))
				{
					// Existing local objects are already materialized, but still count
					// toward the server snapshot total or the loading screen never ends.
					CountInstanceLoaded(null);
					continue;
				}

				if (data.ParentNodeID == null)
				{
					if (_useNetworkLog)
					{
						BV.Print($"[Net] [{NetService.LocalPeerID}] {data.NodePath} no parent");
					}
					if (isPlaceReplicate)
						_worldReplicateSet.Remove(data);
					CountInstanceLoaded(null);
					continue;
				}

				string parentID = data.ParentNodeID;
				string parentPath = data.ParentNodePath;
				NetworkedObject? parentNode = NetService.Root.GetNetObjectFromID(parentID);

				// Fallback to path;
				parentNode ??= NetService.Root.GetNetObj(parentPath);

				if (!NetService.IsPlaceReplicationDone)
				{
					// Check for deleted parents
					bool removed = false;
					foreach (string removedParentPath in _removedRef.ToArray())
					{
						if (removedParentPath != "" && parentPath.StartsWith(removedParentPath))
						{
							if (_useNetworkLog)
							{
								BV.Print(
									$"[Net] [{NetService.LocalPeerID}] {data.NodePath} Removed ref {removedParentPath}"
								);
							}
							_removedRef.Remove(removedParentPath);
							removed = true;
							break;
						}
					}

					if (removed)
					{
						if (_useNetworkLog)
						{
							BV.Print($"[Net] [{NetService.LocalPeerID}] {data.NodePath} removed");
						}
						if (isPlaceReplicate)
							_worldReplicateSet.Remove(data);
						CountInstanceLoaded(null);
						continue;
					}
				}

				if (parentNode != null)
				{
					if (_useNetworkLog)
					{
						BV.Print(
							$"[Net] [{NetService.LocalPeerID}] Chunk Replicate {data.NodePath}"
						);
					}
					parentNode?.RecvReplicate(data);
				}
				else
				{
					if (
						!_pendingReplicates.TryGetValue(parentID, out List<NetReplicateData>? value)
					)
					{
						value = [];
						_pendingReplicates[parentID] = value;
					}

					if (_useNetworkLog)
					{
						BV.Print($"[Net] [{NetService.LocalPeerID}] Pending {data.NodePath}");
					}
					value.Add(data);
				}
			}
		}
		return netObjsData.Length;
	}

	public void SendChunk(
		NetworkedObject[] netObjs,
		Player plr,
		bool isPlaceReplicate = false,
		Action? completed = null,
		bool initializeObjects = false
	)
	{
		if (isPlaceReplicate)
		{
			// Keep this public entry point safe for older callers. The live DataModel is
			// deliberately polled instead of trusting the caller's stale array.
			PlaceReplicationSendSession session = new(plr, completed);
			_placeReplicationSendSessions[plr.PeerID] = session;
			PollPlaceReplication(session);
			return;
		}

		NetReplicateData[] packed = PackNetObjs(netObjs, isPlaceReplicate || initializeObjects);
		List<NetReplicateData[]> chunks = BuildChunks(packed);
		List<byte[]> compressedChunks = [];
		long totalBytes = 0;
		foreach (NetReplicateData[] chunk in chunks)
		{
			if (isPlaceReplicate)
				AppendCompressedChunks(chunk, compressedChunks);
			else
				compressedChunks.Add(
					ZstdCompressionUtils.Compress(SerializeUtils.Serialize(chunk))
				);
		}
		foreach (byte[] chunk in compressedChunks)
			totalBytes += chunk.Length;

		if (!isPlaceReplicate)
		{
			for (int i = 0; i < compressedChunks.Count; i++)
				RpcId(
					plr.PeerID,
					nameof(NetRecvChunk),
					compressedChunks[i],
					false,
					-1,
					i,
					compressedChunks.Count,
					packed.Length,
					false
				);
			return;
		}
	}

	private void PollPlaceReplication(PlaceReplicationSendSession session)
	{
		if (session.Chunks.Length != 0 || Time.GetTicksMsec() < session.NextPollAtMsec)
			return;

		NetworkedObject[] unsent = GetDesiredObjects(session.Player, session.SentNetworkIDs)
			.Where(obj => !session.SentNetworkIDs.Contains(obj.NetworkedObjectID))
			.ToArray();
		NetReplicateData[] packed = PackNetObjs(unsent, true);

		if (packed.Length == 0 && ++session.StablePollCount < StablePlaceReplicationPolls)
		{
			session.NextPollAtMsec = Time.GetTicksMsec() + PlaceReplicationPollIntervalMs;
			return;
		}

		session.IsFinalRound = packed.Length == 0;
		session.StablePollCount = session.IsFinalRound ? session.StablePollCount : 0;
		foreach (NetReplicateData data in packed)
			session.SentNetworkIDs.Add(data.NetworkID);

		List<byte[]> compressedChunks = [];
		foreach (NetReplicateData[] chunk in BuildChunks(packed))
			AppendCompressedChunks(chunk, compressedChunks);

		session.Round++;
		session.Chunks = [.. compressedChunks];
		session.ObjectCount = packed.Length;
		session.NextChunkIndex = 0;
		session.AcknowledgedChunkCount = 0;
		long totalBytes = compressedChunks.Sum(chunk => (long)chunk.Length);
		BV.Print(
			$"[JoinReplication] Peer {session.PeerID} round {session.Round}: objects={packed.Length}, chunks={compressedChunks.Count}, compressedBytes={totalBytes}, final={session.IsFinalRound}."
		);
		SendNextPlaceReplicationChunks(session);
	}

	private static void AppendCompressedChunks(NetReplicateData[] objects, List<byte[]> output)
	{
		byte[] compressed = ZstdCompressionUtils.Compress(SerializeUtils.Serialize(objects));
		if (compressed.Length <= MaxCompressedPlaceChunkBytes || objects.Length <= 1)
		{
			output.Add(compressed);
			return;
		}

		int middle = objects.Length / 2;
		AppendCompressedChunks(objects[..middle], output);
		AppendCompressedChunks(objects[middle..], output);
	}

	private void SendNextPlaceReplicationChunks(PlaceReplicationSendSession session)
	{
		int sent = 0;
		while (
			session.NextChunkIndex < session.Chunks.Length
			&& session.NextChunkIndex - session.AcknowledgedChunkCount < PlaceChunksInFlight
			&& sent++ < PlaceChunksPerFrame
		)
		{
			int chunkIndex = session.NextChunkIndex++;
			RpcId(
				session.PeerID,
				nameof(NetRecvChunk),
				session.Chunks[chunkIndex],
				true,
				session.Round,
				chunkIndex,
				session.Chunks.Length,
				session.ObjectCount,
				session.IsFinalRound
			);
		}
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetAckPlaceReplicationChunk(int round, int chunkIndex)
	{
		if (!NetService.IsServer)
			return;

		int peerID = RemoteSenderId;
		if (
			!_placeReplicationSendSessions.TryGetValue(
				peerID,
				out PlaceReplicationSendSession? session
			)
		)
			return;

		if (round != session.Round || chunkIndex != session.AcknowledgedChunkCount)
		{
			BV.PrintErr(
				$"[JoinReplication] Out-of-order chunk acknowledgement from peer {peerID}: received={round}:{chunkIndex}, expected={session.Round}:{session.AcknowledgedChunkCount}."
			);
			return;
		}

		session.AcknowledgedChunkCount++;
		if (session.AcknowledgedChunkCount == session.Chunks.Length)
		{
			if (session.IsFinalRound)
			{
				_placeReplicationSendSessions.Remove(peerID);
				_streamingPeers[peerID] = new(session.Player, session.SentNetworkIDs);
				BV.Print(
					$"[JoinReplication] Peer {peerID} completed dynamic world replication after {session.Round + 1} rounds and {session.SentNetworkIDs.Count} objects."
				);
				session.Completed?.Invoke();
				return;
			}

			session.Chunks = [];
			session.NextPollAtMsec = Time.GetTicksMsec() + PlaceReplicationPollIntervalMs;
			return;
		}
		SendNextPlaceReplicationChunks(session);
	}

	internal void CancelPlaceReplicationSend(int peerID)
	{
		_placeReplicationSendSessions.Remove(peerID);
		_streamingPeers.Remove(peerID);
	}

	private static List<NetReplicateData[]> BuildChunks(NetReplicateData[] objects)
	{
		List<NetReplicateData[]> chunks = [];
		List<NetReplicateData> current = [];
		int estimatedBytes = 0;

		foreach (NetReplicateData item in objects)
		{
			int itemBytes = SerializeUtils.Serialize(item).Length;
			if (
				current.Count > 0
				&& (
					current.Count >= PlaceReplicationChunkSize
					|| estimatedBytes + itemBytes > MaxPlaceReplicationChunkBytes
				)
			)
			{
				chunks.Add([.. current]);
				current.Clear();
				estimatedBytes = 0;
			}

			current.Add(item);
			estimatedBytes += itemBytes;
		}

		if (current.Count > 0 || chunks.Count == 0)
			chunks.Add([.. current]);

		return chunks;
	}

	// Fallsafe for pending replicates
	public override void Process(double delta)
	{
		base.Process(delta);
		if (NetService.IsDisconnected)
			return;
		if (NetService.IsServer)
		{
			foreach (
				PlaceReplicationSendSession session in _placeReplicationSendSessions.Values.ToArray()
			)
			{
				try
				{
					PollPlaceReplication(session);
					SendNextPlaceReplicationChunks(session);
				}
				catch (Exception ex)
				{
					_placeReplicationSendSessions.Remove(session.PeerID);
					string reportID = Guid.NewGuid().ToString("N")[..8];
					BV.PrintErr(
						$"[JoinFailure:{reportID}] Failed to poll world objects for peer {session.PeerID}: {ex}"
					);
					NetService.DisconnectPeer(
						session.PeerID,
						$"Could not prepare world data. Please retry. If this keeps happening, share report ID {reportID} with support.",
						DisconnectionCodeEnum.ConnectionFailure
					);
				}
			}

			_streamingPollElapsed += delta;
			if (_streamingPollElapsed >= StreamingPollIntervalSeconds)
			{
				_streamingPollElapsed = 0;
				foreach (StreamingPeerState peer in _streamingPeers.Values.ToArray())
				{
					try
					{
						UpdatePeerStreaming(peer);
					}
					catch (Exception ex)
					{
						string reportID = Guid.NewGuid().ToString("N")[..8];
						BV.PrintErr(
							$"[StreamingFailure:{reportID}] Peer {peer.Player.PeerID}: {ex}"
						);
						NetService.DisconnectPeer(
							peer.Player.PeerID,
							$"World streaming failed. Please retry. If this keeps happening, share report ID {reportID} with support.",
							DisconnectionCodeEnum.ConnectionFailure
						);
					}
				}
			}
		}
		ProcessNextIncomingChunk();
		CheckPlaceReplicationTimeout();

		// Debugging magic key
		if (Input.IsActionJustPressed("magic"))
		{
			foreach (var item in _worldReplicateSet)
			{
				BV.Print(item.NodePath, " netID: ", item.NetworkID);
			}
		}

		// Check for pending replications
		if (_pendingReplicates.Count == 0)
		{
			return;
		}

		foreach (string key in _pendingReplicates.Keys.ToArray())
		{
			NetworkedObject? node = NetService.Root.GetNetObjectFromID(key);
			if (
				node == null
				&& _pendingReplicates.TryGetValue(key, out List<NetReplicateData>? pending)
			)
			{
				// A parent may become available under its stable path without being
				// registered under the server's ID (notably for pre-created objects).
				// Retry the same path fallback used during initial chunk application.
				string parentPath = pending.Count > 0 ? pending[0].ParentNodePath : "";
				if (!string.IsNullOrEmpty(parentPath))
					node = NetService.Root.GetNetObj(parentPath);
			}
			if (node != null)
			{
				ResolvePendingReplications(key, node);
			}
		}
	}

	private void UpdatePeerStreaming(StreamingPeerState state)
	{
		if (state.Player.IsDeleted || state.Player.PeerID <= 1)
		{
			_streamingPeers.Remove(state.Player.PeerID);
			return;
		}

		NetworkedObject[] desired = GetDesiredObjects(state.Player, state.ResidentIDs);
		HashSet<string> desiredIDs = desired.Select(obj => obj.NetworkedObjectID).ToHashSet();

		NetworkedObject[] entering = desired
			.Where(obj => !state.ResidentIDs.Contains(obj.NetworkedObjectID))
			.ToArray();
		if (entering.Length > 0)
		{
			SendChunk(entering, state.Player, initializeObjects: true);
			foreach (Dynamic dynamic in entering.OfType<Dynamic>())
				NetService.TransformSync.SendUpdateTransform(dynamic, true, state.Player.PeerID);
			foreach (NetworkedObject obj in entering)
				state.ResidentIDs.Add(obj.NetworkedObjectID);
		}

		HashSet<string> leavingIDs = state
			.ResidentIDs.Where(id => !desiredIDs.Contains(id))
			.ToHashSet();
		foreach (string id in leavingIDs)
		{
			NetworkedObject? obj = NetService.Root.GetNetObjectFromID(id);
			if (
				obj == null
				|| obj.NetworkParent == null
				|| !leavingIDs.Contains(obj.NetworkParent.NetworkedObjectID)
			)
				RpcId(state.Player.PeerID, nameof(NetRecvReplicateRemove), id);
			state.ResidentIDs.Remove(id);
		}
	}

	internal bool IsReplicatedToPeer(int peerID, NetworkedObject obj)
	{
		if (_streamingPeers.TryGetValue(peerID, out StreamingPeerState? state))
			return state.ResidentIDs.Contains(obj.NetworkedObjectID);
		if (
			_placeReplicationSendSessions.TryGetValue(
				peerID,
				out PlaceReplicationSendSession? joining
			)
		)
			return joining.SentNetworkIDs.Contains(obj.NetworkedObjectID);
		return true;
	}

	private NetworkedObject[] GetDesiredObjects(Player player, HashSet<string> residentIDs)
	{
		NetworkedObject[] all = NetService.Root.GetReplicateDescendants();
		HashSet<string> desiredIDs = all.Where(obj =>
				NetService.Root.Streaming.ShouldReplicateToPlayer(
					player,
					obj,
					residentIDs.Contains(obj.NetworkedObjectID)
				)
			)
			.Select(obj => obj.NetworkedObjectID)
			.ToHashSet();

		// A streamed object can never arrive before an absent parent. Pull the
		// complete replicated parent chain into the same residency decision.
		foreach (
			NetworkedObject obj in all.Where(obj => desiredIDs.Contains(obj.NetworkedObjectID))
		)
		{
			for (
				NetworkedObject? parent = obj.NetworkParent;
				parent != null;
				parent = parent.NetworkParent
			)
				desiredIDs.Add(parent.NetworkedObjectID);
		}
		return all.Where(obj => desiredIDs.Contains(obj.NetworkedObjectID)).ToArray();
	}

	private void CheckPlaceReplicationTimeout()
	{
		if (NetService.IsServer || !NetService.ClientConnected || NetService.IsPlaceReplicationDone)
			return;

		// Start the watchdog before the first chunk: malformed/missing RPCs used
		// to leave this timestamp unset, allowing the loading screen to wait forever.
		if (_placeReplicationStartedAtMsec == 0)
		{
			_placeReplicationStartedAtMsec = Time.GetTicksMsec();
			_lastReplicationProgressAtMsec = _placeReplicationStartedAtMsec;
		}
		ulong now = Time.GetTicksMsec();
		bool stalled = now - _lastReplicationProgressAtMsec >= PlaceReplicationStallTimeoutMs;
		bool exceededMaximum =
			now - _placeReplicationStartedAtMsec >= PlaceReplicationMaximumDurationMs;
		if (!stalled && !exceededMaximum)
			return;

		string reportID = Guid.NewGuid().ToString("N")[..8];
		string unresolved = string.Join(
			" | ",
			_worldReplicateSet
				.Take(8)
				.Select(data =>
					$"{data.ClassName} {data.NodePath} (id={data.NetworkID}, parent={data.ParentNodeID})"
				)
		);
		string pendingParents = string.Join(
			" | ",
			_pendingReplicates
				.Take(8)
				.Select(pair =>
					$"{pair.Key} -> {(pair.Value.Count > 0 ? pair.Value[0].ParentNodePath : "<empty>")}"
				)
		);
		BV.PrintErr(
			$"[JoinFailure:{reportID}] World replication {(stalled ? "stalled" : "exceeded maximum duration")}: "
				+ $"elapsed={(now - _placeReplicationStartedAtMsec) / 1000}s, idle={(now - _lastReplicationProgressAtMsec) / 1000}s, "
				+ $"chunks={_placeChunksReceived}/{_placeChunksExpected}, loaded={InstanceLoadedCount - 1}/{InstanceToBeLoadedCount}, "
				+ $"unresolvedObjects={_worldReplicateSet.Count}, pendingParents={_pendingReplicates.Count}, queuedChunks={_incomingChunks.Count}. "
				+ $"First unresolved: [{unresolved}]. First pending parents: [{pendingParents}]."
		);
		NetService.DisconnectSelf(
			$"World loading timed out while applying server data. Please retry. If this keeps happening, share report ID {reportID} with support.",
			DisconnectionCodeEnum.ConnectionTimeout
		);
	}

	public void BroadcastChunk(NetworkedObject[] netObjs)
	{
		if (NetService.NetworkMode != NetworkModeEnum.Client)
			return;
		if (NetService.NetInstance == null)
			return;
		NetService.TransformSync.BroadcastChunk(netObjs);
		foreach (int peerID in NetService.NetInstance.PeerIds)
		{
			NetworkedObject[] visible = netObjs
				.Where(obj => IsReplicatedToPeer(peerID, obj))
				.ToArray();
			if (visible.Length == 0)
				continue;
			byte[] rawData = ZstdCompressionUtils.Compress(
				SerializeUtils.Serialize(PackNetObjs(visible, false))
			);
			RpcId(peerID, nameof(NetRecvChunk), rawData, false, -1, 0, 1, visible.Length, false);
		}
	}

	public static void MarkChunkOverride(NetworkedObject[] netObjs)
	{
		foreach (NetworkedObject item in netObjs)
		{
			item.ChunkBroadcastOverride = true;
		}
	}

	private static NetReplicateData[] PackNetObjs(NetworkedObject[] netObjs, bool isSyncOnce)
	{
		List<NetReplicateData> netObjsData = [];

		foreach (NetworkedObject netObj in netObjs)
		{
			// If no sync, continue
			if (netObj.GetType().IsDefined(typeof(NoSyncAttribute), false))
				continue;

			if (!netObj.ShouldReplicate)
				continue;

			NetReplicateData data = netObj.GetNetReplicateData();
			data.IsSyncOnce = isSyncOnce;
			netObjsData.Add(data);
			if (_useNetworkLog)
			{
				BV.Print($"[Net] Packing {netObj.Name}");
			}
		}

		return [.. netObjsData];
	}

	private void ResolvePendingReplications(string pendingParentID, NetworkedObject node)
	{
		if (_pendingReplicates.TryGetValue(pendingParentID, out List<NetReplicateData>? list))
		{
			_pendingReplicates.Remove(pendingParentID);
			ApplyPendingReplicates(node, list);
		}
	}

	private void ApplyPendingReplicates(NetworkedObject parent, List<NetReplicateData> pending)
	{
		foreach (NetReplicateData data in pending)
		{
			try
			{
				parent.RecvReplicate(data);
			}
			catch (Exception ex)
			{
				string reportID = Guid.NewGuid().ToString("N")[..8];
				BV.PrintErr(
					$"[JoinFailure:{reportID}] Failed to apply pending object '{data.NodePath}' below '{parent.NetworkPath}': {ex}"
				);
				NetService.DisconnectSelf(
					$"Could not load a world object. Please retry. If this keeps happening, share report ID {reportID} with support.",
					DisconnectionCodeEnum.ConnectionFailure
				);
				return;
			}
		}
	}

	internal void CountInstanceLoaded(NetworkedObject? obj)
	{
		if (obj != null)
		{
			// If object is not from world replicate, return
			if (!_worldReplicateSet.Remove(new() { NetworkID = obj.NetworkedObjectID }))
				return;
		}
		InstanceLoadedCount += 1;
		_lastReplicationProgressAtMsec = Time.GetTicksMsec();
		if (InstanceToBeLoadedCount != 0 && !NetService.IsPlaceReplicationDone)
		{
			InstanceLoadedProgress?.Invoke(InstanceLoadedCount, InstanceToBeLoadedCount);

			if (_worldReplicateSet.Count == 0)
			{
				TryCompletePlaceReplication();
			}
		}
	}

	private void TryCompletePlaceReplication()
	{
		if (
			_placeChunksExpected > 0
			&& _placeChunksReceived == _placeChunksExpected
			&& _placeFinalRoundReceived
			&& _incomingChunks.Count == 0
			&& !_processingIncomingChunk
			&& _worldReplicateSet.Count == 0
		)
		{
			NetService.NetWorldSyncd();
		}
	}

	public async void SendNetReplicate(NetworkedObject netObj, bool isSyncOnce = false)
	{
		// if not client, return
		if (NetService.NetworkMode != NetworkModeEnum.Client)
			return;

		// don't sync if not ready
		if (!netObj.Root.IsLoaded)
			return;

		// If should not replicate, return
		if (!netObj.ShouldReplicate)
			return;

		if (_useNetworkLog)
		{
			BV.Print($"[Net] Send Replicate {netObj.Name}");
		}

		if (netObj is Dynamic dyn)
		{
			NetService.TransformSync.SendUpdateTransform(dyn, true);
		}

		NetReplicateData netdata = netObj.GetNetReplicateData();
		netdata.Name = netObj.Name;
		netdata.IsSyncOnce = isSyncOnce;
		byte[] data = SerializeUtils.Serialize(netdata);

		if (NetService.IsServer && NetService.NetInstance != null)
		{
			foreach (int peerID in NetService.NetInstance.PeerIds)
			{
				if (!_streamingPeers.TryGetValue(peerID, out StreamingPeerState? state))
					continue;
				if (!NetService.Root.Streaming.ShouldReplicateToPlayer(state.Player, netObj, false))
					continue;
				// Never send a child before its parent is resident. The next streaming
				// poll will pull the complete dependency chain in as one initialized batch.
				if (
					netObj.NetworkParent is { } parent
					&& parent != NetService.Root
					&& !state.ResidentIDs.Contains(parent.NetworkedObjectID)
				)
					continue;
				RpcId(peerID, nameof(NetRecvReplicate), data);
				state.ResidentIDs.Add(netObj.NetworkedObjectID);
			}
		}
		else
		{
			Rpc(nameof(NetRecvReplicate), data);
		}
	}

	public void SendNetReplicateRemove(NetworkedObject netobj, long peerID = -1)
	{
		foreach (PlaceReplicationSendSession joining in _placeReplicationSendSessions.Values)
		{
			if (!joining.SentNetworkIDs.Remove(netobj.NetworkedObjectID))
				continue;
			RpcId(joining.PeerID, nameof(NetRecvReplicateRemove), netobj.NetworkedObjectID);
		}

		if (peerID != -1)
		{
			RpcId((int)peerID, nameof(NetRecvReplicateRemove), netobj.NetworkedObjectID);
		}
		else
		{
			if (NetService.IsServer && NetService.NetInstance != null)
			{
				foreach (int targetPeer in NetService.NetInstance.PeerIds)
				{
					if (
						!_streamingPeers.TryGetValue(targetPeer, out StreamingPeerState? state)
						|| !state.ResidentIDs.Remove(netobj.NetworkedObjectID)
					)
						continue;
					RpcId(targetPeer, nameof(NetRecvReplicateRemove), netobj.NetworkedObjectID);
				}
			}
			else
				Rpc(nameof(NetRecvReplicateRemove), netobj.NetworkedObjectID);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private async void NetRecvReplicate(byte[] data)
	{
		// Ignore if from server itself
		if (NetService.IsServer)
			return;
		if (data is not { Length: > 0 } || data.Length > MaxSingleReplicateBytes)
		{
			BV.PrintErr("Rejected invalid single-object replication payload.");
			return;
		}

		await Globals.Singleton.WaitFrame();

		NetReplicateData replicateData;
		try
		{
			replicateData = SerializeUtils.Deserialize<NetReplicateData>(data);
		}
		catch (Exception ex)
		{
			BV.PrintErr("Rejected malformed single-object replication payload: ", ex);
			return;
		}
		string parentID = replicateData.ParentNodeID;
		string parentNodePath = replicateData.ParentNodePath;

		if (_useNetworkLog)
		{
			BV.Print($"[Net] Recv Replicate {replicateData.NodePath}");
		}

		NetworkedObject? parent = NetService.Root.GetObjectFromID(parentID);

		// Fallback to path
		parent ??= NetService.Root.GetNetObj(parentNodePath);

		if (parent != null)
		{
			NetworkedObject? existingChild = NetService.Root.GetNetObjectFromID(
				replicateData.NetworkID
			);

			if (existingChild != null)
			{
				NetworkedObject? currentParent = existingChild.NetworkParent;

				if (currentParent != parent)
				{
					if (parent != existingChild)
					{
						if (_useNetworkLog)
						{
							BV.Print($"[Net] [@] Reparent {replicateData.NodePath}");
						}

						existingChild.NetworkParent = parent;
					}
				}
			}
			else
			{
				if (_useNetworkLog)
				{
					BV.Print($"[Net] [+] Add {replicateData.NodePath}");
				}
				parent.RecvReplicate(replicateData);
			}
		}
		else
		{
			// Parent not ready yet → queue
			if (!_pendingReplicates.TryGetValue(parentID, out List<NetReplicateData>? value))
			{
				if (_pendingReplicates.Count >= MaxPendingReplicateParents)
				{
					BV.PrintErr(
						"Pending replication parent limit reached; dropping ",
						replicateData.NodePath
					);
					return;
				}
				value = [];
				_pendingReplicates[parentID] = value;
			}
			if (value.Count >= MaxPendingReplicatesPerParent)
			{
				BV.PrintErr("Pending replication limit reached for parent ", parentID);
				return;
			}
			if (_useNetworkLog)
			{
				BV.Print($"[Net] [?] Pending {replicateData.NodePath}");
			}
			value.Add(replicateData);
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvReplicateRemove(string nodePath)
	{
		if (NetService.IsServer)
			return;
		NetService.PropSync.DiscardPendingProps(nodePath);
		NetService.TransformSync.DiscardPendingTransform(nodePath);
		NetworkedObject? targetObj = NetService.Root.GetNetObjectFromID(nodePath);

		// Add removed reference if replicating
		if (!NetService.IsPlaceReplicationDone)
		{
			_removedRef.Add(nodePath);
		}

		// If target still exists, remove
		if (targetObj != null)
		{
			if (_useNetworkLog)
			{
				BV.Print($"[Net] [-] Remove {targetObj.NetworkPath}");
			}
			targetObj.ForceDelete();
		}
		else
		{
			if (_useNetworkLog)
			{
				BV.Print($"[Net] [-] [?] Net Obj doesn't exist {nodePath}");
			}
		}
	}

	// Flush pending replicates
	public void FlushPendingReplicates(NetworkedObject netObj)
	{
		string netID = netObj.NetworkedObjectID;
		if (_pendingReplicates.TryGetValue(netID, out List<NetReplicateData>? queued))
		{
			_pendingReplicates.Remove(netID);
			if (_useNetworkLog)
			{
				BV.Print($"[Net] [{NetService.LocalPeerID}] Resolve Pending {netID}");
			}
			ApplyPendingReplicates(netObj, queued);
		}
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Services;
using BrickVerse.Shared;
using BrickVerse.Utils;
using BrickVerse.Utils.Compression;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static BrickVerse.Datamodel.Services.NetworkService;

namespace BrickVerse.Networking.Synchronizers;

[Internal]
public sealed partial class NetworkReplicateSync : Instance
{
	private const int PlaceReplicationChunkSize = 300;
	private const int MaxPlaceReplicationChunkBytes = 256 * 1024;
	private const int MaxInFlightPlaceChunks = 4;
	internal NetworkService NetService = null!;

	// Queue for pending replicate data (waiting for node to spawn)
	private readonly Dictionary<string, List<NetReplicateData>> _pendingReplicates = [];

	public int InstanceLoadedCount = 1;
	public int InstanceToBeLoadedCount = 0;
	public event Action<int, int>? InstanceLoadedProgress;
	private readonly HashSet<string> _removedRef = [];
	private readonly HashSet<NetReplicateData> _worldReplicateSet = [];
	private readonly Queue<(byte[] Bytes, bool IsPlaceReplication, int Index, int Count, int ObjectCount)> _incomingChunks = new();
	private readonly Dictionary<int, PlaceReplicationSendSession> _placeReplicationSendSessions = [];
	private bool _processingIncomingChunk;
	private bool _hasProcessedIncomingChunk;
	private int _placeChunksReceived;
	private int _placeChunksExpected;

	private sealed class PlaceReplicationSendSession(int peerID, byte[][] chunks, int objectCount)
	{
		public int PeerID { get; } = peerID;
		public byte[][] Chunks { get; } = chunks;
		public int ObjectCount { get; } = objectCount;
		public int NextChunkIndex { get; set; }
		public int AcknowledgedChunkCount { get; set; }
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

	public void SyncPlaceToPlayer(Player plr)
	{
		World game = NetService.Root;
		NetworkedObject[] netObjs = game.GetReplicateDescendants();

		game.NetSendAllPropUpdate(plr.PeerID);

		SendChunk(netObjs, plr, true);
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvChunk(byte[] rawBytes, bool isPlaceReplicate, int chunkIndex, int chunkCount, int objectCount)
	{
		if (isPlaceReplicate)
		{
			_placeChunksExpected = chunkCount;
			InstanceToBeLoadedCount = objectCount;
		}
		_incomingChunks.Enqueue((rawBytes, isPlaceReplicate, chunkIndex, chunkCount, objectCount));
	}

	private async void ProcessNextIncomingChunk()
	{
		if (_processingIncomingChunk || _incomingChunks.Count == 0)
			return;

		_processingIncomingChunk = true;
		try
		{
			// Defer the first batch for scene cleanup; later chunks are already processed
			// after the previous batch yielded back to the scene tree.
			if (!_hasProcessedIncomingChunk)
			{
				await Globals.Singleton.WaitFrame();
				await Globals.Singleton.WaitFrame();
			}
			var incoming = _incomingChunks.Dequeue();
			await ProcessIncomingChunk(incoming.Bytes, incoming.IsPlaceReplication);
			_hasProcessedIncomingChunk = true;
			if (incoming.IsPlaceReplication)
			{
				_placeChunksReceived++;
				RpcId(1, nameof(NetAckPlaceReplicationChunk), incoming.Index);
			}
		}
		finally
		{
			_processingIncomingChunk = false;
		}

		TryCompletePlaceReplication();
	}

	private async Task ProcessIncomingChunk(byte[] rawBytes, bool isPlaceReplicate)
	{

		NetReplicateData[] netObjsData = SerializeUtils.Deserialize<NetReplicateData[]>(ZstdCompressionUtils.Decompress(rawBytes))!;
		NetReplicateData[][] chunked = ArrayUtils.Chunk(netObjsData, PlaceReplicationChunkSize);

		foreach (NetReplicateData data in netObjsData)
		{
			// If already exists, continue
			if (Root.NetworkObjects.ContainsKey(data.NetworkID)) continue;
			if (isPlaceReplicate)
			{
				_worldReplicateSet.Add(data);
			}
		}

		foreach (NetReplicateData[] chunk in chunked)
		{
			foreach (NetReplicateData data in chunk)
			{
				if (_useNetworkLog) { BV.Print($"[Net] [{NetService.LocalPeerID}] on the way {data.NodePath}"); }

				if (data.ParentNodeID == null)
				{
					if (_useNetworkLog) { BV.Print($"[Net] [{NetService.LocalPeerID}] {data.NodePath} no parent"); }
					if (isPlaceReplicate) _worldReplicateSet.Remove(data);
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
							if (_useNetworkLog) { BV.Print($"[Net] [{NetService.LocalPeerID}] {data.NodePath} Removed ref {removedParentPath}"); }
							_removedRef.Remove(removedParentPath);
							removed = true;
							break;
						}
					}

					if (removed)
					{
						if (_useNetworkLog) { BV.Print($"[Net] [{NetService.LocalPeerID}] {data.NodePath} removed"); }
						if (isPlaceReplicate) _worldReplicateSet.Remove(data);
						CountInstanceLoaded(null);
						continue;
					}
				}

				if (parentNode != null)
				{
					if (_useNetworkLog) { BV.Print($"[Net] [{NetService.LocalPeerID}] Chunk Replicate {data.NodePath}"); }
					parentNode?.RecvReplicate(data);
				}
				else
				{
					if (!_pendingReplicates.TryGetValue(parentID, out List<NetReplicateData>? value))
					{
						value = [];
						_pendingReplicates[parentID] = value;
					}

					if (_useNetworkLog) { BV.Print($"[Net] [{NetService.LocalPeerID}] Pending {data.NodePath}"); }
					value.Add(data);
				}
			}
			// hope and prayers
			await Globals.Singleton.WaitFrame();
		}
	}

	public void SendChunk(NetworkedObject[] netObjs, Player plr, bool isPlaceReplicate = false)
	{
		NetReplicateData[] packed = PackNetObjs(netObjs);
		List<NetReplicateData[]> chunks = BuildChunks(packed);
		byte[][] compressedChunks = new byte[chunks.Count][];
		long totalBytes = 0;
		for (int i = 0; i < chunks.Count; i++)
		{
			compressedChunks[i] = ZstdCompressionUtils.Compress(SerializeUtils.Serialize(chunks[i]));
			totalBytes += compressedChunks[i].Length;
		}

		if (!isPlaceReplicate)
		{
			for (int i = 0; i < compressedChunks.Length; i++)
				RpcId(plr.PeerID, nameof(NetRecvChunk), compressedChunks[i], false, i, compressedChunks.Length, packed.Length);
			return;
		}

		PlaceReplicationSendSession session = new(plr.PeerID, compressedChunks, packed.Length);
		_placeReplicationSendSessions[plr.PeerID] = session;
		BV.Print($"[JoinReplication] Snapshot queued for peer {plr.PeerID}: objects={packed.Length}, chunks={chunks.Count}, compressedBytes={totalBytes}, window={MaxInFlightPlaceChunks}.");
		SendNextPlaceReplicationChunks(session);
	}

	private void SendNextPlaceReplicationChunks(PlaceReplicationSendSession session)
	{
		while (session.NextChunkIndex < session.Chunks.Length
			&& session.NextChunkIndex - session.AcknowledgedChunkCount < MaxInFlightPlaceChunks)
		{
			int chunkIndex = session.NextChunkIndex++;
			RpcId(session.PeerID, nameof(NetRecvChunk), session.Chunks[chunkIndex], true, chunkIndex, session.Chunks.Length, session.ObjectCount);
		}
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetAckPlaceReplicationChunk(int chunkIndex)
	{
		if (!NetService.IsServer) return;

		int peerID = RemoteSenderId;
		if (!_placeReplicationSendSessions.TryGetValue(peerID, out PlaceReplicationSendSession? session))
			return;

		if (chunkIndex != session.AcknowledgedChunkCount)
		{
			BV.PrintErr($"[JoinReplication] Out-of-order chunk acknowledgement from peer {peerID}: received={chunkIndex}, expected={session.AcknowledgedChunkCount}.");
			return;
		}

		session.AcknowledgedChunkCount++;
		if (session.AcknowledgedChunkCount == session.Chunks.Length)
		{
			_placeReplicationSendSessions.Remove(peerID);
			BV.Print($"[JoinReplication] Peer {peerID} acknowledged all {session.Chunks.Length} world chunks.");
			return;
		}

		SendNextPlaceReplicationChunks(session);
	}

	internal void CancelPlaceReplicationSend(int peerID)
	{
		_placeReplicationSendSessions.Remove(peerID);
	}

	private static List<NetReplicateData[]> BuildChunks(NetReplicateData[] objects)
	{
		List<NetReplicateData[]> chunks = [];
		List<NetReplicateData> current = [];
		int estimatedBytes = 0;

		foreach (NetReplicateData item in objects)
		{
			int itemBytes = SerializeUtils.Serialize(item).Length;
			if (current.Count > 0 && (current.Count >= PlaceReplicationChunkSize || estimatedBytes + itemBytes > MaxPlaceReplicationChunkBytes))
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
		ProcessNextIncomingChunk();

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

		foreach (string? key in _pendingReplicates.Keys)
		{
			NetworkedObject? node = NetService.Root.GetNetObjectFromID(key);
			if (node != null)
			{
				CheckLeftoverReplication(node);
			}
		}
	}

	public void BroadcastChunk(NetworkedObject[] netObjs)
	{
		if (NetService.NetworkMode != NetworkModeEnum.Client) return;
		NetService.TransformSync.BroadcastChunk(netObjs);
		byte[] rawData = ZstdCompressionUtils.Compress(SerializeUtils.Serialize(PackNetObjs(netObjs)));

		//GD.PushWarning("Broadcast chunk ", netObjs.Length);

		Rpc(nameof(NetRecvChunk), rawData, false, 0, 1, netObjs.Length);
	}

	public static void MarkChunkOverride(NetworkedObject[] netObjs)
	{
		foreach (NetworkedObject item in netObjs)
		{
			item.ChunkBroadcastOverride = true;
		}
	}

	private static NetReplicateData[] PackNetObjs(NetworkedObject[] netObjs)
	{
		List<NetReplicateData> netObjsData = [];

		foreach (NetworkedObject netObj in netObjs)
		{
			// If no sync, continue
			if (netObj.GetType().IsDefined(typeof(NoSyncAttribute), false)) continue;

			if (!netObj.ShouldReplicate) continue;

			NetReplicateData data = netObj.GetNetReplicateData();
			data.IsSyncOnce = true;
			netObjsData.Add(data);
			if (_useNetworkLog) { BV.Print($"[Net] Packing {netObj.Name}"); }
		}

		return [.. netObjsData];
	}

	private void CheckLeftoverReplication(NetworkedObject node)
	{
		string netID = node.NetworkedObjectID;

		if (_pendingReplicates.TryGetValue(netID, out List<NetReplicateData>? list))
		{
			foreach (NetReplicateData data in list)
			{
				node.RecvReplicate(data);
			}
			_pendingReplicates.Remove(netID);
		}
	}

	internal void CountInstanceLoaded(NetworkedObject? obj)
	{
		if (obj != null)
		{
			// If object is not from world replicate, return
			if (!_worldReplicateSet.Remove(new() { NetworkID = obj.NetworkedObjectID })) return;
		}
		InstanceLoadedCount += 1;
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
		if (_placeChunksExpected > 0
			&& _placeChunksReceived == _placeChunksExpected
			&& _incomingChunks.Count == 0
			&& !_processingIncomingChunk
			&& _worldReplicateSet.Count == 0)
		{
			NetService.NetWorldSyncd();
		}
	}

	public async void SendNetReplicate(NetworkedObject netObj, bool isSyncOnce = false)
	{
		// if not client, return
		if (NetService.NetworkMode != NetworkModeEnum.Client) return;

		// don't sync if not ready
		if (!netObj.Root.IsLoaded) return;

		// If should not replicate, return
		if (!netObj.ShouldReplicate) return;

		if (_useNetworkLog) { BV.Print($"[Net] Send Replicate {netObj.Name}"); }

		if (netObj is Dynamic dyn)
		{
			NetService.TransformSync.SendUpdateTransform(dyn, true);
		}

		NetReplicateData netdata = netObj.GetNetReplicateData();
		netdata.Name = netObj.Name;
		netdata.IsSyncOnce = isSyncOnce;
		byte[] data = SerializeUtils.Serialize(netdata);

		Rpc(nameof(NetRecvReplicate), data);
	}

	public void SendNetReplicateRemove(NetworkedObject netobj, long peerID = -1)
	{
		if (peerID != -1)
		{
			RpcId((int)peerID, nameof(NetRecvReplicateRemove), netobj.NetworkedObjectID);
		}
		else
		{
			Rpc(nameof(NetRecvReplicateRemove), netobj.NetworkedObjectID);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private async void NetRecvReplicate(byte[] data)
	{
		// Ignore if from server itself
		if (NetService.IsServer) return;

		await Globals.Singleton.WaitFrame();

		NetReplicateData replicateData = SerializeUtils.Deserialize<NetReplicateData>(data)!;
		string parentID = replicateData.ParentNodeID;
		string parentNodePath = replicateData.ParentNodePath;

		if (_useNetworkLog) { BV.Print($"[Net] Recv Replicate {replicateData.NodePath}"); }

		NetworkedObject? parent = NetService.Root.GetObjectFromID(parentID);

		// Fallback to path
		parent ??= NetService.Root.GetNetObj(parentNodePath);

		if (parent != null)
		{
			NetworkedObject? existingChild = NetService.Root.GetNetObjectFromID(replicateData.NetworkID);

			if (existingChild != null)
			{
				NetworkedObject? currentParent = existingChild.NetworkParent;

				if (currentParent != parent)
				{
					if (parent != existingChild)
					{
						if (_useNetworkLog) { BV.Print($"[Net] [@] Reparent {replicateData.NodePath}"); }

						existingChild.NetworkParent = parent;
					}
				}
			}
			else
			{
				if (_useNetworkLog) { BV.Print($"[Net] [+] Add {replicateData.NodePath}"); }
				parent.RecvReplicate(replicateData);
			}
		}
		else
		{
			// Parent not ready yet → queue
			if (!_pendingReplicates.TryGetValue(parentID, out List<NetReplicateData>? value))
			{
				value = [];
				_pendingReplicates[parentID] = value;
			}
			if (_useNetworkLog) { BV.Print($"[Net] [?] Pending {replicateData.NodePath}"); }
			value.Add(replicateData);
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvReplicateRemove(string nodePath)
	{
		if (NetService.IsServer) return;
		NetworkedObject? targetObj = NetService.Root.GetNetObjectFromID(nodePath);

		// Add removed reference if replicating
		if (!NetService.IsPlaceReplicationDone)
		{
			_removedRef.Add(nodePath);
		}

		// If target still exists, remove
		if (targetObj != null)
		{
			if (_useNetworkLog) { BV.Print($"[Net] [-] Remove {targetObj.NetworkPath}"); }
			targetObj.ForceDelete();
		}
		else
		{
			if (_useNetworkLog) { BV.Print($"[Net] [-] [?] Net Obj doesn't exist {nodePath}"); }
		}
	}

	// Flush pending replicates
	public void FlushPendingReplicates(NetworkedObject netObj)
	{
		string netID = netObj.NetworkedObjectID;
		if (_pendingReplicates.TryGetValue(netID, out List<NetReplicateData>? queued))
		{
			_pendingReplicates.Remove(netID);
			if (_useNetworkLog) { BV.Print($"[Net] [{NetService.LocalPeerID}] Resolve Pending {netID}"); }
			foreach (NetReplicateData r in queued)
			{
				if (_useNetworkLog) { BV.Print($"[Net] [{NetService.LocalPeerID}] {netID} Resolve Pending Replicate {r.Name}"); }

				netObj.RecvReplicate(r);
			}
		}
	}
}

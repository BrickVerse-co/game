// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace BrickVerse.Networking;

// Fixed-sized ENet messages prevent two data commands being aggregated into a
// full 1400-byte datagram. Padding compresses away at the ENet/Zlib layer.
internal sealed class NetworkPacketFraming
{
	internal const int PacketSize = 960;
	internal const int HeaderSize = 16;
	internal const int PayloadSize = PacketSize - HeaderSize;
	internal const int MaxMessageSize = 16 * 1024 * 1024;
	private const int MaxBufferedBytes = 32 * 1024 * 1024;
	private const int MaxPendingPerPeer = 32;
	private const int MaxUnreliablePendingPerPeer = 4;
	private const int MaxPeerBufferedBytes = MaxMessageSize;
	private const uint Magic = 0x31505642; // BVP1
	private const long ExpiryMilliseconds = 30_000;
	private readonly Dictionary<(int Peer, uint Message), PendingMessage> _pending = [];
	private int _bufferedBytes;
	private long _lastExpirySweep;

	private sealed class PendingMessage(int length, int count, TransferMode mode, long now)
	{
		public int Length { get; } = length;
		public int Count { get; } = count;
		public Dictionary<int, byte[]> Fragments { get; } = [];
		public int BufferedBytes;
		public TransferMode Mode { get; } = mode;
		public long LastProgress = now;
	}

	internal static bool IsCompatible(byte[] packet) =>
		packet.Length == PacketSize && BinaryPrimitives.ReadUInt32LittleEndian(packet) == Magic;

	internal static IEnumerable<byte[]> Encode(byte[] message, uint messageId)
	{
		if (message.Length == 0 || message.Length > MaxMessageSize)
			throw new InvalidDataException("Invalid network message length.");
		int count = (message.Length + PayloadSize - 1) / PayloadSize;
		for (int index = 0; index < count; index++)
		{
			byte[] packet = new byte[PacketSize];
			BinaryPrimitives.WriteUInt32LittleEndian(packet, Magic);
			BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), messageId);
			BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), message.Length);
			BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12), (ushort)index);
			BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14), (ushort)count);
			int offset = index * PayloadSize;
			message
				.AsSpan(offset, Math.Min(PayloadSize, message.Length - offset))
				.CopyTo(packet.AsSpan(HeaderSize));
			yield return packet;
		}
	}

	internal byte[]? Decode(int peer, byte[] packet, TransferMode mode, long now)
	{
		Expire(now);
		if (!IsCompatible(packet))
			throw new InvalidDataException(
				"Incompatible network framing. Update both client and server."
			);
		uint id = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4));
		int length = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(8));
		int index = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12));
		int count = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(14));
		if (
			length <= 0
			|| length > MaxMessageSize
			|| count != (length + PayloadSize - 1) / PayloadSize
			|| index >= count
		)
			throw new InvalidDataException("Invalid network frame metadata.");

		if (count == 1)
			return packet.AsSpan(HeaderSize, length).ToArray();

		var key = (peer, id);
		if (!_pending.TryGetValue(key, out PendingMessage? pending))
		{
			int peerPending = 0;
			(int Peer, uint Message)? oldest = null;
			long oldestProgress = long.MaxValue;
			foreach (var entry in _pending)
				if (entry.Key.Peer == peer && (entry.Value.Mode == TransferMode.Reliable) == (mode == TransferMode.Reliable))
				{
					peerPending++;
					if (entry.Value.LastProgress < oldestProgress)
					{
						oldest = entry.Key;
						oldestProgress = entry.Value.LastProgress;
					}
				}
			int limit = mode == TransferMode.Reliable ? MaxPendingPerPeer : MaxUnreliablePendingPerPeer;
			if (peerPending >= limit)
			{
				if (mode == TransferMode.Reliable)
					throw new InvalidDataException("Network reassembly message limit exceeded.");
				if (oldest.HasValue)
					Remove(oldest.Value); // Lost unreliable updates must not block reliable traffic.
			}
			pending = new PendingMessage(length, count, mode, now);
			_pending.Add(key, pending);
		}
		if (pending.Length != length || pending.Count != count || pending.Mode != mode)
			throw new InvalidDataException("Inconsistent network frame metadata.");
		if (!pending.Fragments.ContainsKey(index))
		{
			int offset = index * PayloadSize;
			int bytes = Math.Min(PayloadSize, length - offset);
			int peerBytes = 0;
			foreach (var entry in _pending)
				if (entry.Key.Peer == peer)
					peerBytes += entry.Value.BufferedBytes;
			if (mode == TransferMode.Reliable
				&& (bytes > MaxBufferedBytes - _bufferedBytes || bytes > MaxPeerBufferedBytes - peerBytes))
			{
				foreach (var obsolete in new List<(int Peer, uint Message)>(_pending.Keys))
					if (obsolete.Peer == peer && _pending[obsolete].Mode != TransferMode.Reliable)
					{
						peerBytes -= _pending[obsolete].BufferedBytes;
						Remove(obsolete);
					}
			}
			if (bytes > MaxBufferedBytes - _bufferedBytes || bytes > MaxPeerBufferedBytes - peerBytes)
				throw new InvalidDataException("Network reassembly byte limit exceeded.");
			// Allocate only bytes actually received, never an advertised full length.
			pending.Fragments.Add(index, packet.AsSpan(HeaderSize, bytes).ToArray());
			pending.BufferedBytes += bytes;
			_bufferedBytes += bytes;
			pending.LastProgress = now;
		}
		if (pending.Fragments.Count != count)
			return null;
		byte[] message = new byte[length];
		foreach (var fragment in pending.Fragments)
			fragment.Value.CopyTo(message, fragment.Key * PayloadSize);
		Remove(key);
		return message;
	}

	internal void RemovePeer(int peer)
	{
		foreach (var key in new List<(int Peer, uint Message)>(_pending.Keys))
			if (key.Peer == peer)
				Remove(key);
	}

	internal void Expire(long now)
	{
		if (now - _lastExpirySweep < 250)
			return;
		_lastExpirySweep = now;
		foreach (var key in new List<(int Peer, uint Message)>(_pending.Keys))
			if (now - _pending[key].LastProgress >= ExpiryMilliseconds)
				Remove(key);
	}

	private void Remove((int Peer, uint Message) key)
	{
		_bufferedBytes -= _pending[key].BufferedBytes;
		_pending.Remove(key);
	}
}

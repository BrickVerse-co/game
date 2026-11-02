// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using BrickVerse.Networking;

namespace BrickVerse.Tests;

public sealed class NetworkPacketFramingTests
{
	[Theory]
	[InlineData(1)]
	[InlineData(944)]
	[InlineData(945)]
	[InlineData(250000)]
	public void LargeMessagesRoundTripThroughFixedSizedFrames(int length)
	{
		byte[] message = new byte[length];
		new Random(42).NextBytes(message);
		byte[][] packets = NetworkPacketFraming.Encode(message, 7).ToArray();
		Assert.All(packets, packet => Assert.Equal(960, packet.Length));
		NetworkPacketFraming receiver = new();
		byte[]? result = null;
		foreach (byte[] packet in packets.Reverse())
			result = receiver.Decode(2, packet, TransferMode.Reliable, 1);
		Assert.Equal(message, result);
	}

	[Fact]
	public void DuplicatesCannotCompleteAnIncompleteMessage()
	{
		byte[][] packets = NetworkPacketFraming.Encode(new byte[2000], 1).ToArray();
		NetworkPacketFraming receiver = new();
		Assert.Null(receiver.Decode(2, packets[0], TransferMode.Reliable, 1));
		Assert.Null(receiver.Decode(2, packets[0], TransferMode.Reliable, 2));
		Assert.Null(receiver.Decode(2, packets[1], TransferMode.Reliable, 3));
		Assert.NotNull(receiver.Decode(2, packets[2], TransferMode.Reliable, 4));
	}

	[Fact]
	public void PartialMessagesAreIsolatedByPeerAndExpire()
	{
		byte[][] packets = NetworkPacketFraming.Encode(new byte[2000], 1).ToArray();
		NetworkPacketFraming receiver = new();
		Assert.Null(receiver.Decode(2, packets[0], TransferMode.Reliable, 1));
		Assert.Null(receiver.Decode(3, packets[1], TransferMode.Reliable, 2));
		receiver.Expire(30001);
		Assert.Null(receiver.Decode(2, packets[1], TransferMode.Reliable, 30002));
		Assert.Null(receiver.Decode(2, packets[2], TransferMode.Reliable, 30003));
	}

	[Fact]
	public void LostUnreliableUpdatesDoNotBlockReliableMessages()
	{
		NetworkPacketFraming receiver = new();
		for (uint id = 1; id <= 40; id++)
			receiver.Decode(
				2,
				NetworkPacketFraming.Encode(new byte[2000], id).First(),
				TransferMode.UnreliableOrdered,
				id
			);
		byte[]? result = null;
		foreach (byte[] packet in NetworkPacketFraming.Encode(new byte[2000], 41))
			result = receiver.Decode(2, packet, TransferMode.Reliable, 41);
		Assert.NotNull(result);
	}

	[Fact]
	public void AdvertisedLargeLengthsDoNotReserveTheirFullPayload()
	{
		NetworkPacketFraming receiver = new();
		byte[] payload = new byte[NetworkPacketFraming.MaxMessageSize];
		for (uint id = 1; id <= 4; id++)
			Assert.Null(
				receiver.Decode(
					2,
					NetworkPacketFraming.Encode(payload, id).First(),
					TransferMode.Reliable,
					id
				)
			);
		byte[]? result = null;
		foreach (byte[] packet in NetworkPacketFraming.Encode(new byte[2000], 5))
			result = receiver.Decode(3, packet, TransferMode.Reliable, 5);
		Assert.NotNull(result);
	}

	[Fact]
	public void ReliableMessageCanUseFullLimitDespiteLostUnreliableFragments()
	{
		NetworkPacketFraming receiver = new();
		receiver.Decode(
			2,
			NetworkPacketFraming.Encode(new byte[2000], 1).First(),
			TransferMode.UnreliableOrdered,
			1
		);
		byte[]? result = null;
		foreach (
			byte[] packet in NetworkPacketFraming.Encode(
				new byte[NetworkPacketFraming.MaxMessageSize],
				2
			)
		)
			result = receiver.Decode(2, packet, TransferMode.Reliable, 2);
		Assert.Equal(NetworkPacketFraming.MaxMessageSize, result?.Length);
	}

	[Fact]
	public void MalformedFramesAreRejected()
	{
		NetworkPacketFraming receiver = new();
		Assert.Throws<InvalidDataException>(() =>
			receiver.Decode(2, new byte[1], TransferMode.Reliable, 1)
		);
		byte[] packet = NetworkPacketFraming.Encode(new byte[2], 1).Single();
		Array.Fill<byte>(packet, 255, 8, 4);
		Assert.Throws<InvalidDataException>(() =>
			receiver.Decode(2, packet, TransferMode.Reliable, 1)
		);
	}
}

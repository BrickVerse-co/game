using System.Reflection;
using BrickVerse.Attributes;
using BrickVerse.Networking;
using BrickVerse.Networking.Synchronizers;

namespace BrickVerse.Tests;

public sealed class ReplicationAcknowledgementTests
{
	[Theory]
	[InlineData("NetRecvChunk")]
	[InlineData("NetAckPlaceReplicationChunk")]
	public void SnapshotTrafficUsesItsOwnReliableChannel(string methodName)
	{
		MethodInfo? method = typeof(NetworkReplicateSync).GetMethod(methodName,
			BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);
		NetRpcAttribute? rpc = method.GetCustomAttribute<NetRpcAttribute>();
		Assert.NotNull(rpc);
		Assert.Equal(TransferMode.Reliable, rpc.TransferMode);
		Assert.Equal(2, rpc.TransferChannel);
	}

	[Fact]
	public void LaterAcknowledgementRecoversMissingEarlierAcknowledgement()
	{
		// The receiver processes chunks in order. ACK 867 proves 864..866 were
		// applied, even if their individual acknowledgements did not arrive.
		Assert.Equal(868, NetworkReplicateSync.AdvanceChunkAcknowledgement(864, 872, 867));
	}

	[Theory]
	[InlineData(864, 872, 863, 864)]
	[InlineData(864, 872, 864, 865)]
	[InlineData(864, 872, 871, 872)]
	[InlineData(864, 872, 872, 864)]
	[InlineData(864, 872, -1, 864)]
	public void DuplicateOrUnsentChunksCannotIncorrectlyAdvanceWindow(
		int acknowledged, int sent, int index, int expected)
	{
		Assert.Equal(expected, NetworkReplicateSync.AdvanceChunkAcknowledgement(acknowledged, sent, index));
	}
}

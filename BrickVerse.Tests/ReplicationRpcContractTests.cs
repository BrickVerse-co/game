using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BrickVerse.Tests;

public sealed class ReplicationRpcContractTests
{
	[Fact]
	public void ChunkSendersSupplyEveryReceiverArgument()
	{
		// RpcId uses params object[], so the compiler cannot catch a wire-contract mismatch.
		DirectoryInfo? directory = new(System.AppContext.BaseDirectory);
		while (
			directory != null && !File.Exists(Path.Combine(directory.FullName, "BrickVerse.sln"))
		)
			directory = directory.Parent;
		Assert.NotNull(directory);
		string source = File.ReadAllText(
			Path.Combine(
				directory.FullName,
				"BrickVerse/scripts/network/syncs/NetworkReplicateSync.cs"
			)
		);
		var root = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)
			.GetRoot(TestContext.Current.CancellationToken);
		var receiver = root.DescendantNodes()
			.OfType<MethodDeclarationSyntax>()
			.Single(method => method.Identifier.Text == "NetRecvChunk");
		var sends = root.DescendantNodes()
			.OfType<InvocationExpressionSyntax>()
			.Where(call =>
				call.Expression.ToString() == "RpcId"
				&& call.ArgumentList.Arguments.Count >= 2
				&& call.ArgumentList.Arguments[1].Expression.ToString() == "nameof(NetRecvChunk)"
			)
			.ToArray();
		Assert.NotEmpty(sends);
		foreach (var send in sends)
			Assert.Equal(
				receiver.ParameterList.Parameters.Count,
				send.ArgumentList.Arguments.Count - 2
			);
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using BrickVerse.Utils.Compression;

namespace BrickVerse.Tests;

public sealed class CompressionUtilsTests
{
	[Fact]
	public void BoundedDecompress_RoundTripsPayloadWithinLimit()
	{
		byte[] source = new byte[4096];
		new Random(42).NextBytes(source);

		byte[] compressed = ZstdCompressionUtils.Compress(source);

		Assert.Equal(source, ZstdCompressionUtils.Decompress(compressed, source.Length));
	}

	[Fact]
	public void BoundedDecompress_RejectsPayloadOverLimit()
	{
		byte[] compressed = ZstdCompressionUtils.Compress(new byte[4096]);

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
			() => ZstdCompressionUtils.Decompress(compressed, 1024)
		);

		Assert.Contains("exceeding", exception.Message);
	}
}

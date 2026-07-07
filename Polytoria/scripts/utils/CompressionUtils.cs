// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using ZstdSharp;

namespace Polytoria.Utils.Compression;

public static class ZstdCompressionUtils
{
	public static byte[] Compress(byte[] data)
	{
		if (data == null || data.Length == 0) return [];

		using Compressor compressor = new();
		Span<byte> compressed = compressor.Wrap(data);
		return compressed.ToArray();
	}

	public static byte[] Decompress(byte[] compressedData)
	{
		if (compressedData == null || compressedData.Length == 0) return [];

		using Decompressor decompressor = new();
		Span<byte> decompressed = decompressor.Unwrap(compressedData);
		return decompressed.ToArray();
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Polytoria.Utils;

public static class ArrayUtils
{
	public static T[][] Chunk<T>(this T[] array, int chunkSize)
	{
		if (chunkSize <= 0)
		{
			throw new ArgumentException("chunkSize must be > 0");
		}

		int totalChunks = (array.Length + chunkSize - 1) / chunkSize; // ceil division
		T[][] result = new T[totalChunks][];

		for (int i = 0; i < totalChunks; i++)
		{
			int currentChunkSize = Math.Min(chunkSize, array.Length - (i * chunkSize));
			T[] chunk = new T[currentChunkSize];
			Array.Copy(array, i * chunkSize, chunk, 0, currentChunkSize);
			result[i] = chunk;
		}

		return result;
	}

	private static readonly Random _rng = new();

	public static T GetRandom<T>(List<T> list)
	{
		if (list == null || list.Count == 0)
		{
			throw new InvalidOperationException("List is empty or null.");
		}

		int index = _rng.Next(list.Count);
		return list[index];
	}

	public static T GetRandom<T>(T[] array)
	{
		if (array == null || array.Length == 0)
		{
			throw new InvalidOperationException("Array is empty or null.");
		}

		int index = _rng.Next(array.Length);
		return array[index];
	}
}

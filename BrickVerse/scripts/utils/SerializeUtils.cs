// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using MemoryPack;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;

namespace Polytoria.Utils;

public static class SerializeUtils
{
	public static byte[] Serialize(object data)
	{
		return MemoryPackSerializer.Serialize(data);
	}

	public static byte[] Serialize(Type type, object data)
	{
		return MemoryPackSerializer.Serialize(type, data);
	}

	public static byte[] Serialize<T>(T data)
	{
		return MemoryPackSerializer.Serialize(data);
	}

	public static T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
	{
		return MemoryPackSerializer.Deserialize<T>(data);
	}

	public static object? Deserialize(Type cla, byte[] data)
	{
		return MemoryPackSerializer.Deserialize(cla, data);
	}

	public static ValueTask SerializeAsync(Stream stream, object? value)
	{
		return MemoryPackSerializer.SerializeAsync(stream, value);
	}

	public static ValueTask SerializeAsync(Type t, Stream stream, object? value)
	{
		return MemoryPackSerializer.SerializeAsync(t, stream, value);
	}

	public static ValueTask SerializeAsync<T>(Stream stream, T? value)
	{
		return MemoryPackSerializer.SerializeAsync(stream, value);
	}

	public static ValueTask<T?> DeserializeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Stream stream)
	{
		return MemoryPackSerializer.DeserializeAsync<T>(stream);
	}

	public static ValueTask<object?> DeserializeAsync(Type t, Stream stream)
	{
		return MemoryPackSerializer.DeserializeAsync(t, stream);
	}
}

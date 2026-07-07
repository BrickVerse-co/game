// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using MemoryPack;
using Polytoria.Attributes;
using Polytoria.Networking.Synchronizers;
using Polytoria.Scripting;
#if DEBUG
using Polytoria.Shared;
#endif
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Data;

[Internal]
public partial class InternalNetMsg : IScriptObject
{
	public bool BroadcastAll = false;
	public int OriginSender = 0;
	public string Target = "";
	public int TargetMethod;
	public List<byte[]> ByteArrays = [];
	public string StackTrace = "";

	public async Task AddValueAsync(object? value)
	{
		ByteArrays.Add(await NetworkPropSync.SerializePropValueAsync(value));
	}

	public void AddValue(object? value)
	{
		ByteArrays.Add(NetworkPropSync.SerializePropValue(value));
	}

	public async Task<byte[]> SerializeAsync()
	{
		InternalNetMsgPayload payload = new()
		{
			BroadcastAll = BroadcastAll,
			Target = Target,
			TargetMethod = TargetMethod,
			ByteArrays = [.. ByteArrays],
			OriginSender = OriginSender,
		};

#if DEBUG
		if (Globals.UseNetTrace)
		{
			payload.StackTrace = System.Environment.StackTrace;
		}
#endif

		using MemoryStream stream = new();
		await SerializeUtils.SerializeAsync(stream, payload);
		return stream.ToArray();
	}

	public byte[] Serialize()
	{
		InternalNetMsgPayload payload = new()
		{
			BroadcastAll = BroadcastAll,
			Target = Target,
			TargetMethod = TargetMethod,
			ByteArrays = [.. ByteArrays],
			OriginSender = OriginSender,
#if DEBUG
			StackTrace = Globals.UseNetTrace ? System.Environment.StackTrace : "",
#endif
		};

		return SerializeUtils.Serialize(payload);
	}

	public static async Task<InternalNetMsg> DeserializeAsync(byte[] rawdata)
	{
		using MemoryStream stream = new(rawdata);
		InternalNetMsgPayload? payload = await SerializeUtils.DeserializeAsync<InternalNetMsgPayload>(stream) ?? throw new Exception("Message is invalid");
		InternalNetMsg msg = new()
		{
			BroadcastAll = payload.BroadcastAll,
			Target = payload.Target,
			TargetMethod = payload.TargetMethod,
			ByteArrays = [.. payload.ByteArrays],
			OriginSender = payload.OriginSender,
#if DEBUG
			StackTrace = payload.StackTrace
#endif
		};
		return msg;
	}

	public static InternalNetMsg Deserialize(byte[] rawdata)
	{
		InternalNetMsgPayload payload = SerializeUtils.Deserialize<InternalNetMsgPayload>(rawdata) ?? throw new Exception("Message is invalid");

		return new InternalNetMsg()
		{
			BroadcastAll = payload.BroadcastAll,
			Target = payload.Target,
			TargetMethod = payload.TargetMethod,
			ByteArrays = [.. payload.ByteArrays],
			OriginSender = payload.OriginSender,
#if DEBUG
			StackTrace = payload.StackTrace
#endif
		};
	}

	[MemoryPackable]
	public partial class InternalNetMsgPayload
	{
		public bool BroadcastAll = false;
		public int OriginSender = 0;
		public string Target = "";
		public int TargetMethod;
		public byte[][] ByteArrays = [];
		public string StackTrace = "";
	}
}

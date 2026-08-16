// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using BrickVerse.Utils;
using BrickVerse.Utils.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections;

namespace BrickVerse.Datamodel.Data;

public partial class NetMessage : IScriptObject
{
	public Dictionary<string, string> Strings = [];
	public Dictionary<string, int> Ints = [];
	public Dictionary<string, float> Numbers = [];
	public Dictionary<string, bool> Bools = [];
	public Dictionary<string, Vector2> Vec2s = [];
	public Dictionary<string, Vector3> Vec3s = [];
	public Dictionary<string, Color> Colors = [];
	public Dictionary<string, Instance> Instances = [];
	public Dictionary<string, byte[]> Buffers = [];

	[ScriptMethod]
	public void AddString(string key, string value)
	{
		Strings.Add(key, value);
	}

	[ScriptMethod]
	public void AddInt(string key, int value)
	{
		Ints.Add(key, value);
	}

	[ScriptMethod]
	public void AddBool(string key, bool value)
	{
		Bools.Add(key, value);
	}

	[ScriptMethod]
	public void AddNumber(string key, float value)
	{
		Numbers.Add(key, value);
	}

	[ScriptMethod]
	public void AddVector2(string key, Vector2 value)
	{
		Vec2s.Add(key, value);
	}

	[ScriptMethod]
	public void AddVector3(string key, Vector3 value)
	{
		Vec3s.Add(key, value);
	}

	[ScriptMethod]
	public void AddColor(string key, Color value)
	{
		Colors.Add(key, value);
	}

	[ScriptMethod]
	public void AddInstance(string key, Instance value)
	{
		Instances.Add(key, value);
	}

	[ScriptMethod]
	public void AddBuffer(string key, byte[] buffer)
	{
		Buffers.Add(key, buffer);
	}

	[ScriptMethod]
	public string? GetString(string key) => Strings.TryGetValue(key, out var value) ? value : null;

	[ScriptMethod]
	public int? GetInt(string key) => Ints.TryGetValue(key, out var value) ? value : (int?)null;

	[ScriptMethod]
	public float? GetNumber(string key) => Numbers.TryGetValue(key, out var value) ? value : (float?)null;

	[ScriptMethod]
	public bool? GetBool(string key) => Bools.TryGetValue(key, out var value) ? value : (bool?)null;

	[ScriptMethod]
	public Vector2? GetVector2(string key) => Vec2s.TryGetValue(key, out var value) ? value : (Vector2?)null;

	[ScriptMethod]
	public Vector3? GetVector3(string key) => Vec3s.TryGetValue(key, out var value) ? value : (Vector3?)null;

	[ScriptMethod]
	public Color? GetColor(string key) => Colors.TryGetValue(key, out var value) ? value : (Color?)null;

	[ScriptMethod]
	public Instance? GetInstance(string key) => Instances.TryGetValue(key, out var value) ? value : null;

	[ScriptMethod]
	public byte[]? GetBuffer(string key) => Buffers.TryGetValue(key, out var value) ? value : null;

	[ScriptMethod]
	public static NetMessage New()
	{
		return new NetMessage();
	}

	public static NetMessage FromObject(object? value)
	{
		if (value == null) return new();
		if (value is NetMessage message) return message;
		if (value is not IDictionary dictionary) throw new ArgumentException("Network payload must be a NetMessage or a table with string keys.");
		NetMessage result = new();
		foreach (DictionaryEntry pair in dictionary)
		{
			if (pair.Key is not string key) throw new ArgumentException("Network payload table keys must be strings.");
			if (pair.Value != null) AddValue(result, key, pair.Value);
		}
		return result;
	}

	internal static void AddValue(NetMessage message, string key, object value)
	{
		switch (value)
		{
			case string v: message.Strings[key] = v; break;
			case bool v: message.Bools[key] = v; break;
			case byte or short or int: message.Ints[key] = Convert.ToInt32(value); break;
			case long v when v >= int.MinValue && v <= int.MaxValue: message.Ints[key] = (int)v; break;
			case float or double or decimal: message.Numbers[key] = Convert.ToSingle(value); break;
			case Vector2 v: message.Vec2s[key] = v; break;
			case Vector3 v: message.Vec3s[key] = v; break;
			case Color v: message.Colors[key] = v; break;
			case Instance v: message.Instances[key] = v; break;
			case byte[] v: message.Buffers[key] = v; break;
			default: throw new ArgumentException($"Unsupported network value for '{key}': {value.GetType().Name}");
		}
	}

	internal static NetMessage FromPayload(NetMessagePayload payload)
	{
		NetMessage msg = new() { Strings = payload.Strings, Ints = payload.Ints, Numbers = payload.Numbers, Bools = payload.Bools, Buffers = payload.Buffers };
		foreach ((string key, Vector2Dto value) in payload.Vec2s) msg.Vec2s[key] = value.ToVector2();
		foreach ((string key, Vector3Dto value) in payload.Vec3s) msg.Vec3s[key] = value.ToVector3();
		foreach ((string key, ColorDto value) in payload.Colors) msg.Colors[key] = value.ToColor();
		return msg;
	}

	public byte[] Serialize()
	{
		NetMessagePayload payload = new()
		{
			Strings = Strings,
			Ints = Ints,
			Numbers = Numbers,
			Bools = Bools,
			Buffers = Buffers,
		};
		foreach ((string key, Vector2 v2) in Vec2s)
		{
			payload.Vec2s[key] = new Vector2Dto(v2);
		}
		foreach ((string key, Vector3 v3) in Vec3s)
		{
			payload.Vec3s[key] = new Vector3Dto(v3);
		}
		foreach ((string key, Color c) in Colors)
		{
			payload.Colors[key] = new ColorDto(c);
		}
		foreach ((string key, Instance i) in Instances)
		{
			payload.Instances[key] = i.NetworkedObjectID;
		}
		return SerializeUtils.Serialize(payload);
	}

	public static async Task<NetMessage> Deserialize(byte[] rawdata)
	{
		NetMessagePayload? payload = SerializeUtils.Deserialize<NetMessagePayload>(rawdata) ?? throw new Exception("Message is invalid");
		NetMessage msg = FromPayload(payload);
		foreach ((string key, string netID) in payload.Instances)
		{
			NetworkedObject? netobj = await World.Current!.WaitForNetObjectAsync(netID);
			if (netobj != null && netobj is Instance i)
			{
				msg.Instances[key] = i;
			}
		}
		return msg;
	}

	[MemoryPackable]
	public partial class NetMessagePayload
	{
		public Dictionary<string, string> Strings = [];
		public Dictionary<string, int> Ints = [];
		public Dictionary<string, float> Numbers = [];
		public Dictionary<string, bool> Bools = [];
		public Dictionary<string, Vector2Dto> Vec2s = [];
		public Dictionary<string, Vector3Dto> Vec3s = [];
		public Dictionary<string, ColorDto> Colors = [];
		public Dictionary<string, string> Instances = [];
		public Dictionary<string, byte[]> Buffers = [];
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Utils.DTOs;

[MemoryPackable]
public partial class Vector2Dto
{
	public float X { get; set; }
	public float Y { get; set; }

	[MemoryPackConstructor, JsonConstructor]
	public Vector2Dto() { }
	public Vector2Dto(Vector2 v) { X = v.X; Y = v.Y; }
	public Vector2 ToVector2() => new(X, Y);

	public static string ToString(Vector2 src)
	{
		return $"{src.X},{src.Y}";
	}

	public static Vector2 FromString(string src)
	{
		string[] parts = src.Split(',');
		return new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
	}
}

public class Vector2JsonConverter : JsonConverter<Vector2>
{
	public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
		{
			throw new JsonException("Expected start of array");
		}

		reader.Read();
		float x = reader.GetSingle();

		reader.Read();
		float y = reader.GetSingle();

		reader.Read();
		if (reader.TokenType != JsonTokenType.EndArray)
		{
			throw new JsonException("Expected end of array");
		}

		return new Vector2(x, y);
	}

	public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		writer.WriteNumberValue(value.X);
		writer.WriteNumberValue(value.Y);
		writer.WriteEndArray();
	}
}

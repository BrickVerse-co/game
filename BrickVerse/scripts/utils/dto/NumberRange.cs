// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using MemoryPack;
using Polytoria.Datamodel.Data;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Utils.DTOs;

[MemoryPackable]
public partial class NumberRangeDto
{
	public float Min { get; set; }
	public float Max { get; set; }

	[MemoryPackConstructor, JsonConstructor]
	public NumberRangeDto() { }

	public NumberRangeDto(NumberRange range) { Min = range.Min; Max = range.Max; }

	public NumberRange ToNumberRange() => new() { Min = Min, Max = Max };
}

public class NumberRangeJsonConverter : JsonConverter<NumberRange>
{
	public override NumberRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
			throw new JsonException("Expected array format [min, max]");

		reader.Read();
		float min = reader.GetSingle();

		reader.Read();
		float max = reader.GetSingle();

		reader.Read();
		if (reader.TokenType != JsonTokenType.EndArray)
			throw new JsonException("Expected array with exactly 2 elements");

		return new NumberRange { Min = min, Max = max };
	}

	public override void Write(Utf8JsonWriter writer, NumberRange value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		writer.WriteNumberValue(value.Min);
		writer.WriteNumberValue(value.Max);
		writer.WriteEndArray();
	}
}

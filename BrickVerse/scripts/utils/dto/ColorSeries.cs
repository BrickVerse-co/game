// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using Polytoria.Datamodel.Data;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Utils.DTOs;


[MemoryPackable]
public partial class ColorSeriesDto
{
	public GradientPointDto[] Points { get; set; } = [];

	[MemoryPackConstructor, JsonConstructor]
	public ColorSeriesDto() { }

	public ColorSeriesDto(ColorSeries colorRange)
	{
		Points = new GradientPointDto[colorRange.PointCount];
		for (int i = 0; i < colorRange.PointCount; i++)
		{
			Points[i] = new GradientPointDto
			{
				Offset = colorRange.GetOffset(i),
				Color = colorRange.GetColor(i).ToHtml()
			};
		}
	}

	public ColorSeries ToColorRange()
	{
		ColorSeries colorRange = new();

		colorRange.Clear();

		for (int i = 0; i < Points.Length; i++)
		{
			colorRange.SetOffset(i, Points[i].Offset);
			colorRange.SetColor(i, new Color(Points[i].Color));
		}

		return colorRange;
	}
}

[MemoryPackable]
public partial class GradientPointDto
{
	public float Offset { get; set; }
	public string Color { get; set; } = "#ffffffff";
}


public class ColorSeriesJsonConverter : JsonConverter<ColorSeries>
{
	public override ColorSeries Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException("Expected start of object");
		}

		ColorSeries colorRange = new();

		colorRange.Clear();

		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndObject)
			{
				return colorRange;
			}

			if (reader.TokenType != JsonTokenType.PropertyName)
			{
				throw new JsonException("Expected property name");
			}

			string propertyName = reader.GetString()!;

			if (propertyName == "Points")
			{
				reader.Read();
				if (reader.TokenType != JsonTokenType.StartArray)
				{
					throw new JsonException("Expected start of array for points");
				}

				int pointIndex = 0;
				while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
				{
					if (reader.TokenType != JsonTokenType.StartObject)
					{
						throw new JsonException("Expected start of point object");
					}

					float offset = 0f;
					Color color = new();

					while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
					{
						string pointProperty = reader.GetString()!;
						reader.Read();

						switch (pointProperty)
						{
							case "Offset":
								offset = reader.GetSingle();
								break;
							case "Color":
								string hexColor = reader.GetString()!;
								color = new Color(hexColor);
								break;
						}
					}

					colorRange.SetOffset(pointIndex, offset);
					colorRange.SetColor(pointIndex, color);
					pointIndex++;
				}
			}
		}

		throw new JsonException("Unexpected end of JSON");
	}

	public override void Write(Utf8JsonWriter writer, ColorSeries value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WritePropertyName("Points");
		writer.WriteStartArray();

		for (int i = 0; i < value.PointCount; i++)
		{
			writer.WriteStartObject();
			writer.WriteNumber("Offset", value.GetOffset(i));
			writer.WriteString("Color", value.GetColor(i).ToHtml());
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
		writer.WriteEndObject();
	}
}

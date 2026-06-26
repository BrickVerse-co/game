// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text.Json.Serialization;

namespace BrickVerse.Schemas.API;

public struct CreatorAuthResponse
{
	[JsonPropertyName("token")]
	public string Token { get; set; }

	[JsonPropertyName("userID")]
	public string userID { get; set; }

	[JsonPropertyName("placeID")]
	public int? PlaceID { get; set; }
}

public struct CreatorPlaceItem
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("worldId")]
	public int? WorldId { get; set; }

	[JsonPropertyName("universeId")]
	public int? UniverseId { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("updatedAt")]
	public DateTime? UpdatedAt { get; set; }

	[JsonPropertyName("iconUrl")]
	public string IconUrl { get; set; }
}

public struct CreatorPublishResponse
{
	[JsonPropertyName("link")]
	public string Link { get; set; }
}

[JsonSerializable(typeof(CreatorPublishResponse))]
[JsonSerializable(typeof(CreatorPlaceItem))]
[JsonSerializable(typeof(CreatorPlaceItem[]))]
[JsonSerializable(typeof(CreatorAuthResponse))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
internal partial class CreatorAPIGenerationContext : JsonSerializerContext { }

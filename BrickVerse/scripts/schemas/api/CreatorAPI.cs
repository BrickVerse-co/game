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

public struct CreatorGuildItem
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("canEditWorlds")]
	public bool CanEditWorlds { get; set; }
}

public struct CreatorPlaceItem
{
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[JsonPropertyName("worldId")]
	public long? WorldId { get; set; }

	[JsonPropertyName("universeId")]
	public long? UniverseId { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("updatedAt")]
	public DateTime? UpdatedAt { get; set; }

	[JsonPropertyName("iconUrl")]
	public string IconUrl { get; set; }
}

public struct CreatorAssetItem
{
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("assetType")]
	public string Type { get; set; }

	[JsonPropertyName("creatorType")]
	public string CreatorType { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("updatedAt")]
	public DateTime? UpdatedAt { get; set; }

	[JsonPropertyName("textureUrl")]
	public string? IconUrl { get; set; }
}

public struct CreatorPublishResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("link")]
	public string Link { get; set; }

	[JsonPropertyName("worldId")]
	public long WorldId { get; set; }

	[JsonPropertyName("universeId")]
	public long UniverseId { get; set; }
}

public sealed class CreatorGuildsResponse
{
	public CreatorGuildItem[] Guilds { get; set; } = [];
	public PaginationInfo Pagination { get; set; } = new();
}

public sealed class PaginationInfo
{
	public int Page { get; set; }
	public int Limit { get; set; }
	public int Total { get; set; }
	public int TotalPages { get; set; }
	public bool HasNextPage { get; set; }
	public bool HasPreviousPage { get; set; }
}

[JsonSerializable(typeof(CreatorPublishResponse))]
[JsonSerializable(typeof(CreatorPlaceItem))]
[JsonSerializable(typeof(CreatorPlaceItem[]))]
[JsonSerializable(typeof(CreatorAuthResponse))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
internal partial class CreatorAPIGenerationContext : JsonSerializerContext { }

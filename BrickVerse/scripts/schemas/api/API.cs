// (c) 2026 Meta Games LLC. All Rights Reserved.

using System;
using System.Text.Json.Serialization;

namespace BrickVerse.Schemas.API;

public struct OpenIdUserInfoResponse
{
	[JsonPropertyName("sub")]
	public string Sub { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("preferred_username")]
	public string PreferredUsername { get; set; }

	[JsonPropertyName("profile")]
	public string Profile { get; set; }

	[JsonPropertyName("headshotUrl")]
	public string HeadshotUrl { get; set; }

	[JsonPropertyName("bodyshotUrl")]
	public string BodyshotUrl { get; set; }

	[JsonPropertyName("email")]
	public string Email { get; set; }

	[JsonPropertyName("email_verified")]
	public bool EmailVerified { get; set; }

	[JsonPropertyName("phone_number")]
	public string PhoneNumber { get; set; }

	[JsonPropertyName("phone_number_verified")]
	public bool PhoneNumberVerified { get; set; }

	[JsonPropertyName("updated_at")]
	public long UpdatedAt { get; set; }

	[JsonPropertyName("picture")]
	public string Picture { get; set; }

	[JsonPropertyName("guilds")]
	public OpenIdUserInfoGuildMembership[] Guilds { get; set; }
}

public struct OpenIdUserInfoGuildMembership
{
	[JsonPropertyName("rank")]
	public OpenIdUserInfoGuildRank Rank { get; set; }

	[JsonPropertyName("guild")]
	public OpenIdUserInfoGuild Guild { get; set; }
}

public struct OpenIdUserInfoGuildRank
{
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("rankLevel")]
	public int RankLevel { get; set; }

	[JsonPropertyName("permissions")]
	public string Permissions { get; set; }
}

public struct OpenIdUserInfoGuild
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime? CreatedAt { get; set; }

	[JsonPropertyName("updatedAt")]
	public DateTime? UpdatedAt { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("logoId")]
	public string LogoId { get; set; }

	[JsonPropertyName("bannerId")]
	public string BannerId { get; set; }

	[JsonPropertyName("logoUrl")]
	public string LogoUrl { get; set; }

	[JsonPropertyName("bannerUrl")]
	public string BannerUrl { get; set; }

	[JsonPropertyName("creator")]
	public OpenIdUserInfoGuildCreator Creator { get; set; }

	[JsonPropertyName("isVerified")]
	public bool IsVerified { get; set; }

	[JsonPropertyName("isFeatured")]
	public bool IsFeatured { get; set; }

	[JsonPropertyName("isSponsored")]
	public bool IsSponsored { get; set; }

	[JsonPropertyName("isModerated")]
	public bool IsModerated { get; set; }
}

public struct OpenIdUserInfoGuildCreator
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("username")]
	public string Username { get; set; }
}

[JsonSerializable(typeof(OpenIdUserInfoResponse))]
[JsonSerializable(typeof(OpenIdUserInfoGuildMembership))]
[JsonSerializable(typeof(OpenIdUserInfoGuildRank))]
[JsonSerializable(typeof(OpenIdUserInfoGuild))]
[JsonSerializable(typeof(OpenIdUserInfoGuildCreator))]
[JsonSerializable(typeof(OpenIdUserInfoGuildMembership[]))]

internal partial class APIGenerationContextV3 : JsonSerializerContext { }

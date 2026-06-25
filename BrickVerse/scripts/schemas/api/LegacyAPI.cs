// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text.Json.Serialization;

namespace BrickVerse.Schemas.API;

public struct APIUserInfo
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("username")]
	public string Username { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("signature")]
	public string Signature { get; set; }

	[JsonPropertyName("thumbnail")]
	public APIUserThumbnail Thumbnail { get; set; }

	[JsonPropertyName("playing")]
	public object Playing { get; set; }

	[JsonPropertyName("netWorth")]
	public int NetWorth { get; set; }

	[JsonPropertyName("placeVisits")]
	public int PlaceVisits { get; set; }

	[JsonPropertyName("profileViews")]
	public int ProfileViews { get; set; }

	[JsonPropertyName("forumPosts")]
	public int ForumPosts { get; set; }

	[JsonPropertyName("assetSales")]
	public int AssetSales { get; set; }

	[JsonPropertyName("membershipType")]
	public string MembershipType { get; set; }

	[JsonPropertyName("isStaff")]
	public bool IsStaff { get; set; }

	[JsonPropertyName("userRoleClass")]
	public string UserRoleClass { get; set; }

	[JsonPropertyName("registeredAt")]
	public DateTime RegisteredAt { get; set; }

	[JsonPropertyName("lastSeenAt")]
	public DateTime LastSeenAt { get; set; }
}

public struct APIUserThumbnail
{
	[JsonPropertyName("avatar")]
	public string Avatar { get; set; }

	[JsonPropertyName("icon")]
	public string Icon { get; set; }
}

public struct APIAvatarResponse
{
	[JsonPropertyName("colors")]
	public APIAvatarBodyColors Colors { get; set; }
	[JsonPropertyName("assets")]
	public APIAvatarAsset[] Assets { get; set; }
	[JsonPropertyName("isDefault")]
	public bool IsDefault { get; set; }
}

public struct APIAvatarAsset
{
	[JsonPropertyName("id")]
	public string ID { get; set; }
	[JsonPropertyName("type")]
	public string Type { get; set; }
	[JsonPropertyName("accessoryType")]
	public string AccessoryType { get; set; }
	[JsonPropertyName("name")]
	public string Name { get; set; }
	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }
	[JsonPropertyName("path")]
	public string Path { get; set; }
}

public struct APIAvatarBodyColors
{
	[JsonPropertyName("head")]
	public string Head { get; set; }
	[JsonPropertyName("torso")]
	public string Torso { get; set; }
	[JsonPropertyName("leftArm")]
	public string LeftArm { get; set; }
	[JsonPropertyName("rightArm")]
	public string RightArm { get; set; }
	[JsonPropertyName("leftLeg")]
	public string LeftLeg { get; set; }
	[JsonPropertyName("rightLeg")]
	public string RightLeg { get; set; }
}
public struct APILibraryResponse
{
	[JsonPropertyName("meta")]
	public APIMeta Meta { get; set; }

	[JsonPropertyName("data")]
	public APILibraryItem[] Data { get; set; }
}

public struct APITokenDataResponse
{
	[JsonPropertyName("success")]
	public bool? Success { get; set; }

	[JsonPropertyName("token")]
	public string Token { get; set; }

	[JsonPropertyName("userID")]
	public uint UserID { get; set; }

	[JsonPropertyName("placeID")]
	public uint PlaceID { get; set; }
}

public struct APILibraryItem
{
	[JsonPropertyName("id")]
	public uint ID { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("thumbnailUrl")]
	public string ThumbnailUrl { get; set; }

	[JsonPropertyName("creatorID")]
	public int CreatorID { get; set; }

	[JsonPropertyName("creatorName")]
	public string CreatorName { get; set; }

	[JsonPropertyName("creatorUrl")]
	public string CreatorUrl { get; set; }
}

public struct APIV3AuthMeRoot
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("user")]
	public APIV3AuthMeUser User { get; set; }
}

public struct APIV3AuthMeUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("gender")]
    public string Gender { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("membershipLevel")]
    public string MembershipLevel { get; set; }

    [JsonPropertyName("headshotId")]
    public string? HeadshotId { get; set; }

    [JsonPropertyName("bodyshotId")]
    public string? BodyshotId { get; set; }

    [JsonPropertyName("headshotUrl")]
    public string? HeadshotUrl { get; set; }

    [JsonPropertyName("bodyshotUrl")]
    public string? BodyshotUrl { get; set; }

    [JsonPropertyName("isVerified")]
    public bool IsVerified { get; set; }

    [JsonPropertyName("isPartner")]
    public bool IsPartner { get; set; }

    [JsonPropertyName("isStarCreator")]
    public bool IsStarCreator { get; set; }

    [JsonPropertyName("isBetaTester")]
    public bool IsBetaTester { get; set; }

    [JsonPropertyName("trustedReporter")]
    public bool TrustedReporter { get; set; }

    [JsonPropertyName("inStudio")]
    public bool InStudio { get; set; }

    [JsonPropertyName("isModerator")]
    public bool IsModerator { get; set; }

    [JsonPropertyName("verifiedEmail")]
    public bool VerifiedEmail { get; set; }

    [JsonPropertyName("twoFactorEnabled")]
    public bool TwoFactorEnabled { get; set; }

    [JsonPropertyName("twoFactorMethod")]
    public string? TwoFactorMethod { get; set; }

    [JsonPropertyName("canUseSupportDesk")]
    public bool CanUseSupportDesk { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("ageBand")]
    public string AgeBand { get; set; }

    [JsonPropertyName("isUnder13")]
    public bool IsUnder13 { get; set; }

    [JsonPropertyName("cubes")]
    public int Cubes { get; set; }

    [JsonPropertyName("ugcCredit")]
    public int UgcCredit { get; set; }

    [JsonPropertyName("xp")]
    public int Xp { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("profileViews")]
    public int ProfileViews { get; set; }

    [JsonPropertyName("serverRegion")]
    public string? ServerRegion { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("lastLoginAt")]
    public DateTime? LastLoginAt { get; set; }

    [JsonPropertyName("lastSeenAt")]
    public DateTime? LastSeenAt { get; set; }

    [JsonPropertyName("hasPurchased")]
    public bool HasPurchased { get; set; }

    [JsonPropertyName("hasParentalPin")]
    public bool HasParentalPin { get; set; }

    [JsonPropertyName("hasPassword")]
    public bool HasPassword { get; set; }

    [JsonPropertyName("levelProgressPercent")]
    public int LevelProgressPercent { get; set; }

    [JsonPropertyName("currentLevelXp")]
    public int CurrentLevelXp { get; set; }

    [JsonPropertyName("nextLevelXp")]
    public int NextLevelXp { get; set; }

    [JsonPropertyName("xpIntoLevel")]
    public int XpIntoLevel { get; set; }

    [JsonPropertyName("xpForNextLevel")]
    public int XpForNextLevel { get; set; }
}

public struct APIV3UserProfileRoot
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("user")]
	public APIV3UserProfileUser User { get; set; }
}

public struct APIV3UserProfileUser
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("username")]
	public string Username { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("status")]
	public string Status { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("lastSeenAt")]
	public DateTime LastSeenAt { get; set; }

	[JsonPropertyName("statistics")]
	public APIV3UserProfileStatistics Statistics { get; set; }
}

public struct APIV3UserProfileStatistics
{
	[JsonPropertyName("visits")]
	public int Visits { get; set; }

	[JsonPropertyName("profileViews")]
	public int ProfileViews { get; set; }

	[JsonPropertyName("forumPosts")]
	public int ForumPosts { get; set; }
}

public struct APIV3CharacterAppearanceRoot
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("appearance")]
	public APIV3CharacterAppearance Appearance { get; set; }
}

public struct APIV3CharacterAppearance
{
	[JsonPropertyName("headColor")]
	public string HeadColor { get; set; }

	[JsonPropertyName("torsoColor")]
	public string TorsoColor { get; set; }

	[JsonPropertyName("leftArmColor")]
	public string LeftArmColor { get; set; }

	[JsonPropertyName("rightArmColor")]
	public string RightArmColor { get; set; }

	[JsonPropertyName("leftLegColor")]
	public string LeftLegColor { get; set; }

	[JsonPropertyName("rightLegColor")]
	public string RightLegColor { get; set; }

	[JsonPropertyName("accessories")]
	public APIV3CharacterAccessory[] Accessories { get; set; }
}

public struct APIV3CharacterAccessory
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("thumbnailUrl")]
	public string ThumbnailUrl { get; set; }
}

public struct APIV3SocialGuildRoot
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("guild")]
	public APIV3SocialGuild Guild { get; set; }
}

public struct APIV3SocialGuild
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("joinType")]
	public string JoinType { get; set; }

	[JsonPropertyName("memberCount")]
	public int MemberCount { get; set; }

	[JsonPropertyName("isVerified")]
	public bool IsVerified { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("creator")]
	public APIV3SocialGuildCreator Creator { get; set; }
}

public struct APIV3SocialGuildCreator
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("username")]
	public string Username { get; set; }
}

public struct APIV3AssetDetailsRoot
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("assetInfo")]
	public APIV3AssetDetails AssetInfo { get; set; }
}

public struct APIV3AssetDetails
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("assetType")]
	public string AssetType { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("creatorId")]
	public string CreatorId { get; set; }

	[JsonPropertyName("creatorType")]
	public string CreatorType { get; set; }

	[JsonPropertyName("price")]
	public int Price { get; set; }

	[JsonPropertyName("sales")]
	public int Sales { get; set; }

	[JsonPropertyName("favorites")]
	public int Favorites { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("updatedAt")]
	public DateTime? UpdatedAt { get; set; }
}

public struct APIV3AssetDiscoverRoot
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("assets")]
	public APIV3AssetDiscoverItem[] Assets { get; set; }

	[JsonPropertyName("nextCursor")]
	public string? NextCursor { get; set; }
}

public struct APIV3AssetDiscoverItem
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("thumbnailId")]
	public string? ThumbnailId { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("creatorId")]
	public string CreatorId { get; set; }

	[JsonPropertyName("creatorName")] 
	public string CreatorName { get; set; }
}

public struct APIV3WorldRoot
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("world")]
	public APIV3WorldInfo World { get; set; }

	[JsonPropertyName("universe")]
	public APIV3UniverseInfo Universe { get; set; }
}

public struct APIV3WorldInfo
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("totalVisits")]
	public int TotalVisits { get; set; }

	[JsonPropertyName("totalPlayers")]
	public int TotalPlayers { get; set; }

	[JsonPropertyName("maxPlayers")]
	public int MaxPlayers { get; set; }
}

public struct APIV3UniverseInfo
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("genre")]
	public string Genre { get; set; }

	[JsonPropertyName("creatorId")]
	public string CreatorId { get; set; }

	[JsonPropertyName("creatorType")]
	public string CreatorType { get; set; }

	[JsonPropertyName("creatorUser")]
	public APIV3UniverseCreatorUser? CreatorUser { get; set; }

	[JsonPropertyName("creatorGuild")]
	public APIV3UniverseCreatorGuild? CreatorGuild { get; set; }

	[JsonPropertyName("universeThumbnails")]
	public APIV3UniverseThumbnail[] UniverseThumbnails { get; set; }
}

public struct APIV3UniverseCreatorUser
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("username")]
	public string Username { get; set; }
}

public struct APIV3UniverseCreatorGuild
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }
}

public struct APIV3UniverseThumbnail
{
	[JsonPropertyName("thumbnailId")]
	public string ThumbnailId { get; set; }
}

public struct APIV3JoinWorldRequest
{
	[JsonPropertyName("platform")]
	public string Platform { get; set; }

	[JsonPropertyName("universeId")]
	public string UniverseId { get; set; }

	[JsonPropertyName("worldId")]
	public string WorldId { get; set; }
}

public struct APIV3JoinWorldResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("joinToken")]
	public string JoinToken { get; set; }

	[JsonPropertyName("ip")]
	public string IP { get; set; }

	[JsonPropertyName("port")]
	public int Port { get; set; }
}

public struct APIFontMeta
{
	[JsonPropertyName("fonts")]
	public APIFontData[] Fonts { get; set; }
	[JsonPropertyName("pages")]
	public int Pages { get; set; }
	[JsonPropertyName("total")]
	public int Total { get; set; }
}

public struct APIFontData
{
	[JsonPropertyName("name")]
	public string Name { get; set; }
	[JsonPropertyName("preview")]
	public string Preview { get; set; }
	[JsonPropertyName("font")]
	public string Font { get; set; }
}

public struct APIMobileTokenRequest
{
	[JsonPropertyName("code")]
	public string Code { get; set; }
	[JsonPropertyName("state")]
	public string State { get; set; }
}

public struct APIMobileTokenResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }
	[JsonPropertyName("userID")]
	public ulong UserID { get; set; }
	[JsonPropertyName("token")]
	public string Token { get; set; }
}

public struct APIPlaceCreator
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }
}

public struct APIPlaceRating
{
	[JsonPropertyName("likes")]
	public int Likes { get; set; }

	[JsonPropertyName("dislikes")]
	public int Dislikes { get; set; }

	[JsonPropertyName("percent")]
	public string Percent { get; set; }
}

public struct APIPlaceInfo
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("creator")]
	public APIPlaceCreator Creator { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }

	[JsonPropertyName("genre")]
	public string Genre { get; set; }

	[JsonPropertyName("maxPlayers")]
	public int MaxPlayers { get; set; }

	[JsonPropertyName("isActive")]
	public bool IsActive { get; set; }

	[JsonPropertyName("isToolsEnabled")]
	public bool IsToolsEnabled { get; set; }

	[JsonPropertyName("isCopyable")]
	public bool IsCopyable { get; set; }

	[JsonPropertyName("visits")]
	public int Visits { get; set; }

	[JsonPropertyName("uniqueVisits")]
	public int UniqueVisits { get; set; }

	[JsonPropertyName("playing")]
	public int Playing { get; set; }

	[JsonPropertyName("rating")]
	public APIPlaceRating Rating { get; set; }

	[JsonPropertyName("accessType")]
	public string AccessType { get; set; }

	[JsonPropertyName("accessPrice")]
	public object AccessPrice { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("updatedAt")]
	public DateTime? UpdatedAt { get; set; }
}
public struct APIGuildCreator
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }
}
public struct APIGuildInfo
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("creator")]
	public APIGuildCreator Creator { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }

	[JsonPropertyName("banner")]
	public string Banner { get; set; }

	[JsonPropertyName("color")]
	public string Color { get; set; }

	[JsonPropertyName("joinType")]
	public string Jointype { get; set; }

	[JsonPropertyName("memberCount")]
	public int MemberCount { get; set; }

	[JsonPropertyName("vaultBalance")]
	public int VaultBalance { get; set; }

	[JsonPropertyName("isVerified")]
	public bool IsVerified { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }
}

public struct APIMeResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("membershipLevel")]
    public string MembershipLevel { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("headshotUrl")]
    public string HeadshotUrl { get; set; }

    [JsonPropertyName("bodyshotUrl")]
    public string BodyshotUrl { get; set; }

    [JsonPropertyName("isVerified")]
    public bool IsVerified { get; set; }

    [JsonPropertyName("isPartner")]
    public bool IsPartner { get; set; }

    [JsonPropertyName("isModerator")]
    public bool IsModerator { get; set; }

    [JsonPropertyName("membershipLevel")]
    public string MembershipType { get; set; }

    [JsonPropertyName("cubes")]
    public int Cubes { get; set; }

    [JsonPropertyName("ugcCredit")]
    public int UgcCredit { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastSeenAt")]
    public DateTime? LastSeenAt { get; set; }
}

public struct APIWorldsData
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("creatorType")]
	public string CreatorType { get; set; }

	[JsonPropertyName("creatorID")]
	public int CreatorID { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("genre")]
	public string Genre { get; set; }

	[JsonPropertyName("placeType")]
	public string PlaceType { get; set; }

	[JsonPropertyName("genreIcon")]
	public string GenreIcon { get; set; }

	[JsonPropertyName("creatorName")]
	public string CreatorName { get; set; }

	[JsonPropertyName("creatorThumbnail")]
	public string CreatorThumbnail { get; set; }

	[JsonPropertyName("visits")]
	public int Visits { get; set; }

	[JsonPropertyName("playing")]
	public int Playing { get; set; }

	[JsonPropertyName("rating")]
	public double? Rating { get; set; }

	[JsonPropertyName("iconUrl")]
	public string IconUrl { get; set; }
}

public struct APIMeta
{
	[JsonPropertyName("total")]
	public int Total { get; set; }

	[JsonPropertyName("perPage")]
	public int PerPage { get; set; }

	[JsonPropertyName("currentPage")]
	public int CurrentPage { get; set; }

	[JsonPropertyName("lastPage")]
	public int LastPage { get; set; }

	[JsonPropertyName("firstPage")]
	public int FirstPage { get; set; }

	[JsonPropertyName("firstPageURL")]
	public string? FirstPageURL { get; set; }

	[JsonPropertyName("lastPageURL")]
	public string? LastPageURL { get; set; }

	[JsonPropertyName("nextPageURL")]
	public string? NextPageURL { get; set; }

	[JsonPropertyName("previousPageURL")]
	public string? PreviousPageURL { get; set; }
}

public struct APIWorldsRoot
{
	[JsonPropertyName("meta")]
	public APIMeta Meta { get; set; }

	[JsonPropertyName("data")]
	public APIWorldsData[] Data { get; set; }
}

public struct APIJoinPlaceResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("token")]
	public string Token { get; set; }
}

public struct APIJoinPlaceRequest
{
	[JsonPropertyName("placeID")]
	public int PlaceID { get; set; }

	[JsonPropertyName("isBeta")]
	public bool IsBeta { get; set; }
}

public struct APIFeedPostAuthor
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("username")]
	public string Username { get; set; }

	[JsonPropertyName("avatarID")]
	public string AvatarID { get; set; }

	[JsonPropertyName("membershipType")]
	public string MembershipType { get; set; }

	[JsonPropertyName("isOnline")]
	public bool IsOnline { get; set; }

	[JsonPropertyName("avatarIconUrl")]
	public string AvatarIconUrl { get; set; }

	[JsonPropertyName("isStaff")]
	public bool IsStaff { get; set; }
}

public struct APIFeedPostComment
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("content")]
	public string Content { get; set; }

	[JsonPropertyName("postedAt")]
	public DateTime PostedAt { get; set; }

	[JsonPropertyName("author")]
	public APIFeedPostAuthor Author { get; set; }

	[JsonPropertyName("reportURL")]
	public string ReportURL { get; set; }
}

public struct APIFeedPostData
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("content")]
	public string Content { get; set; }

	[JsonPropertyName("postedAt")]
	public DateTime PostedAt { get; set; }

	[JsonPropertyName("placeID")]
	public int? PlaceID { get; set; }

	[JsonPropertyName("author")]
	public APIFeedPostAuthor Author { get; set; }

	[JsonPropertyName("likeCount")]
	public int LikeCount { get; set; }

	[JsonPropertyName("replyCount")]
	public int ReplyCount { get; set; }

	[JsonPropertyName("isLiked")]
	public bool IsLiked { get; set; }

	[JsonPropertyName("placeName")]
	public string? PlaceName { get; set; }

	[JsonPropertyName("mediaUrl")]
	public string? MediaUrl { get; set; }

	[JsonPropertyName("reportURL")]
	public string ReportURL { get; set; }

	[JsonPropertyName("canBeDeleted")]
	public bool CanBeDeleted { get; set; }

	[JsonPropertyName("comments")]
	public APIFeedPostComment[] Comments { get; set; }
}

public struct APIFeedPostRoot
{
	[JsonPropertyName("meta")]
	public APIMeta Meta { get; set; }

	[JsonPropertyName("data")]
	public APIFeedPostData[] Data { get; set; }
}

public struct APIStoreItemCreator
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }
}

public struct APIStoreItem
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("accessoryType")]
	public string? AccessoryType { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("tags")]
	public string[] Tags { get; set; }

	[JsonPropertyName("creator")]
	public APIStoreItemCreator Creator { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }

	[JsonPropertyName("version")]
	public int Version { get; set; }

	[JsonPropertyName("sales")]
	public int? Sales { get; set; }

	[JsonPropertyName("price")]
	public int? Price { get; set; }

	[JsonPropertyName("favorites")]
	public int? Favorites { get; set; }

	[JsonPropertyName("isLimited")]
	public bool IsLimited { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("updatedAt")]
	public DateTime? UpdatedAt { get; set; }
}

public struct APIOwnsItem
{
	[JsonPropertyName("owned")]
	public bool Owned { get; set; }
}

public struct APIPlaceMedia
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("url")]
	public string Url { get; set; }
}

public enum LibraryQueryTypeEnum
{
	Model,
	Audio,
	Image,
	Mesh,
	Addon
}

[JsonSerializable(typeof(APIMeta))]
[JsonSerializable(typeof(APIUserInfo))]
[JsonSerializable(typeof(APIUserThumbnail))]
[JsonSerializable(typeof(APIAvatarResponse))]
[JsonSerializable(typeof(APIAvatarAsset))]
[JsonSerializable(typeof(APIAvatarBodyColors))]
[JsonSerializable(typeof(APILibraryResponse))]
[JsonSerializable(typeof(APIV3AuthMeRoot))]
[JsonSerializable(typeof(APIV3AuthMeUser))]
[JsonSerializable(typeof(APIV3UserProfileRoot))]
[JsonSerializable(typeof(APIV3UserProfileUser))]
[JsonSerializable(typeof(APIV3UserProfileStatistics))]
[JsonSerializable(typeof(APIV3CharacterAppearanceRoot))]
[JsonSerializable(typeof(APIV3CharacterAppearance))]
[JsonSerializable(typeof(APIV3CharacterAccessory))]
[JsonSerializable(typeof(APIV3SocialGuildRoot))]
[JsonSerializable(typeof(APIV3SocialGuild))]
[JsonSerializable(typeof(APIV3SocialGuildCreator))]
[JsonSerializable(typeof(APIV3AssetDetailsRoot))]
[JsonSerializable(typeof(APIV3AssetDetails))]
[JsonSerializable(typeof(APIV3AssetDiscoverRoot))]
[JsonSerializable(typeof(APIV3AssetDiscoverItem))]
[JsonSerializable(typeof(APIV3WorldRoot))]
[JsonSerializable(typeof(APIV3WorldInfo))]
[JsonSerializable(typeof(APIV3UniverseInfo))]
[JsonSerializable(typeof(APIV3UniverseCreatorUser))]
[JsonSerializable(typeof(APIV3UniverseCreatorGuild))]
[JsonSerializable(typeof(APIV3UniverseThumbnail))]
[JsonSerializable(typeof(APIV3JoinWorldRequest))]
[JsonSerializable(typeof(APIV3JoinWorldResponse))]
[JsonSerializable(typeof(APITokenDataResponse))]
[JsonSerializable(typeof(APILibraryItem))]
[JsonSerializable(typeof(APIFontMeta))]
[JsonSerializable(typeof(APIMobileTokenRequest))]
[JsonSerializable(typeof(APIMobileTokenResponse))]
[JsonSerializable(typeof(APIPlaceCreator))]
[JsonSerializable(typeof(APIPlaceRating))]
[JsonSerializable(typeof(APIPlaceInfo))]
[JsonSerializable(typeof(APIMeResponse))]
[JsonSerializable(typeof(APIWorldsRoot))]
[JsonSerializable(typeof(APIWorldsData))]
[JsonSerializable(typeof(APIStoreItem))]
[JsonSerializable(typeof(APIStoreItemCreator))]
[JsonSerializable(typeof(APIOwnsItem))]
[JsonSerializable(typeof(APIPlaceMedia))]
[JsonSerializable(typeof(APIGuildCreator))]
[JsonSerializable(typeof(APIGuildInfo))]

[JsonSerializable(typeof(APIJoinPlaceResponse))]
[JsonSerializable(typeof(APIJoinPlaceRequest))]

[JsonSerializable(typeof(APIFeedPostRoot))]
[JsonSerializable(typeof(APIFeedPostData))]
[JsonSerializable(typeof(APIFeedPostComment))]
[JsonSerializable(typeof(APIFeedPostAuthor))]

[JsonSerializable(typeof(APIFeedPostData[]))]
[JsonSerializable(typeof(APIFeedPostComment[]))]

[JsonSerializable(typeof(APIAvatarAsset[]))]
[JsonSerializable(typeof(APILibraryItem[]))]
[JsonSerializable(typeof(APIFontData[]))]
[JsonSerializable(typeof(APIWorldsData[]))]
[JsonSerializable(typeof(APIPlaceMedia[]))]
[JsonSerializable(typeof(APIV3AssetDiscoverItem[]))]
[JsonSerializable(typeof(APIV3CharacterAccessory[]))]

[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(double))]

internal partial class APIGenerationContext : JsonSerializerContext { }

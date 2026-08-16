// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BrickVerse.Schemas.API;

public struct APIHeartbeatResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }
	[JsonPropertyName("remove")]
	public List<string>? Remove { get; set; }
	[JsonPropertyName("shouldShutdown")]
	public bool ShouldShutdown { get; set; }
}

public struct APIValidateResponse
{
	[JsonPropertyName("id")]
	public string UserID { get; set; }

	[JsonPropertyName("username")]
	public string Username { get; set; }

	[JsonPropertyName("canChat")]
	public bool CanChat { get; set; }
	[JsonPropertyName("canQuickChat")]
	public bool CanQuickChat { get; set; }
	[JsonPropertyName("canVoiceChat")]
	public bool CanVoiceChat { get; set; }

	[JsonPropertyName("chatRestrictionReason")]
	public string? ChatRestrictionReason { get; set; }

	[JsonPropertyName("isAgeRestricted")]
	public bool IsAgeRestricted { get; set; }

	[JsonPropertyName("isCreator")]
	public bool IsCreator { get; set; }

	[JsonPropertyName("isStaff")]
	public bool IsStaff { get; set; }

	[JsonPropertyName("isStarCreator")]
	public bool IsStarCreator { get; set; }

	[JsonPropertyName("isUniverseTester")]
	public bool IsUniverseTester { get; set; }

	[JsonPropertyName("isUniverseAdmin")]
	public bool IsUniverseAdmin { get; set; }

	[JsonPropertyName("isGovOfficial")]
	public bool IsGovOfficial { get; set; }

	[JsonPropertyName("isBetaTester")]
	public bool IsBetaTester { get; set; }

	[JsonPropertyName("isPartner")]
	public bool IsPartner { get; set; }

	[JsonPropertyName("isTrustedReporter")]
	public bool IsTrustedReporter { get; set; }

	[JsonPropertyName("hasVerifiedBadge")]
	public bool HasVerifiedBadge { get; set; }
}

public struct APIHasAchievementResponse
{
	[JsonPropertyName("userOwns")]
	public bool UserOwns { get; set; }
}

public struct APIPurchaseResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }
}

public struct SocialFriendRequest
{
	[JsonPropertyName("senderId")]
	public string SenderId { get; set; }
	[JsonPropertyName("recipientId")]
	public string RecipientId { get; set; }
}

[JsonSerializable(typeof(APIHeartbeatResponse))]
[JsonSerializable(typeof(APIValidateResponse))]
[JsonSerializable(typeof(APIHasAchievementResponse))]
[JsonSerializable(typeof(APIPurchaseResponse))]
[JsonSerializable(typeof(SocialFriendRequest))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
internal partial class ServerAPIGenerationContext : JsonSerializerContext { }

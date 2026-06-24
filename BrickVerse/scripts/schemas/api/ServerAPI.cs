// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Polytoria.Schemas.API;

public struct APIHeartbeatResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }
	[JsonPropertyName("remove")]
	public List<int> Remove { get; set; }
}

public struct APIValidateResponse
{
	[JsonPropertyName("id")]
	public int UserID { get; set; }
	[JsonPropertyName("username")]
	public string Username { get; set; }
	[JsonPropertyName("canChat")]
	public bool CanChat { get; set; }
	[JsonPropertyName("isAgeRestricted")]
	public bool IsAgeRestricted { get; set; }
	[JsonPropertyName("isCreator")]
	public bool IsCreator { get; set; }
}

public struct APIHasAchievementResponse
{
	[JsonPropertyName("hasAchievement")]
	public bool HasAchievement { get; set; }
}

public struct APIPurchaseResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }
}

[JsonSerializable(typeof(APIHeartbeatResponse))]
[JsonSerializable(typeof(APIValidateResponse))]
[JsonSerializable(typeof(APIHasAchievementResponse))]
[JsonSerializable(typeof(APIPurchaseResponse))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
internal partial class ServerAPIGenerationContext : JsonSerializerContext { }

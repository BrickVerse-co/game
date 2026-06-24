// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Polytoria.Schemas.API;

public struct APIFriendRequest
{
	[JsonPropertyName("userID")]
	public int UserID { get; set; }
	[JsonPropertyName("friendID")]
	public int FriendID { get; set; }
}

public struct APIAreFriendsResponse
{
	[JsonPropertyName("areFriends")]
	public bool AreFriends { get; set; }
}

[JsonSerializable(typeof(APIFriendRequest))]
[JsonSerializable(typeof(APIAreFriendsResponse))]
[JsonSerializable(typeof(int))]
internal partial class SocialAPIGenerationContext : JsonSerializerContext { }

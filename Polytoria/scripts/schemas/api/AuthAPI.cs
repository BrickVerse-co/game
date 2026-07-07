// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Polytoria.Schemas.API;

public struct APIServerAuthRequestMessage
{
	[JsonPropertyName("clientToken")]
	public string ClientToken { get; set; }
	[JsonPropertyName("localUserId")]
	public int LocalUserId { get; set; }
}

public struct APIServerListenResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }
	[JsonPropertyName("placeID")]
	public int WorldID { get; set; }
	[JsonPropertyName("port")]
	public int Port { get; set; }
	[JsonPropertyName("serverID")]
	public int ServerID { get; set; }
	[JsonPropertyName("placePath")]
	public string? PlacePath { get; set; }
}

public struct APIClientAuthResponseMessage
{
	[JsonPropertyName("name")]
	public string PlaceName { get; set; }
	[JsonPropertyName("ip")]
	public string IP { get; set; }
	[JsonPropertyName("port")]
	public int Port { get; set; }
	[JsonPropertyName("placeID")]
	public int WorldID { get; set; }
	[JsonPropertyName("serverID")]
	public int ServerID { get; set; }
}

public struct APIServerStatus
{
	[JsonPropertyName("status")]
	public string Status { get; set; }
}

[JsonSerializable(typeof(APIServerAuthRequestMessage))]
[JsonSerializable(typeof(APIServerListenResponse))]
[JsonSerializable(typeof(APIClientAuthResponseMessage))]
[JsonSerializable(typeof(APIServerStatus))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(string))]
internal partial class AuthAPIGenerationContext : JsonSerializerContext { }

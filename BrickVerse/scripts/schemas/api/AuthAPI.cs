// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace BrickVerse.Schemas.API;

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
	[JsonPropertyName("worldId")]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
	public long WorldID { get; set; }
	[JsonPropertyName("port")]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
	public int Port { get; set; }
	[JsonPropertyName("serverId")]
	public string ServerID { get; set; }
	[JsonPropertyName("placePath")]
	public string? PlacePath { get; set; }
	[JsonPropertyName("maxPlayers")]
	public int MaxPlayers { get; set; }
}

public class APIServerStatus
{
	[JsonPropertyName("status")]
	public string Status { get; set; } = string.Empty;
}

public class APIClientAuthResponseMessage : APIServerStatus
{
	[JsonPropertyName("worldName")]
	public string PlaceName { get; set; } = string.Empty;

	[JsonPropertyName("ip")]
	public string IP { get; set; } = string.Empty;

	[JsonPropertyName("port")]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
	public int Port { get; set; }

	[JsonPropertyName("universeId")]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
	public long UniverseID { get; set; }

	[JsonPropertyName("worldId")]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
	public long WorldID { get; set; }

	[JsonPropertyName("serverId")]
	public string ServerID { get; set; } = string.Empty;
}

[JsonSerializable(typeof(APIServerAuthRequestMessage))]
[JsonSerializable(typeof(APIServerListenResponse))]
[JsonSerializable(typeof(APIClientAuthResponseMessage))]
[JsonSerializable(typeof(APIServerStatus))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(string))]
internal partial class AuthAPIGenerationContext : JsonSerializerContext { }

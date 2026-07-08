// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Schemas.API;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrickVerse.Client.WebAPI;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ClientConnectRequest))]
[JsonSerializable(typeof(ServerListenRequest))]
[JsonSerializable(typeof(HeartbeatRequest))]
[JsonSerializable(typeof(ValidatePlayerRequest))]
[JsonSerializable(typeof(LogIngestRequest))]
[JsonSerializable(typeof(ClientIntegrityProof))]
[JsonSerializable(typeof(APIServerStatus))]
[JsonSerializable(typeof(APIClientAuthResponseMessage))]
[JsonSerializable(typeof(APIServerListenResponse))]
[JsonSerializable(typeof(APIHeartbeatResponse))]
[JsonSerializable(typeof(APIValidateResponse))]
internal sealed partial class BrickVerseJsonContext : JsonSerializerContext
{
}

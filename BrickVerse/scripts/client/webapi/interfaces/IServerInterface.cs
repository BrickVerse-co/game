// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Schemas.API;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI.Interfaces;

public interface IServerInterface
{
	void SetToken(string token);
	Task<byte[]> DownloadWorld(int worldID);
	Task<APIHeartbeatResponse> Heartbeat(string[] playerIDs);
	Task<APIValidateResponse> ValidatePlayer(string token);
	Task LogEvent(ServerEventType eventType, Dictionary<string, string>? data = null);
	Task Log(string log, ServerLogSource source = ServerLogSource.Server, ServerLogLevel level = ServerLogLevel.Info, long? timestampUnixMs = null);
}

public enum ServerEventType
{
	ServerStarted,
	ServerStopped,
	ClientConnected,
	ClientDisconnected
}

public enum ServerLogSource
{
	Server,
	Client
}

public enum ServerLogLevel
{
	Info,
	Warning,
	Error
}

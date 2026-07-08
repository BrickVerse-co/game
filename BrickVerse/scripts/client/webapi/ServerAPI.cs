// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Client.WebAPI.Interfaces;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

public static class ServerAPI
{
	internal static string HostToken = "";
	internal static IServerInterface? ServerInterface { get; set; }

	public static void SetAuthToken(string hostToken)
	{
		HostToken = hostToken;
		ServerInterface?.SetToken(hostToken);
	}

	public static string GetAuthorizationHeaderValue()
	{
		if (string.IsNullOrWhiteSpace(HostToken))
		{
			return "";
		}

		if (HostToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			return HostToken;
		}

		return "Bearer " + HostToken;
	}

	public static Task<byte[]> DownloadWorld(int worldID)
	{
		if (ServerInterface == null) throw new MissingComponentException("Missing server interface component");
		return ServerInterface.DownloadWorld(worldID);
	}

	public static Task<APIHeartbeatResponse> SendHeartbeat(string[] playerIDs)
	{
		if (ServerInterface == null) throw new MissingComponentException("Missing server interface component");
		return ServerInterface.Heartbeat(playerIDs);
	}

	public static Task<APIValidateResponse> ValidatePlayer(string hostToken)
	{
		if (ServerInterface == null) throw new MissingComponentException("Missing server interface component");
		return ServerInterface.ValidatePlayer(hostToken);
	}

	public static Task LogServerEvent(ServerEventType eventType, Dictionary<string, string>? data = null)
	{
		if (ServerInterface == null) throw new MissingComponentException("Missing server interface component");
		return ServerInterface.LogEvent(eventType, data);
	}

	public static Task LogServerLog(string log, ServerLogSource source = ServerLogSource.Server, ServerLogLevel level = ServerLogLevel.Info, long? timestampUnixMs = null)
	{
		if (ServerInterface == null) throw new MissingComponentException("Missing server interface component");
		return ServerInterface.Log(log, source, level, timestampUnixMs);
	}
}

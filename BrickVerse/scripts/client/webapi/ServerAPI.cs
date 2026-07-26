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
	internal static string HostToken { get; private set; } = string.Empty;
	internal static IServerInterface? ServerInterface { get; set; }

	public static void SetAuthToken(string hostToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(hostToken);

		HostToken = hostToken;
		ServerInterface?.SetToken(hostToken);
	}

	public static string GetAuthorizationHeaderValue()
	{
		if (string.IsNullOrWhiteSpace(HostToken))
		{
			return string.Empty;
		}

		if (HostToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			return HostToken;
		}

		return "Bearer " + HostToken;
	}

	private static IServerInterface GetServerInterface()
	{
		return ServerInterface ?? throw new MissingComponentException("Missing server interface component");
	}

	public static Task<byte[]> DownloadWorld(long worldID)
	{
		if (worldID <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(worldID), "worldID must be greater than zero.");
		}

		return GetServerInterface().DownloadWorld(worldID);
	}

	public static Task<APIHeartbeatResponse> SendHeartbeat(string[] playerIDs)
	{
		return GetServerInterface().Heartbeat(playerIDs);
	}

	public static Task<APIValidateResponse> ValidatePlayer(string playerToken)
	{
		return GetServerInterface().ValidatePlayer(playerToken);
	}

	public static Task LogServerEvent(ServerEventType eventType, Dictionary<string, string>? data = null)
	{
		return GetServerInterface().LogEvent(eventType, data);
	}

	public static Task LogServerLog(string log, ServerLogSource source = ServerLogSource.Server, ServerLogLevel level = ServerLogLevel.Info, long? timestampUnixMs = null)
	{
		return GetServerInterface().Log(log, source, level, timestampUnixMs);
	}
}

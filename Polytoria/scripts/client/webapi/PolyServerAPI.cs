// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Client.WebAPI.Interfaces;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Client.WebAPI;

public static class PolyServerAPI
{
	internal static string AuthToken = "";
	internal static IServerInterface? ServerInterface { get; set; }

	public static void SetAuthToken(string userToken)
	{
		AuthToken = userToken;
		ServerInterface?.SetToken(userToken);
	}

	public static Task<byte[]> DownloadWorld(int worldID)
	{
		if (ServerInterface == null) throw new MissingComponentException("Missing server interface component");
		return ServerInterface.DownloadWorld(worldID);
	}

	public static Task<APIHeartbeatResponse> SendHeartbeat(int[] playerIDs)
	{
		if (ServerInterface == null) throw new MissingComponentException("Missing server interface component");
		return ServerInterface.Heartbeat(playerIDs);
	}

	public static Task<APIValidateResponse> ValidatePlayer(string token)
	{
		if (ServerInterface == null) throw new MissingComponentException("Missing server interface component");
		return ServerInterface.ValidatePlayer(token);
	}

	public static Task LogServerEvent(ServerEventType eventType, Dictionary<string, string>? data = null)
	{
		if (ServerInterface == null) throw new MissingComponentException("Missing server interface component");
		return ServerInterface.LogEvent(eventType, data);
	}
}

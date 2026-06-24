// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Client.WebAPI.Interfaces;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

public static class PolyAuthAPI
{
	internal static string Token = "";

	internal static IClientConnector? ClientConnector { get; set; }
	internal static IServerListener? ServerListener { get; set; }

	public static void SetAuthToken(string token)
	{
		Token = token;
		ClientConnector?.SetToken(token);
		ServerListener?.SetToken(token);
	}

	public static Task<APIServerStatus> CheckServerStatus()
	{
		if (ClientConnector == null) throw new MissingComponentException("Client Connector component missing");
		return ClientConnector.CheckServerStatus();
	}

	public static Task<APIClientAuthResponseMessage> SendClientConnect()
	{
		if (ClientConnector == null) throw new MissingComponentException("Client Connector component missing");
		return ClientConnector.Connect();
	}

	public static Task<APIServerListenResponse> SendServerListen()
	{
		if (ServerListener == null) throw new MissingComponentException("Server listener component missing");
		return ServerListener.Listen();
	}
}

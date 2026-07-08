// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Client.WebAPI.Interfaces;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Datamodel.Services;
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

public static class ClientAuthAPI
{
	private static bool _bootstrapped;
	internal static string JoinToken = "";
	internal static IClientConnector? ClientConnector { get; set; }
	internal static IServerListener? ServerListener { get; set; }

	public static void Initialize(bool isServer = false)
	{
		if (_bootstrapped)
		{
			return;
		}

		ClientConnector = new ClientConnector();
		ServerListener = new ServerListener();
		NetworkService.IntegrityCheckLayer = new OfficialNetworkIntegrityCheck();

		if (isServer)
		{
			ServerAPI.ServerInterface = new HttpServerInterface();
		}
		_bootstrapped = true;
	}

	public static void SetAuthToken(string joinToken)
	{
		JoinToken = joinToken;
		ClientConnector?.SetToken(joinToken);
		ServerListener?.SetToken(joinToken);
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
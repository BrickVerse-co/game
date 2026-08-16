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
	private static readonly object BootstrapLock = new();
	private static bool _bootstrapped;
	internal static string JoinToken { get; private set; } = string.Empty;
	internal static string CreatorToken { get; private set; } = string.Empty;
	internal static IClientConnector? ClientConnector { get; private set; }
	internal static IServerListener? ServerListener { get; private set; }

	public static void Initialize(bool isServer = false)
	{
		lock (BootstrapLock)
		{
			if (_bootstrapped)
			{
				return;
			}

			ClientConnector = new ClientConnector();
			ServerListener = new ServerListener();
			ClientConnector.SetToken(JoinToken);
			ServerListener.SetToken(JoinToken);
			NetworkService.IntegrityCheckLayer = new OfficialNetworkIntegrityCheck();

			if (isServer)
			{
				ServerAPI.ServerInterface = new HttpServerInterface();
				ServerAPI.ServerInterface.SetToken(ServerAPI.HostToken);
			}
			_bootstrapped = true;
		}
	}

	public static void SetAuthToken(string joinToken)
	{
		if (string.IsNullOrWhiteSpace(joinToken))
		{
			return;
		}

		JoinToken = joinToken;
		ClientConnector?.SetToken(joinToken);
		ServerListener?.SetToken(joinToken);
	}

	public static string GetAuthorizationHeaderValue()
	{
		if (string.IsNullOrWhiteSpace(JoinToken)) return string.Empty;
		return JoinToken.StartsWith("Bearer ", System.StringComparison.OrdinalIgnoreCase) ? JoinToken : "Bearer " + JoinToken;
	}

	public static void SetCreatorToken(string creatorToken)
	{
		if (string.IsNullOrWhiteSpace(creatorToken))
		{
			return;
		}

		CreatorToken = creatorToken;
	}

	public static Task<APIServerStatus> CheckServerStatus()
	{
		return ClientConnector?.CheckServerStatus()
			?? throw new MissingComponentException("Client Connector component missing");
	}

	public static Task<APIClientAuthResponseMessage> SendClientConnect()
	{
		return ClientConnector?.Connect()
			?? throw new MissingComponentException("Client Connector component missing");
	}

	public static Task<APIServerListenResponse> SendServerListen()
	{
		return ServerListener?.Listen()
			?? throw new MissingComponentException("Server listener component missing");
	}
}

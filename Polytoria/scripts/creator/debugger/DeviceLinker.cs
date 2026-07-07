// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Sockets;

namespace Polytoria.Creator;

public static class DeviceLinker
{
	public static string? GetLocalIP()
	{
		// Source - https://stackoverflow.com/questions/6803073/get-local-ip-address
		// Posted by Mr.Wang from Next Door, modified by community. See post 'Timeline' for change history
		// Retrieved 2025-11-21, License - CC BY-SA 3.0

		string localIP;
		using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, 0);
		socket.Connect("8.8.8.8", 65530);

		if (socket.LocalEndPoint is not IPEndPoint endPoint)
		{
			return null;
		}

		localIP = endPoint.Address.ToString();

		return localIP;
	}

	public static string? GetConnectAddress()
	{
		string? ip = GetLocalIP();
		if (ip == null) return null;
		return $"polytoria://test/{ip}";
	}
}

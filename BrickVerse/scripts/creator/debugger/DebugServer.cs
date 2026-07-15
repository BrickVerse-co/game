// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Schemas.Debugger;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace BrickVerse.Creator.Debugger;

public class DebugServer
{
	private static readonly TimeSpan WorldServerAllocTimeout = TimeSpan.FromSeconds(30);
	public int Port { get; private set; } = 24111;
	public bool ServerStarted { get; private set; } = false;
	private TcpListener _server = null!;

	private readonly List<TcpClient> _tcpClients = [];
	private readonly Dictionary<TcpClient, ClientData> _clientToData = [];
	private readonly Dictionary<string, TcpClient> _idToClient = [];

	private readonly Dictionary<string, TaskCompletionSource> _pendingServerInstance = [];

	public void Start()
	{
		if (ServerStarted) return;
		IPAddress localAddr = IPAddress.Parse("127.0.0.1");
		_server = new TcpListener(localAddr, Port);
		_server.Start();
		_ = Task.Run(ServerMainLoop);
		ServerStarted = true;

		BV.Print($"-- Debug server started at {localAddr}:{Port} --");
	}

	private async Task ServerMainLoop()
	{
		while (ServerStarted)
		{
			TcpClient client = await _server.AcceptTcpClientAsync();
			BV.Print("Debug client connected");
			_ = HandleClient(client);
		}
	}

	private async Task HandleClient(TcpClient client)
	{
		_tcpClients.Add(client);
		try
		{
			NetworkStream stream = client.GetStream();
			byte[] buffer = new byte[1024];

			while (ServerStarted)
			{
				int bytesRead = await stream.ReadAsync(buffer);

				if (bytesRead == 0)
				{
					break; // Client disconnected gracefully
				}

				IDebugMessage? msg = SerializeUtils.Deserialize<IDebugMessage>(buffer);
				if (msg != null)
				{
					try
					{
						await OnMessageRecv(client, msg);
					}
					catch (Exception ex)
					{
						BV.PrintErr(ex);
					}
				}
			}
		}
		finally
		{
			client.Close();
			BV.Print("Debug client disconnected");
			if (_clientToData.Remove(client, out var data))
			{
				// Cleanup local test process
				CreatorService.Singleton.LocalTestProcesses.Remove(data.ProcessID);
			}
			_tcpClients.Remove(client);
			foreach ((string id, TcpClient c) in _idToClient)
			{
				if (c == client)
				{
					_idToClient.Remove(id);
					break;
				}
			}
		}
	}

	private async Task OnMessageRecv(TcpClient from, IDebugMessage msg)
	{
		if (msg is MessageClientData data)
		{
			_clientToData.Add(from, new()
			{
				DebugID = data.DebugID,
				ProcessID = data.ProcessID,
			});

			if (data.ProcessID != 0)
			{
				CreatorService.Singleton.LocalTestProcesses.Add(data.ProcessID);
			}
		}
		else if (msg is MessageLogDispatch log)
		{
			BV.DispatchLog(new() { Content = log.Content, LogFrom = log.LogFrom, LogType = log.LogType });
		}
		else if (msg is MessageNewServerRequest req)
		{
			if (_clientToData.TryGetValue(from, out ClientData cdata))
			{
				CreatorSession session = CreatorService.LocalTestIDToSession[cdata.DebugID];
				BV.Print("Server start request: ", req.WorldPath);
				string worldPath = req.WorldPath;
				string originPlacePath = worldPath;

				// Fix .bvxw or .bvworld extension
				if (!worldPath.EndsWith(".bvxw") && !worldPath.EndsWith(".bvworld")) worldPath += ".bvxw";

				// call on main thread
				BV.CallOnMainThread(async () =>
				{
					try
					{
						int port = GD.RandRange(20000, 30000);

						TaskCompletionSource tcs = new();

						_pendingServerInstance.Add(cdata.DebugID, tcs);

						await CreatorService.Singleton.StartLocalTestOnEntry(session.ProjectFolderPath, worldPath, cdata.DebugID, port, true);

						BV.Print($"Awaiting server start.. ({worldPath})");
						await tcs.Task.WaitAsync(WorldServerAllocTimeout);
						BV.Print("New server started!");

						SendMessage(from, new MessageNewServerResponse() { WorldPath = originPlacePath, Address = "127.0.0.1", Port = port, DebugID = cdata.DebugID });
					}
					catch (Exception ex)
					{
						OS.Alert(ex.Message);
					}
				});
			}
			else
			{
				BV.PrintErr("World join failure: no client data");
			}
		}
		else if (msg is MessageServerReady serverReady)
		{
			if (_clientToData.TryGetValue(from, out ClientData cdata))
			{
				if (_pendingServerInstance.TryGetValue(cdata.DebugID, out TaskCompletionSource? tcs))
				{
					BV.Print("Server start resolved");
					tcs.SetResult();
				}
			}
		}
	}

	public async void BroadcastMessage(IDebugMessage msg)
	{
		byte[] data = SerializeUtils.Serialize(msg);
		foreach (TcpClient client in _tcpClients)
		{
			NetworkStream stream = client.GetStream();
			await stream.WriteAsync(data);
		}
	}

	private static async void SendMessage(TcpClient client, IDebugMessage msg)
	{
		byte[] data = SerializeUtils.Serialize(msg);
		NetworkStream stream = client.GetStream();
		await stream.WriteAsync(data);
	}

	public void SendTerminateProgram()
	{
		BroadcastMessage(new MessageShutdown());
	}

	private struct ClientData
	{
		public string DebugID;
		public int ProcessID;
	}
}

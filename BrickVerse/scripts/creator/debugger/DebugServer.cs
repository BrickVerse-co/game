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
using System.IO;
using System.Threading;
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
	private readonly Dictionary<TcpClient, SemaphoreSlim> _clientSendLocks = [];
	private readonly Dictionary<string, TcpClient> _idToClient = [];

	private readonly Dictionary<string, TaskCompletionSource> _pendingServerInstance = [];

	public event Action<int, bool>? RuntimeConnected;
	public event Action<int>? RuntimeDisconnected;
	public event Action<int, MessageRuntimeSnapshot>? RuntimeSnapshotReceived;
	public event Action<int, MessageRuntimeDiagnostics>? RuntimeDiagnosticsReceived;
	public event Action<int, MessageLogDispatch>? RuntimeLogReceived;

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
		_clientSendLocks[client] = new SemaphoreSlim(1, 1);
		try
		{
			NetworkStream stream = client.GetStream();
			while (ServerStarted)
			{
				byte[]? buffer = await ReadFrameAsync(stream);
				if (buffer == null) break;

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
				BV.CallOnMainThread(() => RuntimeDisconnected?.Invoke(data.ProcessID));
			}
			_tcpClients.Remove(client);
			if (_clientSendLocks.Remove(client, out SemaphoreSlim? sendLock)) sendLock.Dispose();
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
				IsServer = data.IsServer,
			});

			if (data.ProcessID != 0)
			{
				CreatorService.Singleton.LocalTestProcesses.Add(data.ProcessID);
			}
			BV.CallOnMainThread(() => RuntimeConnected?.Invoke(data.ProcessID, data.IsServer));
		}
		else if (msg is MessageLogDispatch log)
		{
			BV.DispatchLog(new() { Content = log.Content, Source = log.Source, LogFrom = log.LogFrom, LogType = log.LogType });
			if (_clientToData.TryGetValue(from, out ClientData logClient))
			{
				BV.CallOnMainThread(() => RuntimeLogReceived?.Invoke(logClient.ProcessID, log));
			}
		}
		else if (msg is MessageRuntimeSnapshot snapshot)
		{
			if (_clientToData.TryGetValue(from, out ClientData runtime))
			{
				BV.CallOnMainThread(() => RuntimeSnapshotReceived?.Invoke(runtime.ProcessID, snapshot));
			}
		}
		else if (msg is MessageRuntimeDiagnostics diagnostics)
		{
			if (_clientToData.TryGetValue(from, out ClientData runtime))
				BV.CallOnMainThread(() => RuntimeDiagnosticsReceived?.Invoke(runtime.ProcessID, diagnostics));
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

	public void BroadcastMessage(IDebugMessage msg)
	{
		foreach (TcpClient client in _tcpClients)
		{
			SendMessage(client, msg);
		}
	}

	private async void SendMessage(TcpClient client, IDebugMessage msg)
	{
		byte[] data = SerializeUtils.Serialize(msg);
		NetworkStream stream = client.GetStream();
		if (!_clientSendLocks.TryGetValue(client, out SemaphoreSlim? sendLock)) return;
		await sendLock.WaitAsync();
		try
		{
			await WriteFrameAsync(stream, data);
		}
		finally
		{
			sendLock.Release();
		}
	}

	public void RequestRuntimeSnapshot(int processId) =>
		SendToProcess(processId, new MessageRuntimeSnapshotRequest());

	public void RequestRuntimeDiagnostics(int processId) =>
		SendToProcess(processId, new MessageRuntimeDiagnosticsRequest());

	public void SetRuntimeProperty(int processId, string objectId, string propertyName, string value) =>
		SendToProcess(processId, new MessageRuntimePropertySet
		{
			ObjectID = objectId,
			PropertyName = propertyName,
			Value = value
		});

	public void ExecuteRuntimeLuau(int processId, string source) =>
		SendToProcess(processId, new MessageRuntimeExecute { Source = source });

	public void RenameRuntimeObject(int processId, string objectId, string name) =>
		SendToProcess(processId, new MessageRuntimeRename { ObjectID = objectId, Name = name });

	public void SetRuntimeViewportRect(int processId, Rect2I rect, bool visible) =>
		SendToProcess(processId, new MessageRuntimeViewportRect
		{
			X = rect.Position.X,
			Y = rect.Position.Y,
			Width = rect.Size.X,
			Height = rect.Size.Y,
			Visible = visible
		});

	public void SetRuntimeDeviceEmulation(int processId, MessageRuntimeDeviceEmulation state) =>
		SendToProcess(processId, state);

	private void SendToProcess(int processId, IDebugMessage message)
	{
		foreach ((TcpClient client, ClientData data) in _clientToData)
		{
			if (data.ProcessID == processId)
			{
				SendMessage(client, message);
				return;
			}
		}
	}

	private static async Task<byte[]?> ReadFrameAsync(NetworkStream stream)
	{
		byte[] lengthBytes = new byte[sizeof(int)];
		if (!await ReadExactlyAsync(stream, lengthBytes)) return null;
		int length = BitConverter.ToInt32(lengthBytes);
		if (length <= 0 || length > 64 * 1024 * 1024) throw new InvalidDataException("Invalid debugger frame length.");
		byte[] payload = new byte[length];
		return await ReadExactlyAsync(stream, payload) ? payload : null;
	}

	private static async Task<bool> ReadExactlyAsync(NetworkStream stream, byte[] buffer)
	{
		int offset = 0;
		while (offset < buffer.Length)
		{
			int read = await stream.ReadAsync(buffer.AsMemory(offset));
			if (read == 0) return false;
			offset += read;
		}
		return true;
	}

	private static async Task WriteFrameAsync(NetworkStream stream, byte[] payload)
	{
		await stream.WriteAsync(BitConverter.GetBytes(payload.Length));
		await stream.WriteAsync(payload);
	}

	public void SendTerminateProgram()
	{
		BroadcastMessage(new MessageShutdown());
	}

	private struct ClientData
	{
		public string DebugID;
		public int ProcessID;
		public bool IsServer;
	}
}

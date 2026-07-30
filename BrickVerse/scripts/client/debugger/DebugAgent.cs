// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Attributes;
using BrickVerse.Networking.Synchronizers;
using BrickVerse.Schemas.Debugger;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BrickVerse.Client.Debugger;

public class DebugAgent
{
	public bool ClientStarted { get; private set; } = false;
	private TcpClient _client = null!;
	private NetworkStream _stream = null!;
	private readonly List<KeyValuePair<string, TaskCompletionSource<MessageNewServerResponse>>> _pendingServerInstance = [];
	private readonly SemaphoreSlim _sendLock = new(1, 1);

	private string _address = "";

	public async Task Start(string addresss, int port, string? debugID = null)
	{
		if (ClientStarted) return;

		_address = addresss;
		_client = new TcpClient();

		await _client.ConnectAsync(addresss, port);

		_stream = _client.GetStream();

		_ = ReceiveMessages();

		ClientStarted = true;

		// Init messages
		int procId = Globals.IsMobileBuild ? 0 : OS.GetProcessId();

		if (debugID != null)
		{
			BV.Print("Reporting debug ID: ", debugID);
			await SendMessage(new MessageClientData() { DebugID = debugID, ProcessID = procId, IsServer = BV.IsServer });
		}
		else
		{
			BV.Print("No debug ID attached");
			await SendMessage(new MessageClientData() { ProcessID = procId, IsServer = BV.IsServer });
		}

		BV.PrintV($"-- Connected to debug server --");
	}

	private async Task ReceiveMessages()
	{
		while (true)
		{
			if (!_client.Connected) { ClientStarted = false; break; }
			try
			{
				byte[]? buffer = await ReadFrameAsync(_stream);
				if (buffer == null) break;

				IDebugMessage? msg = SerializeUtils.Deserialize<IDebugMessage>(buffer);
				if (msg != null)
				{
					OnMessageRecv(msg);
				}
			}
			catch (Exception e)
			{
				BV.PrintErrV(e);
				BV.PrintErrV($"Receive error: {e.Message}");
			}
		}
	}

	private void OnMessageRecv(IDebugMessage msg)
	{
		if (msg is MessageShutdown)
		{
			if (!Globals.IsMobileBuild)
			{
				Globals.Singleton.Quit();
			}
		}
		else if (Globals.IsMobileBuild)
		{
			if (msg is MessageLaunchWorld)
			{
				Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
				if (app is ClientEntry ce)
				{
					ClientEntry.ClientEntryData entryData = new()
					{
						ConnectAddress = _address,
						TestIsServer = false,
						TestUserID = Globals.TestUserIdStart,
					};
					ce.Entry(entryData);
				}
			}
		}
		else if (msg is MessageNewServerResponse ns)
		{
			foreach (var pair in _pendingServerInstance.ToArray())
			{
				if (pair.Key == ns.WorldPath)
				{
					pair.Value.SetResult(ns);
					_pendingServerInstance.Remove(pair);
				}
			}
		}
		else if (msg is MessageObjPropChange pc)
		{
			if (World.Current == null) return;
			if (!World.Current.Network.IsServer) return;
			NetworkedObject? obj = World.Current.GetObjectFromID(pc.ObjectID);
			if (obj != null)
			{
				PropertyInfo? prop = obj.GetSyncProperty(pc.PropertyName);
				if (prop != null)
				{
					object? val = NetworkPropSync.DeserializePropValue(pc.PropertyValue, prop.PropertyType);

					// Call in main thread
					BV.CallOnMainThread(() =>
					{
						prop.SetValue(obj, val);
					});
				}
			}
		}
		else if (msg is MessageRuntimeSnapshotRequest)
		{
			BV.CallOnMainThread(async () => await SendRuntimeSnapshot());
		}
		else if (msg is MessageRuntimePropertySet propertySet)
		{
			BV.CallOnMainThread(() => SetRuntimeProperty(propertySet));
		}
		else if (msg is MessageRuntimeExecute execute)
		{
			BV.CallOnMainThread(() => ExecuteRuntimeLuau(execute.Source));
		}
		else if (msg is MessageRuntimeRename rename)
		{
			BV.CallOnMainThread(() =>
			{
				NetworkedObject? target = World.Current?.GetObjectFromID(rename.ObjectID);
				if (target != null && !string.IsNullOrWhiteSpace(rename.Name))
					target.Name = rename.Name.Trim();
			});
		}
		else if (msg is MessageRuntimeViewportRect viewportRect)
		{
			BV.CallOnMainThread(() => ClientEntry.ApplyLocalTestViewport(viewportRect));
		}
	}

	public async Task SendMessage(IDebugMessage msg)
	{
		if (!ClientStarted) return;
		byte[] data = SerializeUtils.Serialize(msg);
		try
		{
			await _sendLock.WaitAsync();
			try
			{
				await WriteFrameAsync(_stream, data);
			}
			finally
			{
				_sendLock.Release();
			}
		}
		catch (Exception ex)
		{
			BV.PrintErrV(ex.Message);
		}
	}

	private async Task SendRuntimeSnapshot()
	{
		World? world = World.Current;
		if (world == null) return;

		Instance[] instances = [world, .. world.GetDescendants()];
		RuntimeObjectInfo[] objects = instances.Select(instance => new RuntimeObjectInfo
		{
			ObjectID = instance.ObjectID,
			ParentObjectID = instance.Parent?.ObjectID ?? "",
			Name = instance.Name,
			ClassName = instance.ClassName,
			Properties = instance.GetType()
				.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(property => property.IsDefined(typeof(EditableAttribute), true) && property.CanRead)
				.Select(property => new RuntimePropertyInfo
				{
					Name = property.Name,
					TypeName = property.PropertyType.AssemblyQualifiedName ?? property.PropertyType.FullName ?? property.PropertyType.Name,
					Value = Convert.ToString(property.GetValue(instance), CultureInfo.InvariantCulture) ?? "",
					CanWrite = property.CanWrite
				})
				.ToArray()
		}).ToArray();

		await SendMessage(new MessageRuntimeSnapshot { Objects = objects });
	}

	private static void SetRuntimeProperty(MessageRuntimePropertySet change)
	{
		NetworkedObject? target = World.Current?.GetObjectFromID(change.ObjectID);
		PropertyInfo? property = target?.GetType().GetProperty(change.PropertyName, BindingFlags.Public | BindingFlags.Instance);
		if (target == null || property == null || !property.CanWrite || !property.IsDefined(typeof(EditableAttribute), true))
			return;

		try
		{
			object? value = ParseRuntimeValue(change.Value, property.PropertyType);
			property.SetValue(target, value);
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Runtime property edit failed: {ex.Message}");
		}
	}

	private static object? ParseRuntimeValue(string raw, Type type)
	{
		Type targetType = Nullable.GetUnderlyingType(type) ?? type;
		if (targetType == typeof(string)) return raw;
		if (targetType.IsEnum) return Enum.Parse(targetType, raw, true);

		string[] components = raw.Trim('(', ')', '[', ']').Split(',', StringSplitOptions.TrimEntries);
		if (targetType == typeof(Vector2) && components.Length == 2)
			return new Vector2(ParseFloat(components[0]), ParseFloat(components[1]));
		if (targetType == typeof(Vector3) && components.Length == 3)
			return new Vector3(ParseFloat(components[0]), ParseFloat(components[1]), ParseFloat(components[2]));
		if (targetType == typeof(Color) && (components.Length == 3 || components.Length == 4))
			return new Color(
				ParseFloat(components[0]),
				ParseFloat(components[1]),
				ParseFloat(components[2]),
				components.Length == 4 ? ParseFloat(components[3]) : 1
			);

		return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
	}

	private static float ParseFloat(string value) =>
		float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

	private static void ExecuteRuntimeLuau(string source)
	{
		World? world = World.Current;
		if (world == null || string.IsNullOrWhiteSpace(source)) return;

		Datamodel.Script script = world.Network.IsServer
			? world.New<ServerScript>(world.ScriptService)
			: world.New<ClientScript>(world.ScriptService);
		script.Name = "RuntimeConsoleExecutor";
		script.Source = source;
		script.PermissionFlags = ScriptPermissionFlags.CreatorAccess | ScriptPermissionFlags.ContextAccess;
		script.Run();
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

	public async Task SendServerReady()
	{
		await SendMessage(new MessageServerReady());
	}

	public async Task SendLogDispatch(LogDispatcher.LogData data)
	{
		await SendMessage(new MessageLogDispatch()
		{
			LogType = data.LogType,
			LogFrom = data.LogFrom,
			Content = data.Content
		});
	}

	public async Task<MessageNewServerResponse> CreateServerInstance(string toPath)
	{
		TaskCompletionSource<MessageNewServerResponse> restsk = new();
		_pendingServerInstance.Add(new(toPath, restsk));
		await SendMessage(new MessageNewServerRequest() { WorldPath = toPath });
		return await restsk.Task;
	}
}

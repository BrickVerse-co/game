// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using MemoryPack;
using BrickVerse.Attributes;
using BrickVerse.Client.WebAPI;
using BrickVerse.Client.WebAPI.Interfaces;
#if CREATOR
using BrickVerse.Creator.UI;
#endif
using BrickVerse.Datamodel;
using BrickVerse.Networking;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Collections.Generic;
using System.Text;

namespace BrickVerse.Scripting;

[Internal, NoSync]
public partial class LogDispatcher : NetworkedObject
{
	private const int MaxLogLength = 16384;
	private const int ClientForwardLimitCount = 12;
	private const long ClientForwardLimitWindowMs = 60000;
	private const int ServerForwardLimitCount = 12;
	private const long ServerForwardLimitWindowMs = 60000;
	private const int MaxForwardedContentLength = 1000;
	public event Action<LogData>? NewLog;
	public event Action<LogData[]>? LogSynchronized;
	public List<LogData> ServerLogs = [];
	public List<LogData> Logs = [];
	private readonly Queue<long> _clientForwardWindow = [];
	private int _clientForwardDroppedCount;
	private readonly Dictionary<int, Queue<long>> _serverForwardWindows = [];
	private readonly Dictionary<int, int> _serverForwardDroppedCounts = [];

	public override void Init()
	{
		base.Init();
		if (Root != null)
		{
			if (Root.IsLoaded)
			{
				OnGameReady();
			}
			else
			{
				Root.Loaded.Once(OnGameReady);
			}
		}
	}

	private void OnGameReady()
	{
		if (!Root.Network.IsServer && Root.SessionType != World.SessionTypeEnum.Creator)
		{
			RpcId(1, nameof(NetReqServerLogs));
		}
	}

	public void LogWarning(Datamodel.Script from, string content)
	{
		BV.PrintV($"[Lua] {from.NetworkPath} {content}");
		DispatchLog(new()
		{
			ID = Guid.NewGuid().ToString(),
			LogType = LogTypeEnum.Warning,
			Content = content,
			LogFrom = (from is ClientScript) ? LogFromEnum.Client : LogFromEnum.Server
		});
	}

	public void LogInfo(Datamodel.Script from, string content)
	{
		BV.PrintV($"[Lua] {from.NetworkPath} {content}");
		DispatchLog(new()
		{
			ID = Guid.NewGuid().ToString(),
			LogType = LogTypeEnum.Info,
			Content = content,
			LogFrom = (from is ClientScript) ? LogFromEnum.Client : LogFromEnum.Server
		});
	}

	public void LogError(Datamodel.Script from, string content)
	{
		BV.PrintErrV($"[Lua] {from.NetworkPath} {content}");
		DispatchLog(new()
		{
			ID = Guid.NewGuid().ToString(),
			LogType = LogTypeEnum.Error,
			Content = content,
			LogFrom = (from is ClientScript) ? LogFromEnum.Client : LogFromEnum.Server
		});
	}

	internal async void DispatchLog(LogData data, bool preserveSource = false)
	{
		if (!preserveSource && Root.Network.IsServer && Root.SessionType == World.SessionTypeEnum.Client)
		{
			// Explicitly set on server if is client/ from server
			data.LogFrom = LogFromEnum.Server;
		}
		data.LoggedAt = DateTime.UtcNow;
		BV.CallOnMainThread(() =>
		{
			InvokeNewLog(data);
			if (Root.Network.IsServer)
			{
				foreach (Player plr in Root.Players.GetPlayers())
				{
					// If is creator or in beta program (beta gets the logs), or is solo test
					if (plr.IsCreator || plr.IsAdmin || Globals.IsBetaBuild || (Root.Entry != null && Root.Entry.IsSoloTest))
					{
						RpcId(plr.PeerID, nameof(NetRecvLog), SerializeUtils.Serialize(data));
					}
				}
			}
			// TODO: Turn this into an event instead? Maybe dispatch it to BV
#if CREATOR
			DebugConsole.Singleton?.NewLog(data);
#endif
		});
		if (Root.Entry?.DebugAgent != null)
		{
			await Root.Entry.DebugAgent.SendLogDispatch(data);
		}

		TryForwardClientLogToServer(data);

		if (Root.Network.IsServer && Root.Network.IsProd)
		{
			ServerLogSource source = data.LogFrom == LogFromEnum.Client ? ServerLogSource.Client : ServerLogSource.Server;
			ServerLogLevel level = data.LogType switch
			{
				LogTypeEnum.Warning => ServerLogLevel.Warning,
				LogTypeEnum.Error => ServerLogLevel.Error,
				_ => ServerLogLevel.Info
			};

			long timestampUnixMs = new DateTimeOffset(data.LoggedAt).ToUnixTimeMilliseconds();
			_ = ServerAPI.LogServerLog(data.Content, source, level, timestampUnixMs);
		}
	}

	private void TryForwardClientLogToServer(LogData data)
	{
		if (Root.Network.IsServer || Root.SessionType != World.SessionTypeEnum.Client)
		{
			return;
		}

		if (!CanForwardClientLog())
		{
			return;
		}

		LogData payload = new()
		{
			ID = data.ID,
			LogType = data.LogType,
			LogFrom = LogFromEnum.Client,
			Content = TruncateForForward(data.Content),
			LoggedAt = data.LoggedAt
		};

		RpcId(1, nameof(NetReqClientLog), SerializeUtils.Serialize(payload));
	}

	private bool CanForwardClientLog()
	{
		long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		while (_clientForwardWindow.Count > 0 && nowMs - _clientForwardWindow.Peek() > ClientForwardLimitWindowMs)
		{
			_clientForwardWindow.Dequeue();
		}

		if (_clientForwardWindow.Count >= ClientForwardLimitCount)
		{
			_clientForwardDroppedCount++;
			if (_clientForwardDroppedCount == 1 || _clientForwardDroppedCount % 10 == 0)
			{
				BV.PrintWarn($"[ClientLogForward] Rate limited. Dropped {_clientForwardDroppedCount} client log(s).");
			}

			return false;
		}

		if (_clientForwardDroppedCount > 0)
		{
			BV.Print($"[ClientLogForward] Recovered after dropping {_clientForwardDroppedCount} client log(s).");
			_clientForwardDroppedCount = 0;
		}

		_clientForwardWindow.Enqueue(nowMs);
		return true;
	}

	private static string TruncateForForward(string content)
	{
		if (content.Length <= MaxForwardedContentLength)
		{
			return content;
		}

		StringBuilder sb = new(MaxForwardedContentLength + 32);
		sb.Append(content.AsSpan(0, MaxForwardedContentLength));
		sb.Append("...[truncated]");
		return sb.ToString();
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetReqClientLog(byte[] rawdata)
	{
		if (!Root.Network.IsServer)
		{
			return;
		}

		if (RemoteSenderId <= 1)
		{
			return;
		}

		if (!CanAcceptClientLogFromPeer(RemoteSenderId))
		{
			return;
		}

		LogData? data = SerializeUtils.Deserialize<LogData>(rawdata);
		if (data == null)
		{
			return;
		}

		data.ID = Guid.NewGuid().ToString();
		data.LogFrom = LogFromEnum.Client;
		data.Content = TruncateForForward(data.Content ?? string.Empty);
		DispatchLog(data, preserveSource: true);
	}

	private bool CanAcceptClientLogFromPeer(int peerID)
	{
		long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		if (!_serverForwardWindows.TryGetValue(peerID, out Queue<long>? window))
		{
			window = [];
			_serverForwardWindows[peerID] = window;
		}

		while (window.Count > 0 && nowMs - window.Peek() > ServerForwardLimitWindowMs)
		{
			window.Dequeue();
		}

		if (window.Count >= ServerForwardLimitCount)
		{
			int dropped = 1;
			if (_serverForwardDroppedCounts.TryGetValue(peerID, out int currentDropped))
			{
				dropped = currentDropped + 1;
			}

			_serverForwardDroppedCounts[peerID] = dropped;
			if (dropped == 1 || dropped % 10 == 0)
			{
				BV.PrintWarn($"[ClientLogForward] Server rate limit exceeded for peer {peerID}. Dropped {dropped} log(s).");
			}

			return false;
		}

		window.Enqueue(nowMs);
		if (_serverForwardDroppedCounts.Remove(peerID, out int recoveredDropped) && recoveredDropped > 0)
		{
			BV.Print($"[ClientLogForward] Server limiter recovered for peer {peerID} after dropping {recoveredDropped} log(s).");
		}

		return true;
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvLog(byte[] rawdata)
	{
		LogData? data = SerializeUtils.Deserialize<LogData>(rawdata);
		if (data != null)
		{
			InvokeNewLog(data);
		}
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetReqServerLogs()
	{
		RpcId(RemoteSenderId, nameof(NetRecvServerLogs), SerializeUtils.Serialize(Logs.ToArray()));
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvServerLogs(byte[] rawdata)
	{
		LogData[]? data = SerializeUtils.Deserialize<LogData[]>(rawdata);
		if (data != null)
		{
			foreach (LogData item in data)
			{
				RegisterLogItem(item);
			}

			LogSynchronized?.Invoke([.. Logs]);
		}
	}

	private void RegisterLogItem(LogData item)
	{
		// Clear loggedAt data if from server and receiver is client (time from sserver may be desynchronized with the client)
		if (item.LogFrom == LogFromEnum.Server && Root.SessionType == World.SessionTypeEnum.Client && Root.Network.IsProd)
		{
			item.LoggedAt = DateTime.UtcNow;
		}
		Logs.Add(item);
		if (Logs.Count > MaxLogLength)
		{
			Logs.RemoveAt(0);
		}
		if (ServerLogs.Count > MaxLogLength)
		{
			ServerLogs.RemoveAt(0);
		}
	}

	private void InvokeNewLog(LogData item)
	{
		RegisterLogItem(item);
		NewLog?.Invoke(item);
	}

	[MemoryPackable]
	public partial class LogData
	{
		public LogTypeEnum LogType;
		public LogFromEnum LogFrom = LogFromEnum.None;
		public string ID = "";
		public string Content = "";
		public DateTime LoggedAt;

		public override int GetHashCode()
		{
			return ID.GetHashCode();
		}
	}

	public enum LogTypeEnum
	{
		Info,
		Error,
		Warning
	}

	public enum LogFromEnum
	{
		None,
		Client,
		Server,
		Addon
	}
}

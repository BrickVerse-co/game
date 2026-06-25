// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using BrickVerse.Attributes;
using BrickVerse.Client;
using BrickVerse.Client.Networking;
using BrickVerse.Client.WebAPI;
using BrickVerse.Client.WebAPI.Interfaces;
using BrickVerse.Datamodel.Data;
using BrickVerse.Networking;
using BrickVerse.Networking.Interfaces;
using BrickVerse.Networking.RateLimiters;
using BrickVerse.Networking.Synchronizers;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Utils;
using BrickVerse.Utils.DTOs;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;


namespace BrickVerse.Datamodel.Services;

[ExplorerExclude, SaveIgnore, Internal]
public sealed partial class NetworkService : Instance
{
	private static readonly ConcurrentDictionary<(Type Type, int MethodId), RpcDispatchInfo> _rpcDispatchCache = new();

	private const int PingIntervalMs = 250; // Ping interval
	private const int AuthWaitTimeoutSec = 15; // Time wait before user disconnects due to auth timeout
	private const int HeartbeatIntervalSec = 10; // Server heartbeat interval
	private const int HeartbeatBeforeCheckPlayers = 5; // Server wait before checking players
	private const float ConnectTimeoutSec = 90; // Timeout kick if player is not ready

	// TODO: Narrow this down
	private const int MaxBroadcastPacketPerSec = 100;

	public const string TerminationMessage = "Your BrickVerse account has been terminated, please visit the website for more info.";
	public const string AuthFailureMessage = "Authentication failure, Please try again.";
	public const string IntegrityFailureMessage = "Integrity failure, please update your client.";
	public const string NetworkModeMismatchMessage = "Network mode mismatch.";
	public const string MultipleDeviceMessage = "Another device is already playing using this account, please leave the game first then try again.";
	public const string ConnectTimeoutMessage = "Connection timeout";

	private static readonly Dictionary<NetworkInstance.NetInstanceErrorEnum, string> _netInstanceErrorMessages = new()
	{
		{ NetworkInstance.NetInstanceErrorEnum.DataChannelConnectFailure, "Couldn't connect to the data channel, please try rejoining" }
	};

	private const ENetConnection.CompressionMode CompressionMode = ENetConnection.CompressionMode.Fastlz;

	public event Action? ServerStarted;
	public event Action? ClientWorldReady;
	public event Action? ClientReady;
	public event Action? ClientConnectedToServer;
	public event Action<string, DisconnectionCodeEnum>? ClientDisconnected;
	public event Action<int>? PeerPreInit;

	public NetworkTransformSync TransformSync { get; private set; } = new();
	public NetworkPropSync PropSync { get; private set; } = new();
	public NetworkReplicateSync ReplicateSync { get; private set; } = new();
	public NetworkScriptSync ScriptSync { get; private set; } = new();

	public NetworkInstance? NetInstance = null;
	public bool IsServer = false;
	public bool IsDisconnected = false;
	public bool IsShuttingDown = false;
	public bool ClientConnected = false;


	// Rate limiters
	private readonly Dictionary<int, RateLimiters> _peerRateLimiters = [];
	private readonly Lock _rateLimiterLock = new();

	private Players _players = null!;

	// List of active peer ids
	public List<int> ActivePeerIDs = [];

	public int LocalPeerID = 0;
	public bool IsReplicationDone = false;
	public bool IsPlaceReplicationDone = false;
	public bool IsTransformReplicateDone = false;
	public bool IsClientScriptReplicateDone = false;
	// Is production
	public bool IsProd = false;
	public NetworkModeEnum NetworkMode { get; set; } = NetworkModeEnum.Client;

	private ulong placeReplicationStartTime = 0;
	private readonly Dictionary<string, List<NetReplicateData>> _pendingReplications = [];
	private Godot.Timer _heartbeatTimer = null!;
	private ulong _heartbeatCount = 0;
	public ClientEntry Entry = null!;

	/// <summary>
	/// Integrity check layer for this network
	/// </summary>
	internal static IIntegrityCheck? IntegrityCheckLayer { get; set; }

	public void Attach(World game)
	{
		game.Network = this;
		Root = game;
		TransformSync.Name = "TransformSync";
		PropSync.Name = "PropSync";
		ReplicateSync.Name = "ReplicateSync";
		ScriptSync.Name = "ScriptSync";

		TransformSync.NetworkParent = this;
		PropSync.NetworkParent = this;
		ReplicateSync.NetworkParent = this;
		ScriptSync.NetworkParent = this;

		TransformSync.NetService = this;
		PropSync.NetService = this;
		ReplicateSync.NetService = this;
		ScriptSync.NetService = this;

		TransformSync.Root = game;
		PropSync.Root = game;
		ReplicateSync.Root = game;
		ScriptSync.Root = game;

		Globals.BeforeQuit += BeforeQuitHandler;
	}

	public override void PreDelete()
	{
		Globals.BeforeQuit -= BeforeQuitHandler;

		// Null all events
		ServerStarted = null;
		ClientWorldReady = null;
		ClientReady = null;
		ClientConnectedToServer = null;
		ClientDisconnected = null;
		PeerPreInit = null;

		Root.Network = null!;

		base.PreDelete();
	}

	private void BeforeQuitHandler()
	{
		if (ClientConnected)
		{
			DisconnectSelf();
		}
	}

	private void InitNodes()
	{
		_players = Root.FindChild<Players>("Players")!;
	}

	private void SetupNetwork()
	{
		Root.ListenToNetworkService();
	}

	private void SetupPeer()
	{
		NetInstance?.MessageReceived += OnMessageRecv;
	}

	private static RpcDispatchInfo GetDispatchInfo(NetworkedObject target, int methodId)
	{
		Type targetType = target.GetType();

		return _rpcDispatchCache.GetOrAdd((targetType, methodId), (key) =>
		{
			MethodInfo method = target.GetRpcMethod(methodId);
			NetRpcAttribute attribute = method.GetCustomAttribute<NetRpcAttribute>() ?? throw new NetworkException($"Tried to call Rpc function which is not marked as Rpc ({method.Name})");
			Type[] parameterTypes = [.. method.GetParameters().Select(p => p.ParameterType)];

			Action<NetworkedObject, object?[]?> invoker = CreateInvoker(method);

			return new RpcDispatchInfo()
			{
				Method = method,
				Attribute = attribute,
				ParameterTypes = parameterTypes,
				Invoke = invoker
			};
		});
	}

	private static Action<NetworkedObject, object?[]?> CreateInvoker(MethodInfo method)
	{
		ParameterExpression targetParam = System.Linq.Expressions.Expression.Parameter(typeof(NetworkedObject), "target");
		ParameterExpression argsParam = System.Linq.Expressions.Expression.Parameter(typeof(object[]), "args");

		Type declaringType = method.DeclaringType ?? throw new InvalidOperationException("Method has no declaring type");
		UnaryExpression castTarget = System.Linq.Expressions.Expression.Convert(targetParam, declaringType);

		ParameterInfo[] parameters = method.GetParameters();
		System.Linq.Expressions.Expression[] callArgs = new System.Linq.Expressions.Expression[parameters.Length];

		for (int i = 0; i < parameters.Length; i++)
		{
			var indexExpr = System.Linq.Expressions.Expression.ArrayIndex(argsParam, System.Linq.Expressions.Expression.Constant(i));
			callArgs[i] = System.Linq.Expressions.Expression.Convert(indexExpr, parameters[i].ParameterType);
		}

		MethodCallExpression call = System.Linq.Expressions.Expression.Call(castTarget, method, callArgs);
		System.Linq.Expressions.Expression body = method.ReturnType == typeof(void) ? call : System.Linq.Expressions.Expression.Block(call, System.Linq.Expressions.Expression.Empty());

		return System.Linq.Expressions.Expression.Lambda<Action<NetworkedObject, object?[]?>>(body, targetParam, argsParam).Compile();
	}

	private static void ValidateAuthority(NetRpcAttribute rpcAttr, MethodInfo md, NetworkedObject netObj, int originFromPeer, TransferMode tfm, InternalNetMsg netMsg)
	{
		if (rpcAttr.TransferMode == TransferMode.Reliable && tfm != TransferMode.Reliable)
		{
			throw new NetworkException(
				$"Flag mismatch. Method={md.Name}, Expected={rpcAttr.TransferMode}, Actual={tfm}, " +
				$"OriginPeer={originFromPeer}, Target={netMsg.Target}"
			);
		}

		if (rpcAttr.AuthorMode == AuthorityMode.Server)
		{
			if (originFromPeer != 1)
			{
				throw new NetworkException(
					$"Invalid authority. Method={md.Name}, Required=Server, OriginPeer={originFromPeer}, " +
					$"Target={netMsg.Target}, BroadcastAll={netMsg.BroadcastAll}, TransferMode={tfm}"
				);
			}

			return;
		}

		if (rpcAttr.AuthorMode == AuthorityMode.Authority)
		{
			if (originFromPeer != 1 && originFromPeer != netObj.NetworkAuthority)
			{
				throw new NetworkException(
					$"Invalid authority. Method={md.Name}, RequiredAuthority={netObj.NetworkAuthority}, " +
					$"OriginPeer={originFromPeer}, Target={netMsg.Target}, BroadcastAll={netMsg.BroadcastAll}"
				);
			}

			return;
		}

		if (rpcAttr.AuthorMode == AuthorityMode.Any)
		{
			if (originFromPeer != 1 && netMsg.BroadcastAll && rpcAttr.AllowToServerOnly)
			{
				throw new NetworkException(
					$"Broadcast to server only rule violation. Method={md.Name}, OriginPeer={originFromPeer}, " +
					$"Target={netMsg.Target}, TransferMode={tfm}"
				);
			}
		}
	}

	private async void OnMessageRecv(int fromPeer, byte[] data, TransferMode tfm)
	{
		if (NetInstance == null) return;

#if DEBUG
		string netDebugTrace = "";
#endif

		try
		{
			InternalNetMsg netMsg = InternalNetMsg.Deserialize(data);

#if DEBUG
			netDebugTrace = netMsg.StackTrace;
#endif

			int originFromPeer = (fromPeer == 1 && netMsg.OriginSender != 0)
				? netMsg.OriginSender
				: fromPeer;

			NetworkedObject? netObj;

			if (netMsg.Target.StartsWith("i:"))
			{
				string netId = netMsg.Target.TrimPrefix("i:");
				netObj = await Root.WaitForNetObjectAsync(netId, timeoutMs: 10000);
			}
			else
			{
				netObj = Root.GetNetObj(netMsg.Target);
			}

			if (netObj == null)
			{
				PT.PrintErr(
					$"Dropped packet: target not found. " +
					$"Target={netMsg.Target}, MethodId={netMsg.TargetMethod}, " +
					$"FromPeer={fromPeer}, OriginPeer={originFromPeer}, TransferMode={tfm}"
				);
				return;
			}

			RpcDispatchInfo dispatch = GetDispatchInfo(netObj, netMsg.TargetMethod);
			ValidateAuthority(dispatch.Attribute, dispatch.Method, netObj, originFromPeer, tfm, netMsg);

			Type[] paramTypes = dispatch.ParameterTypes;
			int expectedArgCount = paramTypes.Length;
			int actualArgCount = netMsg.ByteArrays.Count;

			if (actualArgCount != expectedArgCount)
			{
				PT.PrintErr(
					$"RPC arg count mismatch. Method={dispatch.Method.Name}, Target={netMsg.Target}, " +
					$"TargetType={netObj.GetType().FullName}, FromPeer={fromPeer}, OriginPeer={originFromPeer}, " +
					$"Expected={expectedArgCount}, Actual={actualArgCount}"
				);

				for (int i = 0; i < actualArgCount; i++)
				{
					PT.PrintErr($"  Arg[{i}] bytes={netMsg.ByteArrays[i]?.Length ?? 0}");
				}

				return;
			}

			if (netMsg.BroadcastAll && IsServer)
			{
				bool canSend;

				if (originFromPeer != 1)
				{
					lock (_rateLimiterLock)
					{
						if (!_peerRateLimiters.TryGetValue(originFromPeer, out RateLimiters? rateLimiter))
						{
							PT.PrintErr($"Dropped broadcast RPC from unknown peer {originFromPeer}");
							return;
						}

						canSend = tfm == TransferMode.Reliable
							? rateLimiter.Reliable.TryAccept()
							: rateLimiter.Unreliable.TryAccept();
					}
				}
				else
				{
					canSend = true;
				}

				netMsg.OriginSender = originFromPeer;

				if (canSend)
				{
					if (Globals.UseLogRPC)
					{
						PT.Print($"Broadcast {dispatch.Method.Name} from {originFromPeer} to all");
					}

					NetInstance.BroadcastMessage(
						netMsg.Serialize(),
						dispatch.Attribute.TransferMode,
						dispatch.Attribute.TransferChannel,
						[originFromPeer]
					);
				}
				else
				{
					if (Globals.UseLogRPC)
					{
						PT.Print($"Blocked {dispatch.Method.Name} from {originFromPeer}");
					}

					return;
				}
			}

			if (originFromPeer == LocalPeerID)
				return;

			object?[] args = ArrayPool<object?>.Shared.Rent(expectedArgCount);

			try
			{
				for (int i = 0; i < expectedArgCount; i++)
				{
					try
					{
						args[i] = NetworkPropSync.DeserializePropValue(netMsg.ByteArrays[i], paramTypes[i]);
					}
					catch (Exception ex)
					{
						PT.PrintErr(
							$"RPC arg deserialize failed. Method={dispatch.Method.Name}, Target={netMsg.Target}, " +
							$"TargetType={netObj.GetType().FullName}, ArgIndex={i}, " +
							$"TargetArgType={paramTypes[i].FullName}, Bytes={netMsg.ByteArrays[i]?.Length ?? 0}, " +
							$"FromPeer={fromPeer}, OriginPeer={originFromPeer}"
						);
						PT.PrintErr(ex.ToString());
						return;
					}
				}

				netObj.RemoteSenderId = originFromPeer;
				dispatch.Invoke(netObj, args);
			}
			catch (Exception ex)
			{
				if (OS.IsDebugBuild())
				{
					PT.PrintErr($"{dispatch.Method.Name} invoke failure: {ex}");
				}
			}
			finally
			{
				netObj.RemoteSenderId = 0;
				Array.Clear(args, 0, expectedArgCount);
				ArrayPool<object?>.Shared.Return(args);
			}
		}
		catch (Exception ex)
		{
#if DEBUG
			if (OS.IsDebugBuild())
			{
				PT.PrintErr("Invalid Packet: ", ex, "\nOrigin stack trace: ", netDebugTrace);
			}
#endif
		}
	}

	public void CreateServer(int port = 24221)
	{
		SetupNetwork();
		InitNodes();
		Root.ListenToNetworkService();

		if (Globals.IsInGDEditor)
		{
			GD.PushWarning("Server");
		}

		NetInstance = new();

		NetInstance.PeerConnected += OnPeerConnected;
		NetInstance.PeerDisconnected += OnPeerDisconnected;

		NetInstance.CreateServer(port);

		SetupPeer();

		LocalPeerID = 1;
		ActivePeerIDs.Add(1);
		IsServer = true;

		if (Globals.IsInGDEditor)
		{
			DisplayServer.WindowSetTitle("BrickVerse - Server");
		}

		if (IsProd)
		{
			_heartbeatTimer = new();
			Globals.Singleton.AddChild(_heartbeatTimer);
			_heartbeatTimer.Timeout += ServerSendHeartbeat;
			_heartbeatTimer.Start(HeartbeatIntervalSec);
			ServerSendHeartbeat();
		}
		Root.Players.PlayerAdded.Connect(OnPlayerAdded);
		Root.Players.PlayerRemoved.Connect(OnPlayerRemoved);

		OnServerStarted();
		OnSessionStarted();
	}

	public async void CreateClient(string address, int port = 24221)
	{
		SetupNetwork();

		NetInstance = new();

		NetInstance.ClientConnected += ConnectedToServer;
		NetInstance.ClientError += ConnectionFailed;
		NetInstance.ClientDisconnected += ServerDisconnected;

		await NetInstance.CreateClient(address, port);

		SetupPeer();
		OnSessionStarted();
	}

	private async void ServerSendHeartbeat()
	{
		_heartbeatCount++;
		APIHeartbeatResponse res = await ServerAPI.SendHeartbeat(World.Current!.Players.GetPlayerIDArray());
		if (res.Remove.Count > 0)
		{
			foreach (string r in res.Remove)
			{
				Player? player = Root.Players.GetPlayerByID(r);
				if (player != null)
				{
					DisconnectPeer(player.PeerID, TerminationMessage, DisconnectionCodeEnum.UserTerminated);
				}
			}
		}

		// Check for players in the server
		if (_heartbeatCount > HeartbeatBeforeCheckPlayers)
		{
			if (World.Current.Players.AbsolutePlayersCount <= 0)
			{
				PT.Print("No players, shutting down");
				ShutdownServer();
			}
		}

		_heartbeatTimer.Start(HeartbeatIntervalSec);
	}

	private void OnPlayerAdded(Player player)
	{
		if (IsProd)
		{
			Dictionary<string, string> data = new()
			{
				{ "userID", player.UserID.ToString() }
			};
			_ = ServerAPI.LogServerEvent(ServerEventType.ClientConnected, data);
		}
		OnPlayerChanged();
	}

	private void OnPlayerRemoved(Player player)
	{
		if (IsProd)
		{
			Dictionary<string, string> data = new()
			{
				{ "userID", player.UserID.ToString() }
			};
			_ = ServerAPI.LogServerEvent(ServerEventType.ClientDisconnected, data);
		}
		OnPlayerChanged();
	}

	private void OnPlayerChanged()
	{
		NetInstance?.AdaptBandwidth(_players.PlayersCount);
	}

	private async void OnPeerConnected(int peerID)
	{
		lock (_rateLimiterLock)
		{
			_peerRateLimiters.Add(peerID, new());
		}

		RpcId(peerID, nameof(NetRequestAuth), peerID);

		await Globals.Singleton.WaitAsync(AuthWaitTimeoutSec);
		Player? plr = _players.GetPlayerFromPeerID(peerID);
		if (plr == null)
		{
			DisconnectPeer(peerID, "Authentication Timeout", DisconnectionCodeEnum.AuthTimeout);
		}
	}

	private void OnPeerDisconnected(int peerID)
	{
		ActivePeerIDs.Remove(peerID);
		lock (_rateLimiterLock)
		{
			_peerRateLimiters.Remove(peerID);
		}

		Player? plr = _players.GetPlayerFromPeerID(peerID);
		if (plr != null)
		{
			// Remove from peer ID
			_players.PeerIDToPlayer.Remove(peerID);

			_players.InvokePlayerRemoved(plr);
			plr.ForceDelete();
		}
	}

	internal async void DisconnectPeer(int peerID, string reason = "", DisconnectionCodeEnum code = DisconnectionCodeEnum.Kicked)
	{
		if (!IsServer || LocalPeerID != 1)
		{
			PT.PrintErr(
				$"DisconnectPeer blocked on non-server. LocalPeerID={LocalPeerID}, IsServer={IsServer}, " +
				$"TargetPeer={peerID}, Reason={reason}, Code={code}"
			);
			return;
		}

		PT.Print($"DisconnectPeer -> Peer={peerID}, Reason={reason}, Code={code}");
		RpcId(peerID, nameof(NetRecvDisconnect), reason, (int)code);
		await Globals.Singleton.WaitAsync(3);
		NetInstance?.DisconnectPeer(peerID, true);
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvDisconnect(string reason, int code)
	{
		DisconnectSelf(reason, (DisconnectionCodeEnum)code);
	}

	internal void DisconnectSelf(string reason = "", DisconnectionCodeEnum code = DisconnectionCodeEnum.Unknown)
	{
		if (IsDisconnected) return;
		PT.Print("Shutting down network instance.");
		IsDisconnected = true;
		NetInstance?.Shutdown();
		Callable.From(() =>
		{
			ClientDisconnected?.Invoke(reason, code);
		}).CallDeferred();
	}

	private void OnTimeoutTimer()
	{
		if (!IsPlaceReplicationDone)
		{
			DisconnectSelf("Connection Timeout", DisconnectionCodeEnum.ConnectionTimeout);
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRequestAuth(int peerID)
	{
		LocalPeerID = peerID;

		if (Globals.IsInGDEditor)
		{
			GD.PushWarning($"Client ({peerID})");
		}

		ClientPlatformEnum platform = ClientPlatformEnum.Desktop;

		if (Globals.IsMobileBuild)
		{
			platform = ClientPlatformEnum.Mobile;
		}

		string platformName = Globals.ResolveCurrentPlatform();

		byte[] pk = [];

		if (IntegrityCheckLayer != null)
		{
			pk = IntegrityCheckLayer.Generate(platformName);
		}

		PT.Print($"NetRequestAuth received. AssignedPeer={peerID}, TestUserID={Entry.TestUserID}, NetworkMode={NetworkMode}, Platform={platformName}");
		RpcId(1, nameof(NetAuthResponse), Entry.TestUserID, ClientAuthAPI.JoinToken, (int)NetworkMode, (int)platform, platformName, pk);
	}


	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private async void NetAuthResponse(string testUserID, string userToken, int networkMode, int platform, string platformStr, byte[] pk)
	{
		if (NetInstance == null)
			return;

		int peerID = RemoteSenderId;

		PT.Print(
			$"NetAuthResponse received. Peer={peerID}, TestUserID={testUserID}, " +
			$"NetworkMode={(NetworkModeEnum)networkMode}, Platform={(ClientPlatformEnum)platform}"
		);

		if (peerID <= 1)
		{
			PT.PrintErr($"Auth rejected: invalid peer id {peerID}");
			return;
		}

		if (IntegrityCheckLayer != null)
		{
			try
			{
				if (!IntegrityCheckLayer.Validate(pk, platformStr))
				{
					throw new Exception("Integrity validation failed.");
				}
			}
			catch
			{
				DisconnectPeer(peerID, IntegrityFailureMessage, DisconnectionCodeEnum.IntegrityFail);
				return;
			}
		}

		if (networkMode != (int)NetworkMode)
		{
			DisconnectPeer(peerID, NetworkModeMismatchMessage, DisconnectionCodeEnum.NetworkModeMismatch);
			return;
		}

		APIValidateResponse validateRes;
		APIUserInfo userData;

		try
		{
			PT.Print("Auth: starting user validation...");

			if (!IsProd)
			{
				validateRes = new()
				{
					CanChat = true,
					UserID = testUserID,
					IsCreator = true,
					IsAgeRestricted = false
				};

				userData = new()
				{
					Username = "Player" + testUserID,
					Id = testUserID,
					IsStaff = false
				};
			}
			else
			{
				validateRes = await AuthenticatePlayer(userToken);
				userData = await BVAPI.GetUserFromID(validateRes.UserID);
			}

			if (IsProd && Root.WorldInfo.HasValue && Root.WorldInfo.Value.Creator.Type == "guild")
			{
				APIV3SocialGuild guildInfo = await BVAPI.GetGuildFromID(Root.WorldInfo.Value.Creator.Id);
				validateRes.IsCreator = guildInfo.Creator.Id == userData.Id;
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr("Auth failure:");
			PT.PrintErr(ex.ToString());
			DisconnectPeer(peerID, AuthFailureMessage, DisconnectionCodeEnum.AuthFailure);
			return;
		}

		if (!NetInstance.IsPeerConnected(peerID))
		{
			PT.PrintErr($"Auth stopped: peer {peerID} disconnected before player creation.");
			return;
		}

		string username = userData.Username;

		if (_players.GetPlayer(username) != null || _players.GetPlayerFromPeerID(peerID) != null)
		{
			DisconnectPeer(peerID, MultipleDeviceMessage, DisconnectionCodeEnum.MultipleDeviceNotAllowed);
			return;
		}

		Player plr;

		try
		{
			PT.Print("Auth: creating player...");

			plr = Globals.LoadInstance<Player>(Root)!;

			_players.PeerIDToPlayer.TryAdd(peerID, plr);

			plr.PeerID = peerID;
			plr.UserID = userData.Id;
			plr.Name = username;
			plr.IsAdmin = userData.IsStaff;
			plr.UserRoleClass = userData.UserRoleClass ?? "";

			plr.IsCreator = validateRes.IsCreator;
			plr.IsAgeRestricted = validateRes.IsAgeRestricted;
			plr.CanChat = validateRes.CanChat;
			plr.UserPlatform = (ClientPlatformEnum)platform;

			if (plr.IsAdmin)
			{
				plr.ChatColor = Color.FromHtml("#DD5555");
			}
			else if (Root.PlayerDefaults.ChatColorsEnabled)
			{
				plr.ChatColor = Player.ChatColorFromUserID(userData.Id);
			}
			else
			{
				plr.ChatColor = Root.PlayerDefaults.ChatColor;
			}

			plr.SetNetworkAuthority(peerID, true);
			plr.NetTransformAuthority = peerID;

			if (!_players.UseServerAuthority)
			{
				plr.NetPropAuthority = peerID;
			}

			if (NetworkMode == NetworkModeEnum.Client)
			{
				Root.Insert.InitializeDefaultNPC(plr);
			}

			plr.Parent = _players;
			plr.Anchored = true;
			plr.IsReady = false;

			foreach (Instance item in Root.PlayerDefaults.GetChildren())
			{
				if (item is Inventory)
					continue;

				NetworkedObject clone = item.Clone();

				if (clone is Instance instance)
				{
					instance.Parent = plr;
				}
			}

			PT.Print($"Auth: player created. Peer={peerID}, UserID={plr.UserID}, Username={plr.Name}");
		}
		catch (Exception ex)
		{
			PT.PrintErr("Auth: player creation failed.");
			PT.PrintErr(ex.ToString());
			DisconnectPeer(peerID, AuthFailureMessage, DisconnectionCodeEnum.AuthFailure);
			return;
		}

		try
		{
			PT.Print("Auth: invoking PeerPreInit...");
			PeerPreInit?.Invoke(peerID);

			PT.Print("Auth: starting SyncPlaceToPlayer...");
			ReplicateSync.SyncPlaceToPlayer(plr);
			PT.Print("Auth: SyncPlaceToPlayer returned.");
		}
		catch (Exception ex)
		{
			PT.PrintErr("Auth: SyncPlaceToPlayer failed.");
			PT.PrintErr(ex.ToString());
			DisconnectPeer(peerID, AuthFailureMessage, DisconnectionCodeEnum.AuthFailure);
			return;
		}

		await Globals.Singleton.WaitAsync(ConnectTimeoutSec);

		if (!plr.IsDeleted && !plr.IsReady)
		{
			DisconnectPeer(peerID, ConnectTimeoutMessage, DisconnectionCodeEnum.ConnectTimeout);
			plr.ForceDelete();
		}
	}

	private void ConnectedToServer()
	{
		PT.Print("Connected to server");
		ClientConnected = true;
		ClientConnectedToServer?.Invoke();
		placeReplicationStartTime = Time.GetTicksMsec();
	}

	private void ConnectionFailed(NetworkInstance.NetInstanceErrorEnum netInstanceError)
	{
		PT.Print("NetInstance Failure");
		string errMsg = "Network Instance Failure";
		if (_netInstanceErrorMessages.TryGetValue(netInstanceError, out var preMsg))
		{
			errMsg = preMsg;
		}
		DisconnectSelf(errMsg, DisconnectionCodeEnum.NetInstanceFailure);
	}

	private void ServerDisconnected()
	{
		ClientConnected = false;
		// wait one frame in case of disconnected due to other reasons
		Callable.From(() =>
		{
			if (!IsDisconnected)
			{
				PT.Print("Disconnected from server");
				DisconnectSelf("Disconnected from server", DisconnectionCodeEnum.Unknown);
			}
		}).CallDeferred();
	}

	private void OnServerStarted()
	{
		PT.Print("BrickVerse Server Started");
		if (IsProd)
		{
			_ = ServerAPI.LogServerEvent(ServerEventType.ServerStarted);
		}
		ServerStarted?.Invoke();
	}

	public async void ShutdownServer()
	{
		if (IsProd)
		{
			await ServerAPI.LogServerEvent(ServerEventType.ServerStopped);
		}
		Globals.Singleton.Quit();
	}

	private static void OnSessionStarted()
	{
		PT.Print("BrickVerse Network session started");
	}

	public static async Task<APIValidateResponse> AuthenticatePlayer(string token)
	{
		return await ServerAPI.ValidatePlayer(token);
	}

	/// <summary>
	/// Called by World Replication when transform is ready
	/// </summary>
	internal void NetWorldSyncd()
	{
		if (IsPlaceReplicationDone)
		{
			return;
		}
		IsPlaceReplicationDone = true;

		InitNodes();

		PT.Print("[Client] World Replicated");
		RpcId(1, nameof(NetReqAllTransform));
	}

	/// <summary>
	/// Called by TransformSync when transform is ready
	/// </summary>
	internal void NetTransformSyncd()
	{
		if (IsTransformReplicateDone)
		{
			return;
		}
		IsTransformReplicateDone = true;
		PT.Print("[Client] Transform Replicated");

		// Request for Localplayer
		if (_players != null)
		{
			RpcId(1, nameof(NetReqAllScripts));
		}
		else
		{
			DisconnectSelf("INTERNAL BUG: Players not found, at transform sync flow", DisconnectionCodeEnum.PlayersNotFound);
		}
	}

	/// <summary>
	/// Called by ClientScriptSync when scripts is ready
	/// </summary>
	internal void NetScriptSyncd()
	{
		if (IsClientScriptReplicateDone)
		{
			return;
		}
		IsClientScriptReplicateDone = true;

		IsReplicationDone = true;

		PT.Print("[Client] Script Replicated");
		ClientWorldReady?.Invoke();

		PT.Print("[Client] Replication done in: ", (Time.GetTicksMsec() - placeReplicationStartTime) / 1000, "s");

		// Request for Localplayer
		if (_players != null)
		{
			_players.ReqLocalPlayer();
		}
		else
		{
			DisconnectSelf("INTERNAL BUG: Players not found, at script sync flow", DisconnectionCodeEnum.PlayersNotFound);
		}
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetReqAllTransform()
	{
		TransformSync.SyncAllTransformToPeer(RemoteSenderId);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetReqAllScripts()
	{
		ScriptSync.SyncScriptsToPeer(RemoteSenderId);
	}

	// Emit when localplayer is ready
	public void OnLocalPlayerReady()
	{
		if (Globals.IsInGDEditor)
		{
			DisplayServer.WindowSetTitle($"BrickVerse - Client [{LocalPeerID}]");
		}

		Rpc(nameof(NetPlayerReportReady));
	}

	// Emit when player reports itself as ready
	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, CallLocal = true, AllowToServerOnly = false)]
	private void NetPlayerReportReady()
	{
		int peerID = RemoteSenderId;
		Player? plr = Root.Players.GetPlayerFromPeerID(peerID);

		if (plr != null && !plr.IsReady)
		{
			ActivePeerIDs.Remove(peerID);
			ActivePeerIDs.Add(peerID);

			if (IsServer)
			{
				plr.IsReady = true;
				plr.Anchored = false;
				plr.Respawn();
				Root.Players.InvokePlayerAdded(plr);
				RpcId(peerID, nameof(NetRecvReportReady));
			}
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvReportReady()
	{
		ClientReady?.Invoke();

		// Dispatch run client scripts
		try
		{
			Root.DispatchClientScriptRun();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}

	// Check authority (if is server/authority, return true)
	public static bool CheckAuthority(int peerID, int authorityID)
	{
		if (Globals.CurrentAppEntry != Globals.AppEntryEnum.Client) { return true; }
		return peerID == 1 || peerID == authorityID;
	}

	[MemoryPackable]
	public partial struct NetReplicateData()
	{
		[JsonInclude] public string Name = null!;
		[JsonInclude] public string ClassName = null!;
		[JsonInclude] public string NodePath = null!;
		[JsonInclude] public string ParentNodePath = null!;
		[JsonInclude] public string ParentNodeID = null!;
		[JsonInclude] public int Authority;
		[JsonInclude] public string NetworkID = "";
		[JsonInclude] public NetPropReplicateData[] Props = [];
		[JsonInclude] public bool IsSyncOnce = false;
		[JsonInclude] public int Index = 0;
		[JsonInclude] public int Sequence = 0;

		public override readonly bool Equals(object? obj)
		{
			return obj is NetReplicateData n && n.NetworkID == NetworkID;
		}

		public override readonly int GetHashCode()
		{
			return NetworkID.GetHashCode();
		}

		public static bool operator ==(NetReplicateData left, NetReplicateData right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(NetReplicateData left, NetReplicateData right)
		{
			return !(left == right);
		}
	}

	[MemoryPackable]
	public partial struct NetPropReplicateData()
	{
		[JsonInclude] public string Name = null!;
		[JsonInclude] public byte[] ValueRaw = null!;
		[JsonInclude] public long Sequence = 0;
	}

	[MemoryPackable]
	public partial class NetPropNetworkedObjectRef
	{
		[JsonInclude] public string NetID = null!;
		[MemoryPackIgnore]
		[JsonIgnore]
		public PropertyInfo? TargetProp;
	}

	[MemoryPackable]
	public partial struct NetBatchTransformData()
	{
		[JsonInclude]
		public string NetID = null!;
		[JsonInclude]
		public byte[] Value = null!;
	}

	[MemoryPackable]
	public partial struct NetBatchScriptData()
	{
		[JsonInclude]
		public string NetID = null!;
		[JsonInclude]
		public byte[] Bytecode = null!;
	}

	[JsonSerializable(typeof(string))]
	[JsonSerializable(typeof(bool))]
	[JsonSerializable(typeof(byte))]
	[JsonSerializable(typeof(sbyte))]
	[JsonSerializable(typeof(short))]
	[JsonSerializable(typeof(ushort))]
	[JsonSerializable(typeof(int))]
	[JsonSerializable(typeof(uint))]
	[JsonSerializable(typeof(long))]
	[JsonSerializable(typeof(ulong))]
	[JsonSerializable(typeof(float))]
	[JsonSerializable(typeof(double))]
	[JsonSerializable(typeof(decimal))]

	[JsonSerializable(typeof(string[]))]
	[JsonSerializable(typeof(byte[]))]

	[JsonSerializable(typeof(Vector2))]
	[JsonSerializable(typeof(Vector3))]
	[JsonSerializable(typeof(Color))]

	[JsonSerializable(typeof(Vector2Dto))]
	[JsonSerializable(typeof(Vector3Dto))]
	[JsonSerializable(typeof(ColorDto))]
	[JsonSerializable(typeof(Transform3DDto))]
	[JsonSerializable(typeof(UnitQuaternionDto))]
	[JsonSerializable(typeof(TransformPayloadDto))]

	[JsonSerializable(typeof(NetPropNetworkedObjectRef))]
	[JsonSerializable(typeof(NetPropReplicateData))]
	[JsonSerializable(typeof(NetBatchScriptData))]
	[JsonSerializable(typeof(NetBatchTransformData))]
	[JsonSerializable(typeof(List<NetPropReplicateData>))]
	[JsonSerializable(typeof(List<NetBatchTransformData>))]
	[JsonSerializable(typeof(List<NetReplicateData>))]
	[JsonSerializable(typeof(NetPropReplicateData[]))]
	[JsonSerializable(typeof(NetBatchTransformData[]))]
	[JsonSerializable(typeof(NetReplicateData[]))]
	[JsonSerializable(typeof(NetBatchScriptData[]))]
	internal partial class NetDataGenerationContext : JsonSerializerContext
	{
	}

	public enum DisconnectionCodeEnum
	{
		Unknown,
		ConnectionFailure,
		ConnectionTimeout,
		Kicked,
		UserTerminated,
		AuthFailure,
		MultipleDeviceNotAllowed,
		NetworkModeMismatch,
		PlayerNotFound,
		PlayersNotFound,
		DatamodelGone,
		StatusFetchFailure,
		Teleport,
		AuthTimeout,
		AFK,
		ConnectTimeout,
		IntegrityFail,
		NetInstanceFailure
	}

	public enum NetworkModeEnum
	{
		Client,
		Creator,
		Renderer
	}

	[ScriptEnum]
	public enum ClientPlatformEnum
	{
		Desktop,
		Mobile,
		VR
	}

	private class RateLimiters
	{
		public SlidingWindowRateLimiter Reliable = new(MaxBroadcastPacketPerSec, TimeSpan.FromSeconds(1));
		public SlidingWindowRateLimiter Unreliable = new(MaxBroadcastPacketPerSec, TimeSpan.FromSeconds(1));
	}

	private class RpcDispatchInfo
	{
		public required MethodInfo Method { get; init; }
		public required NetRpcAttribute Attribute { get; init; }
		public required Type[] ParameterTypes { get; init; }
		public required Action<NetworkedObject, object?[]?> Invoke { get; init; }
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#if CREATOR || DEBUG || PT_DOCKER
#define ALLOW_SELFHOST
#endif

using Godot;
using BrickVerse.Client.Debugger;
using BrickVerse.Client.Settings;
using BrickVerse.Client.Settings.Appliers;
using BrickVerse.Client.WebAPI;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Services;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Shared.Settings;
#if CREATOR
using BrickVerse.Creator.Utils;
#endif
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BrickVerse.Client;

public sealed partial class ClientEntry : Node3D
{
	private const int ServerStatusPollIntervalSeconds = 2;
	private const string LocalTestLogPath = "user://logs/localtest";
	private const string DefaultLocalAddress = "127.0.0.1";
	private const int DefaultLocalPort = 24221;

	public event Action? NetworkEssentialsReady;
	public event Action? LeaveGameRequested;
	public event Action? TargetServerReady;

	public NetworkService NetworkService { get; private set; } = null!;
	public DatamodelBridge DatamodelBridge { get; private set; } = null!;
	public World Root { get; private set; } = null!;

	public bool IsFocused { get; private set; }
	public bool IsContained { get; set; }
	public bool IsNetEssentialsReady { get; private set; }
	public bool IsSoloTest { get; private set; }
	public bool TestModeReady { get; private set; }

	public string TestUserID { get; private set; } = Globals.TestUserIdStart;
	public int TestClientCount { get; private set; }

#if ALLOW_SELFHOST
	public Vector3? DebugSpawnPos { get; private set; }
#endif

	internal DebugAgent? DebugAgent { get; private set; }

	private readonly List<int> _localTestClientProcessIds = [];

	private Timer? _serverStatusPollTimer;
	private APIClientAuthResponseMessage? _clientConnectionInfo;
	private string? _debugServerAddress;
	private int? _debugServerPort;

	public ClientEntry()
	{
		Root = Globals.LoadInstance<World>();
	}

	public async void Entry(ClientEntryData? entryData = null)
	{
		PT.Print("ClientEntry starting...");
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		Stopwatch stopwatch = Stopwatch.StartNew();
		ClientLaunchOptions launchOptions = BuildLaunchOptions(entryData);

		IsFocused = !IsContained;

#if ALLOW_SELFHOST
		ApplySelfHostedLaunchOptions(launchOptions);
#endif

		await ConnectDebugAgentAsync(launchOptions.DebugAddress, launchOptions.DebugId, stopwatch);
		ApplyMobileWindowSettings();
		CreateCoreServices(launchOptions.IsServer);
		InitializeWorld(stopwatch);

#if CREATOR
		ApplyCreatorTestToken(launchOptions.CreatorToken);
#endif

#if ALLOW_SELFHOST
		await LoadSelfHostedWorldIfNeededAsync(launchOptions, stopwatch);
		StartSoloTestClientsIfNeeded(launchOptions);
#endif

		ApplyFpsFlags();

		if (launchOptions.IsServer)
		{
			PT.Print("Starting server...");
			await StartServerAsync(launchOptions);
		}

		if (launchOptions.IsClient)
		{
			PT.Print("Starting client...");
			await StartClientAsync(launchOptions);
		}
	}

	private static ClientLaunchOptions BuildLaunchOptions(ClientEntryData? entryData)
	{
		Dictionary<string, string> args = Globals.ReadCmdArgs();
		args.TryGetValue("network", out string? networkMode);
		args.TryGetValue("entry", out string? worldEntryPath);
		args.TryGetValue("token", out string? authToken);
		args.TryGetValue("debug", out string? debugAddress);
		args.TryGetValue("debug-id", out string? debugId);

		bool runAsServer = networkMode == "server";

		ClientLaunchOptions options = new()
		{
			IsClient = !runAsServer,
			IsServer = runAsServer,
			IsSubWorld = args.ContainsKey("subworld"),
			WorldEntryPath = string.IsNullOrWhiteSpace(worldEntryPath) ? null : worldEntryPath,
			AuthToken = authToken,
			DebugAddress = debugAddress,
			DebugId = debugId,
		};

#if ALLOW_SELFHOST
		args.TryGetValue("address", out string? localAddress);
		args.TryGetValue("world", out string? localWorldPath);
		args.TryGetValue("port", out string? localPortText);
		args.TryGetValue("id", out string? testUserId);
		args.TryGetValue("solo", out string? soloWorldPath);
		args.TryGetValue("nplr", out string? soloClientCountText);
		args.TryGetValue("spawnpos", out string? debugSpawnPositionText);
		args.TryGetValue("ctoken", out string? creatorToken);

		options.LocalAddress = localAddress ?? DefaultLocalAddress;
		options.LocalPort = (localPortText ?? DefaultLocalPort.ToString()).ToInt();
		options.LocalWorldPath = localWorldPath;
		options.TestUserId = string.IsNullOrWhiteSpace(testUserId) ? Globals.TestUserIdStart : testUserId;
		options.SoloWorldPath = soloWorldPath;
		options.SoloClientCount = (soloClientCountText ?? "1").ToInt();
		options.DebugSpawnPositionText = debugSpawnPositionText;
		options.CreatorToken = creatorToken;

#if PT_DOCKER
		options.LocalPort = 7777;
#endif
#endif

		if (entryData.HasValue)
		{
			ApplyEntryDataOverrides(options, entryData.Value);
		}

		return options;
	}

	private static void ApplyEntryDataOverrides(ClientLaunchOptions options, ClientEntryData entryData)
	{
		if (entryData.TestIsServer.HasValue)
		{
			options.IsServer = entryData.TestIsServer.Value;
			options.IsClient = !entryData.TestIsServer.Value;
		}

		options.AuthToken = entryData.Token ?? options.AuthToken;
		options.DebugId = entryData.TestDebugID ?? options.DebugId;

#if ALLOW_SELFHOST
		options.LocalWorldPath = entryData.TestWorldPath ?? options.LocalWorldPath;
		options.LocalAddress = entryData.ConnectAddress ?? options.LocalAddress;
		options.LocalPort = entryData.ConnectPort ?? options.LocalPort;
		options.TestUserId = entryData.TestUserID ?? options.TestUserId;
#endif
	}

#if ALLOW_SELFHOST
	private void ApplySelfHostedLaunchOptions(ClientLaunchOptions options)
	{
		TestUserID = string.IsNullOrWhiteSpace(options.TestUserId) ? Globals.TestUserIdStart : options.TestUserId;

		if (!string.IsNullOrWhiteSpace(options.DebugSpawnPositionText))
		{
			DebugSpawnPos = ParseDebugSpawnPosition(options.DebugSpawnPositionText);
		}

		if (string.IsNullOrWhiteSpace(options.SoloWorldPath))
		{
			return;
		}

		options.IsClient = false;
		options.IsServer = true;
		options.LocalWorldPath = options.SoloWorldPath;
		IsSoloTest = true;
	}

	private static Vector3 ParseDebugSpawnPosition(string spawnPositionText)
	{
		string[] components = spawnPositionText.TrimStart('v').Split(',');
		return new Vector3(
			int.Parse(components[0]),
			int.Parse(components[1]),
			int.Parse(components[2])
		);
	}
#endif

	private async System.Threading.Tasks.Task ConnectDebugAgentAsync(string? debugAddress, string? debugId, Stopwatch stopwatch)
	{
		if (string.IsNullOrWhiteSpace(debugAddress))
		{
			return;
		}

		DebugAgent = new DebugAgent();
		stopwatch.Restart();
		PT.Print($"Connecting to debug server {debugAddress}");

		try
		{
			string[] addressParts = debugAddress.Split(':');
			_debugServerAddress = addressParts[0];
			_debugServerPort = int.Parse(addressParts[1]);

			await DebugAgent.Start(_debugServerAddress, _debugServerPort.Value, debugId);
			PT.Print($"Debug server connected in {stopwatch.ElapsedMilliseconds}ms");
		}
		catch (Exception ex)
		{
			GD.PushError(ex);
			Globals.Singleton.Quit(true);
		}
	}

	private static void ApplyMobileWindowSettings()
	{
		if (!Globals.IsMobileBuild)
		{
			return;
		}

		DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Landscape);
		DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
	}

	private void CreateCoreServices(bool isServer)
	{
		ClientSettingsService settings = new()
		{
			Name = "ClientSettings",
			Entry = this,
		};

		AddChild(settings, true, InternalMode.Front);
		settings.Init();

		AssetLoader.Singleton.MaxConcurrentRequests = ClientSettingsService.Instance.Get<int>(SharedSettingKeys.Advanced.AssetQueue);

		settings.AddChild(new DisplaySettingsApplier { Name = "DisplaySettingsApplier" }, true, InternalMode.Front);
		settings.AddChild(new AudioSettingsApplier { Name = "AudioSettingsApplier" }, true, InternalMode.Front);
		settings.AddChild(new GraphicsSettingsApplier { Name = GraphicsSettingsApplier.NodeName, Settings = settings }, true, InternalMode.Front);

		DatamodelBridge = new DatamodelBridge { Name = "DatamodelBridge" };
		AddChild(DatamodelBridge, true);

		NetworkService = new NetworkService
		{
			Name = "NetworkService",
			Entry = this,
			IsServer = isServer,
			NetworkParent = Root,
		};

		NetworkService.Attach(Root);
	}

	private void InitializeWorld(Stopwatch stopwatch)
	{
		AddChild(Root.GDNode, true);

		Root.Root = Root;
		Root.Entry = this;
		Root.World3D = GetWorld3D();
		Root.InitEntry();

		DatamodelBridge.Attach(Root);
		World.Current = Root;

		IsNetEssentialsReady = true;
		NetworkEssentialsReady?.Invoke();

		stopwatch.Restart();
		Root.Setup();
		PT.Print($"World setup in {stopwatch.ElapsedMilliseconds}ms");
	}

#if CREATOR
	private static void ApplyCreatorTestToken(string? creatorToken)
	{
		if (!string.IsNullOrWhiteSpace(creatorToken))
		{
			CreatorAPI.SetToken(creatorToken);
		}
	}
#endif

#if ALLOW_SELFHOST
	private async System.Threading.Tasks.Task LoadSelfHostedWorldIfNeededAsync(ClientLaunchOptions options, Stopwatch stopwatch)
	{
		if (!options.IsServer)
		{
			return;
		}

		FreeLook freeLook = CreateLocalServerCamera();
		stopwatch.Restart();

		if (!string.IsNullOrWhiteSpace(options.LocalWorldPath))
		{
			await LoadLocalWorldFileAsync(options.LocalWorldPath, options.WorldEntryPath);
		}

		Root.Environment.CameraOverride = freeLook;
		PT.Print($"World loaded in {stopwatch.ElapsedMilliseconds}ms");
	}

	private FreeLook CreateLocalServerCamera()
	{
		FreeLook freeLook = new() { Name = "FreeLook" };
		Root.GDNode.AddChild(freeLook, false, @internal: Node.InternalMode.Back);

		freeLook.GlobalPosition = new Vector3(0, 2, -4);
		freeLook.RotationDegrees = new Vector3(-25, 180, 0);

		return freeLook;
	}

	private async System.Threading.Tasks.Task LoadLocalWorldFileAsync(string worldPath, string? worldEntryPath)
	{
		string absoluteWorldPath = ProjectSettings.GlobalizePath(worldPath);
		PT.Print("Loading world with entry: ", worldEntryPath);

		try
		{
			await DatamodelLoader.LoadWorldFile(Root, absoluteWorldPath, worldEntryPath);
			PT.Print("World loaded!");
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			OS.Alert("World load failed");
			Globals.Singleton.Quit();
		}
	}

	private void StartSoloTestClientsIfNeeded(ClientLaunchOptions options)
	{
		if (!IsSoloTest || options.IsSubWorld)
		{
			return;
		}

		for (int i = 1; i <= options.SoloClientCount; i++)
		{
			LocalTestStartClient(options.LocalPort);
		}

		TestModeReady = true;
		Globals.BeforeQuit += KillLocalTestClients;
	}

	private void KillLocalTestClients()
	{
		foreach (int processId in _localTestClientProcessIds)
		{
			if (OS.IsProcessRunning(processId))
			{
				OS.Kill(processId);
			}
		}
	}
#endif

	private static void ApplyFpsFlags()
	{
		if (OS.HasFeature("lowfps"))
		{
			Engine.MaxFps = 15;
			return;
		}

		if (OS.HasFeature("potatofps"))
		{
			Engine.MaxFps = 2;
		}
	}

	private async System.Threading.Tasks.Task StartServerAsync(ClientLaunchOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.AuthToken))
		{
			await StartProductionServerAsync(options.AuthToken);
			return;
		}

#if ALLOW_SELFHOST
		await StartLocalServerAsync(options.LocalPort);
#endif
	}

	private async System.Threading.Tasks.Task StartProductionServerAsync(string authToken)
	{
		NetworkService.IsProd = true;
		Engine.MaxFps = 30;

		ClientAuthAPI.SetAuthToken(authToken);
		ServerAPI.SetAuthToken(authToken);

		try
		{
			PT.Print("Server authenticating...");
			Stopwatch stopwatch = Stopwatch.StartNew();

			APIServerListenResponse listenResponse = await ClientAuthAPI.SendServerListen();
			LogServerListenResponse(listenResponse);

			Root.WorldID = listenResponse.WorldID;
			Root.ServerID = listenResponse.ServerID;

			PT.Print("Listen sent ", stopwatch.ElapsedMilliseconds, "ms");
			PT.Print("Downloading world...");

			stopwatch.Restart();
			byte[] worldContent = await ServerAPI.DownloadWorld(listenResponse.WorldID);
			PT.Print("World downloaded in ", stopwatch.ElapsedMilliseconds, "ms");

			stopwatch.Restart();
			PT.Print("Constructing...");
			await DatamodelLoader.LoadWorldBytes(Root, worldContent, listenResponse.PlacePath);
			PT.Print("Construction finished in ", stopwatch.ElapsedMilliseconds, "ms");

			int serverPort = listenResponse.Port;
#if PT_DOCKER
			serverPort = 7777;
#endif
			NetworkService.CreateServer(serverPort);
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex.ToString());
			Globals.Singleton.Quit();
		}
	}

	private static void LogServerListenResponse(APIServerListenResponse listenResponse)
	{
		PT.Print("BrickVerse Server Info ----");
		PT.Print("Server ID: ", listenResponse.ServerID);
		PT.Print("World ID: ", listenResponse.WorldID);
		PT.Print("Port: ", listenResponse.Port);
		PT.Print("Started at: ", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));
		PT.Print("--------------------------");
	}

#if ALLOW_SELFHOST
	private async System.Threading.Tasks.Task StartLocalServerAsync(int port)
	{
		try
		{
			PT.Print("Starting local server on " + port);
			NetworkService.CreateServer(port);

			if (DebugAgent != null)
			{
				await DebugAgent.SendServerReady();
			}
		}
		catch (Exception ex)
		{
			GD.PushError(ex);
			OS.Alert("Local host start failure");
			Globals.Singleton.Quit(true);
		}
	}
#endif

	private async System.Threading.Tasks.Task StartClientAsync(ClientLaunchOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.AuthToken))
		{
			await StartProductionClientAsync(options.AuthToken);
			return;
		} else
		{
			PT.PrintErr("No auth token provided, cannot start production client.");
		}

#if ALLOW_SELFHOST
		NetworkService.CreateClient(options.LocalAddress, options.LocalPort);
#endif
	}

	private async System.Threading.Tasks.Task StartProductionClientAsync(string authToken)
	{
		PT.Print("Connecting to BrickVerse...");
		ClientAuthAPI.SetAuthToken(authToken);

		try
		{
			PT.Print("========== CLIENT CONNECT ==========");
			PT.Print($"Auth Token Present: {!string.IsNullOrWhiteSpace(authToken)}");
			PT.Print("Calling ClientAuthAPI.SendClientConnect()...");

			_clientConnectionInfo = await ClientAuthAPI.SendClientConnect();
			LogClientConnectionInfo(_clientConnectionInfo.Value);

			Root.WorldID = _clientConnectionInfo.Value.WorldID;
			Root.ServerID = _clientConnectionInfo.Value.ServerID;
			NetworkService.IsProd = true;

			StartServerStatusPolling();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			NetworkService.DisconnectSelf(ex.Message, NetworkService.DisconnectionCodeEnum.ConnectionFailure);
		}
	}

	private static void LogClientConnectionInfo(APIClientAuthResponseMessage connectionInfo)
	{
		PT.Print("BrickVerse Network Info ----");
		PT.Print("World ID: ", connectionInfo.WorldID);
		PT.Print("Server ID: ", connectionInfo.ServerID);
		PT.Print("Connected at: ", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));
		PT.Print("--------------------------");
	}

	private void StartServerStatusPolling()
	{
		_serverStatusPollTimer = new Timer();
		AddChild(_serverStatusPollTimer);

		_serverStatusPollTimer.Timeout += PollServerStatus;
		_serverStatusPollTimer.Start(ServerStatusPollIntervalSeconds);
	}

	private async void PollServerStatus()
	{
		if (_serverStatusPollTimer == null || !_clientConnectionInfo.HasValue)
		{
			return;
		}

		try
		{
			APIServerStatus status = await ClientAuthAPI.CheckServerStatus();
			PT.Print(status.Status);

			if (status.Status == "started")
			{
				TargetServerReady?.Invoke();
				NetworkService.CreateClient(_clientConnectionInfo.Value.IP, _clientConnectionInfo.Value.Port);
				_serverStatusPollTimer.QueueFree();
				_serverStatusPollTimer = null;
				return;
			}
		}
		catch (Exception ex)
		{
			GD.PushError(ex);
		}

		_serverStatusPollTimer!.Start(ServerStatusPollIntervalSeconds);
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_fullscreen"))
		{
			bool fullscreen = ClientSettingsService.Instance.Get<bool>(SharedSettingKeys.Display.Fullscreen);
			ClientSettingsService.Instance.Set(SharedSettingKeys.Display.Fullscreen, !fullscreen);
		}

		base._UnhandledKeyInput(@event);
	}

	public override void _Process(double delta)
	{
		if (IsSoloTest && TestModeReady)
		{
			RemoveClosedLocalTestClients();

			if (_localTestClientProcessIds.Count == 0)
			{
				Globals.Singleton.Quit();
			}
		}

		base._Process(delta);
	}

	private void RemoveClosedLocalTestClients()
	{
		foreach (int processId in _localTestClientProcessIds.ToArray())
		{
			if (!OS.IsProcessRunning(processId))
			{
				_localTestClientProcessIds.Remove(processId);
			}
		}
	}

	public void LeaveGame()
	{
		if (OS.HasFeature("mobile-ui"))
		{
			Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.MobileUI);
			return;
		}

		if (!IsContained)
		{
			Globals.Singleton.Quit();
			return;
		}

		LeaveGameRequested?.Invoke();
	}

	public void LocalTestStartClient(int port = DefaultLocalPort)
	{
		TestClientCount++;

		int testClientId = TestClientCount;
		string executablePath = OS.GetExecutablePath();
		string logDirectory = ProjectSettings.GlobalizePath(LocalTestLogPath);

		if (!DirAccess.DirExistsAbsolute(logDirectory))
		{
			DirAccess.MakeDirRecursiveAbsolute(logDirectory);
		}

		string logFilePath = logDirectory.PathJoin($"{testClientId}.txt");

		List<string> args =
		[
			"--windowed",
			"--log-file", logFilePath,
			"-network", "client",
			"-id", testClientId.ToString(),
			"-ltchild",
			"-port", port.ToString(),
		];

		if (Globals.IsInGDEditor)
		{
			args.AddRange(["--remote-debug", "tcp://127.0.0.1:6007"]);
		}

		if (_debugServerAddress != null && _debugServerPort.HasValue)
		{
			args.AddRange(["-debug", $"{_debugServerAddress}:{_debugServerPort.Value}"]);
		}

		args.AddRange(["--rendering-method", RenderingDeviceSwitcher.GetCurrentDriverName()]);
		args.Add("-rmswignore");

		int processId = OS.CreateProcess(executablePath, [.. args]);
		_localTestClientProcessIds.Add(processId);

		PT.Print($"Started new client process with ID {processId}");
	}

	public override void _ExitTree()
	{
		if (!Globals.IsExiting)
		{
			Root.ForceDelete();
			DatamodelBridge.Free();
		}

		base._ExitTree();
	}

	private sealed class ClientLaunchOptions
	{
		public bool IsClient { get; set; } = true;
		public bool IsServer { get; set; }
		public bool IsSubWorld { get; set; }

		public string? AuthToken { get; set; }
		public string? WorldEntryPath { get; set; }
		public string? DebugAddress { get; set; }
		public string? DebugId { get; set; }

#if ALLOW_SELFHOST
		public string LocalAddress { get; set; } = DefaultLocalAddress;
		public int LocalPort { get; set; } = DefaultLocalPort;
		public string TestUserId { get; set; } = Globals.TestUserIdStart;
		public string? LocalWorldPath { get; set; }
		public string? SoloWorldPath { get; set; }
		public int SoloClientCount { get; set; } = 1;
		public string? DebugSpawnPositionText { get; set; }
		public string? CreatorToken { get; set; }
#endif
	}

	public struct ClientEntryData
	{
		public string? ConnectAddress;
		public int? ConnectPort;
		public string? Token;
		public string? TestUserID;
		public bool? TestIsServer;
		public string? TestWorldPath;
		public string? TestDebugID;
	}
}

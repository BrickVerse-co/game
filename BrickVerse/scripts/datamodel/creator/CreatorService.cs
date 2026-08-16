// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Creator;
using BrickVerse.Creator.Debugger;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.TeamCreate;
using BrickVerse.Creator.Managers;
using BrickVerse.Creator.UI;
using BrickVerse.Creator.UI.Splashes;
using BrickVerse.Creator.UI.TextEditor;
using BrickVerse.Creator.Utils;
using BrickVerse.Formats;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using BrickVerse.Utils;
using BrickVerse.Datamodel.Services;
using BrickVerse.Schemas.Debugger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BrickVerse.Datamodel.Creator;

[Static("Creator"), ExplorerExclude]
public sealed partial class CreatorService : Node, IScriptObject
{
	public const string BrickVerseFolderName = "BrickVerse/";
	public const string CloudWorldProjectsFolderName = "BrickVerseCreator/My Worlds";
	public string? PendingModelImportPath { get; set; }

	private long _localTestIDCounter = 0;

	[ScriptProperty] public static CreatorInterface Interface { get; private set; } = null!;
	public static CreatorClipboard Clipboard { get; private set; } = null!;

	[ScriptProperty] public static World? CurrentGame => World.Current;

	public static CreatorService Singleton { get; set; } = null!;
	public static CreatorSession? CurrentSession { get; internal set; }

	[ScriptProperty] public BVSignal LocalTestStarted { get; private set; } = new();
	[ScriptProperty] public BVSignal LocalTestStopped { get; private set; } = new();
	[ScriptProperty] public bool LocalTestActive => LocalTestProcesses.Count != 0;
	public List<int> LocalTestProcesses { get; private set; } = [];
	public List<string> LocalTestWorlds { get; private set; } = [];
	public int LocalTestPlayerCount { get; set; } = 1;
	public static List<CreatorSession> Sessions { get; private set; } = [];
	public static Dictionary<string, CreatorSession> LocalTestIDToSession { get; private set; } = [];
	public static Dictionary<CreatorSession, string> SessionToLocalTestID { get; private set; } = [];

	internal DebugServer DebugServer { get; private set; } = null!;
	private readonly Dictionary<int, RuntimeDebugWindow> _runtimeDebugWindows = [];
	private int? _primaryRuntimeClientProcess;
	private Rect2I? _lastRuntimeViewportRect;
	private bool _lastRuntimeViewportVisible;
	private double _runtimeViewportSyncElapsed;
	private double _runtimeViewportForceSyncElapsed;
	private double _popupStackSyncElapsed;
	private bool _creatorPromotedForPopup;
	private double _studioPresenceHeartbeatElapsed;
	private MessageRuntimeDeviceEmulation? _deviceEmulation;

	public CreatorService()
	{
		Singleton = this;
		Interface = new()
		{
			Service = this
		};
		Clipboard = new()
		{
			Service = this
		};
		AddChild(Interface);
		AddChild(new TeamCreateService());

		string polyFolder = Path.Join(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), BrickVerseFolderName);
		if (!Directory.Exists(polyFolder))
		{
			Directory.CreateDirectory(polyFolder);
		}
	}

	public override void _Ready()
	{
		OS.LowProcessorUsageMode = true;
		Globals.BeforeQuit += OnBeforeQuit;
		CreatorAPI.UserAuthenticated += OnCreatorAuthenticated;
		DebugServer = new();
		DebugServer.Start();
		DebugServer.RuntimeConnected += OnRuntimeConnected;
		DebugServer.RuntimeDisconnected += OnRuntimeDisconnected;
		DebugServer.RuntimeSnapshotReceived += OnRuntimeSnapshotReceived;
		DebugServer.RuntimeLogReceived += OnRuntimeLogReceived;

		DisplayServer.WindowSetDropFilesCallback(Callable.From<string[]>(OnFilesDropped));
		_ = CreatorAPI.UpdateStudioPresence(0, active: true);
		base._Ready();
	}

	public override void _ExitTree()
	{
		Globals.BeforeQuit -= OnBeforeQuit;
		CreatorAPI.UserAuthenticated -= OnCreatorAuthenticated;
		DebugServer.RuntimeConnected -= OnRuntimeConnected;
		DebugServer.RuntimeDisconnected -= OnRuntimeDisconnected;
		DebugServer.RuntimeSnapshotReceived -= OnRuntimeSnapshotReceived;
		DebugServer.RuntimeLogReceived -= OnRuntimeLogReceived;
		base._ExitTree();
	}

	private void OnRuntimeConnected(int processId, bool isServer)
	{
		if (processId == 0 || _runtimeDebugWindows.ContainsKey(processId)) return;
		if (!isServer)
		{
			if (_primaryRuntimeClientProcess.HasValue) return;
			_primaryRuntimeClientProcess = processId;
			_lastRuntimeViewportRect = null;
			_runtimeViewportSyncElapsed = double.MaxValue;
			if (_deviceEmulation != null) DebugServer.SetRuntimeDeviceEmulation(processId, _deviceEmulation);
		}

		RuntimeDebugWindow window = new(DebugServer, processId, isServer);
		_runtimeDebugWindows[processId] = window;
		TabContainer bottomTabs = CreatorGUIRoot.Singleton.GetNode<TabContainer>(
			"Splitter/Center/BottomTabs/Tabs"
		);
		bottomTabs.AddChild(window);
		window.Activate();
	}

	private void OnCreatorAuthenticated(BrickVerse.Schemas.API.OpenIdUserInfoResponse _)
	{
		long worldId = CurrentSession?.Metadata.WorldId ?? 0;
		CreatorAPI.UpdateStudioPresence(worldId, active: true);
	}

	public void ShowRuntimeDebugWindows()
	{
		foreach (RuntimeDebugWindow window in _runtimeDebugWindows.Values)
		{
			if (!IsInstanceValid(window)) continue;
			window.Activate();
		}
	}

	public bool ApplyDeviceEmulation(MessageRuntimeDeviceEmulation state)
	{
		_deviceEmulation = state;
		if (!_primaryRuntimeClientProcess.HasValue) return false;
		DebugServer.SetRuntimeDeviceEmulation(_primaryRuntimeClientProcess.Value, state);
		return true;
	}

	private void CloseRuntimeDebugWindows()
	{
		foreach (RuntimeDebugWindow window in _runtimeDebugWindows.Values)
		{
			if (IsInstanceValid(window)) window.QueueFree();
		}
		_runtimeDebugWindows.Clear();
		_primaryRuntimeClientProcess = null;
		_lastRuntimeViewportRect = null;
	}

	private void OnRuntimeDisconnected(int processId)
	{
		if (_runtimeDebugWindows.Remove(processId, out RuntimeDebugWindow? window) && IsInstanceValid(window))
			window.QueueFree();
		if (_primaryRuntimeClientProcess == processId)
		{
			_primaryRuntimeClientProcess = null;
			_lastRuntimeViewportRect = null;
		}
	}

	private void OnRuntimeSnapshotReceived(int processId, MessageRuntimeSnapshot snapshot)
	{
		if (_runtimeDebugWindows.TryGetValue(processId, out RuntimeDebugWindow? window))
			window.ApplySnapshot(snapshot);
	}

	private void OnRuntimeLogReceived(int processId, MessageLogDispatch log)
	{
		if (_runtimeDebugWindows.TryGetValue(processId, out RuntimeDebugWindow? window))
			window.AppendLog(log);

		DebugConsole.Singleton?.NewLog(new LogDispatcher.LogData
		{
			ID = Guid.NewGuid().ToString(),
			LogType = log.LogType,
			LogFrom = log.LogFrom,
			Content = log.Content,
			Source = log.Source,
			SourceLine = log.SourceLine,
		});
	}

	private void OnBeforeQuit()
	{
		long worldId = CurrentSession?.Metadata.WorldId ?? 0;
		_ = CreatorAPI.UpdateStudioPresence(worldId, active: false);

		try
		{
			StopLocalTest();
		}
		catch (Exception ex)
		{
			BV.PrintErr("Error while quitting: ", ex);
		}
		try
		{
			CleanupSessions();
		}
		catch (Exception ex)
		{
			BV.PrintErr("Error while quitting: ", ex);
		}
	}

	private async void OnFilesDropped(string[] files)
	{
		string firstFile = files[0];
		string firstFileExt = firstFile.GetExtension();

		if (firstFileExt == "bvxm" || firstFileExt == "bvmodel" || firstFileExt == "model")
		{
			Interface.ImportModel(firstFile);
		}
		else if (firstFileExt == "bvxl" || firstFileExt == "bvproject")
		{
			await CreateNewSession(firstFile);
		}
		else if (firstFileExt == "bvxw" || firstFileExt == "bvworld")
		{
			Interface.OpenWorldFile(firstFile);
		}
		else if (firstFileExt == "bvanim")
		{
			Interface.OpenAnimationEditor(firstFile);
		}
		else if (firstFileExt == "bvaddon")
		{
			await AddonsManager.InstallAddonFile(firstFile);
			Interface.PopupAlert($"Installed {Path.GetFileName(firstFile)}.", "Addon Installed");
		}
	}

	public override void _Process(double delta)
	{
		_studioPresenceHeartbeatElapsed += delta;
		if (_studioPresenceHeartbeatElapsed >= 30.0)
		{
			_studioPresenceHeartbeatElapsed = 0.0;
			long worldId = CurrentSession?.Metadata.WorldId ?? 0;
			_ = CreatorAPI.UpdateStudioPresence(worldId, active: true);
		}

		if (LocalTestActive)
		{
			foreach (int procID in LocalTestProcesses.ToArray())
			{
				if (!OS.IsProcessRunning(procID))
				{
					LocalTestProcesses.Remove(procID);
				}
			}

			if (LocalTestProcesses.Count == 0)
			{
				CleanupLocalTest();
				LocalTestStopped.Invoke();
				Tabs.Singleton?.RefreshCreatorPresence();
			}
		}
		SyncPrimaryClientViewport(delta);
		SyncEditorPopupStack(delta);
		base._Process(delta);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		base._UnhandledInput(@event);
	}

	private void SyncEditorPopupStack(double delta)
	{
		if (!_primaryRuntimeClientProcess.HasValue && !_creatorPromotedForPopup) return;
		_popupStackSyncElapsed += delta;
		if (_popupStackSyncElapsed < 0.03) return;
		_popupStackSyncElapsed = 0;

		bool embeddedPopupVisible = HasVisibleEmbeddedPopup(Interface);
		if (embeddedPopupVisible == _creatorPromotedForPopup) return;
		_creatorPromotedForPopup = embeddedPopupVisible;
		DisplayServer.WindowSetFlag(
			DisplayServer.WindowFlags.AlwaysOnTop,
			embeddedPopupVisible,
			GetWindow().GetWindowId()
		);
		if (embeddedPopupVisible) GetWindow().GrabFocus();
	}

	private static bool HasVisibleEmbeddedPopup(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is Window window
				&& window.Visible
				&& window.IsEmbedded())
				return true;
			if (HasVisibleEmbeddedPopup(child)) return true;
		}
		return false;
	}

	private void SyncPrimaryClientViewport(double delta)
	{
		if (!_primaryRuntimeClientProcess.HasValue) return;
		if (CreatorSettingsService.Instance.Get<PlayTestPresentationEnum>(CreatorSettingKeys.Creator.PlayTestPresentation) != PlayTestPresentationEnum.Attached) return;

		_runtimeViewportSyncElapsed += delta;
		_runtimeViewportForceSyncElapsed += delta;
		if (_runtimeViewportSyncElapsed < 0.05) return;
		_runtimeViewportSyncElapsed = 0;
		bool forceSync = _runtimeViewportForceSyncElapsed >= 0.5;

		Rect2I? rect = GetCurrentWorldViewportScreenRect();
		bool visible = rect.HasValue && GetWindow().Mode != Window.ModeEnum.Minimized && Tabs.Singleton?.CurrentWorldContainer?.IsVisibleInTree() == true;
		// Do not minimize the runtime when Creator temporarily loses the world tab,
		// focus, or a popup covers it. Resume geometry syncing when it is visible again.
		if (!visible) return;

		Rect2I? effectiveRect = rect ?? _lastRuntimeViewportRect;
		if (!forceSync && rect.HasValue && rect == _lastRuntimeViewportRect && visible == _lastRuntimeViewportVisible) return;
		if (!rect.HasValue && !_lastRuntimeViewportVisible) return;

		if (rect.HasValue) _lastRuntimeViewportRect = rect;
		_lastRuntimeViewportVisible = visible;
		if (effectiveRect.HasValue)
		{
			DebugServer.SetRuntimeViewportRect(_primaryRuntimeClientProcess.Value, effectiveRect.Value, visible);
			_runtimeViewportForceSyncElapsed = 0;
		}
	}

	private Rect2I? GetCurrentWorldViewportScreenRect()
	{
		WorldContainer? worldContainer = Tabs.Singleton?.CurrentWorldContainer;
		if (worldContainer == null || !IsInstanceValid(worldContainer)) return null;

		Rect2 globalRect = worldContainer.GetGlobalRect();
		Vector2 windowPosition = DisplayServer.WindowGetPosition(GetWindow().GetWindowId());
		float scale = GetWindow().ContentScaleFactor;
		Vector2I position = new(
			Mathf.RoundToInt(windowPosition.X + globalRect.Position.X * scale),
			Mathf.RoundToInt(windowPosition.Y + globalRect.Position.Y * scale)
		);
		Vector2I size = new(
			Mathf.Max(320, Mathf.RoundToInt(globalRect.Size.X * scale)),
			Mathf.Max(240, Mathf.RoundToInt(globalRect.Size.Y * scale))
		);
		return new Rect2I(position, size);
	}

	public async Task CreateNewSessionByWorldId(string worldId, bool forceNew = false)
	{
		if (!long.TryParse(worldId, out long parsedWorldId) || parsedWorldId == 0)
		{
			BV.PrintErr("Invalid world id, world id 0 is reserved for local projects.");
			return;
		}

		bool keepOverlayVisible = false;

		try
		{
			BV.Print("Creating new session for world id ", worldId, " (forceNew=", forceNew, ")");
			Interface.LoadOverlay?.SetTitle("Opening world creator");
			Interface.LoadOverlay?.SetStatus("Determining project folder");
			Interface.LoadOverlay?.SetMaxProgress(4);
			Interface.LoadOverlay?.Show();

			// Check previous projects for existing world files
			if (!forceNew)
			{
				ProjectManager.RecentData[] recents = await ProjectManager.GetRecents();
				foreach (ProjectManager.RecentData r in recents)
				{
					// Check if any of the recent projects have a matching world id
					if (r.WorldId == parsedWorldId)
					{
						BV.Print("Found existing project for world id ", worldId, " at ", r.FolderPath);
						try
						{
							// Open the existing project. If it is stale or damaged, remove
							// the recent entry and fall back to downloading a clean copy.
							await CreateNewSession(r.FolderPath);
							keepOverlayVisible = true;
							return;
						}
						catch (Exception ex)
						{
							BV.PrintWarn($"Existing project could not be opened; downloading world again: {ex.Message}");
							await ProjectManager.RemoveFromRecents(r.FolderPath);
							Interface.LoadOverlay?.Show();
						}
					}
				}
			}

			Interface.LoadOverlay?.SetStatus("Downloading world");
			Interface.LoadOverlay?.SetProgress(0);

			// Download world from API
			byte[] worldContent = await CreatorAPI.DownloadWorld(worldId);
			PolyFileTypeEnum fileType = DatamodelLoader.DetermineFileTypeFromBytes(worldContent);
			Interface.LoadOverlay?.SetProgress(1);

			Interface.LoadOverlay?.SetStatus("Loading world bytes");
			World root = Globals.LoadInstance<World>();
			root.WorldID = parsedWorldId;
			bool rootDeleted = false;
			SubViewport? tempViewport = null;

			try
			{
				World3D world3D = new();
				tempViewport = new()
				{
					RenderTargetClearMode = SubViewport.ClearMode.Never,
					RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
					World3D = world3D
				};

				root.SessionType = World.SessionTypeEnum.Creator;
				root.World3D = world3D;

				NetworkService netService = new();
				netService.Attach(root);
				netService.NetworkMode = NetworkService.NetworkModeEnum.Creator;
				netService.IsServer = true;

				AddChild(tempViewport);
				tempViewport.AddChild(root.GDNode);

				root.Root = root;
				root.InitEntry();
				root.Setup();

				await DatamodelLoader.LoadWorldBytes(root, worldContent);
				Interface.LoadOverlay?.SetProgress(2);

				string projectName = string.IsNullOrWhiteSpace(root.Name) ? "World" : root.Name.Trim();
				string safeProjectName = projectName.SanitizeFileName().Trim();
				if (string.IsNullOrWhiteSpace(safeProjectName)) safeProjectName = "World";
				string projectFolderName = $"{safeProjectName}-{parsedWorldId}";
				string projectsRoot = Path.Join(
					System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
					CloudWorldProjectsFolderName
				);
				string projectFolderPath = Path.Join(projectsRoot, projectFolderName);

				bool promptForLocation = CreatorSettingsService.Instance.Get<bool>(
					CreatorSettingKeys.Creator.PromptForWorldProjectLocation
				);
				if (promptForLocation)
				{
					Interface.LoadOverlay?.SetStatus("Choosing project folder");
					Directory.CreateDirectory(projectsRoot);
					string targetPath = await Interface.PromptFolderSelect(new()
					{
						Title = "Select a folder for this world project",
						CurrentDirectory = projectsRoot,
					});

					if (string.IsNullOrWhiteSpace(targetPath)) return;
					projectFolderPath = targetPath;
					if (Directory.Exists(projectFolderPath))
					{
						bool hasExistingContent = Directory.EnumerateFileSystemEntries(projectFolderPath).Any();
						if (hasExistingContent && new DirectoryInfo(projectFolderPath).Name != projectFolderName)
							projectFolderPath = Path.Join(projectFolderPath, projectFolderName);
					}
				}
				else
				{
					Interface.LoadOverlay?.SetStatus("Creating local project");
					BV.Print("Using automatic project folder: ", projectFolderPath);
				}

				Directory.CreateDirectory(projectFolderPath);

				string projectFilePath = Path.Join(projectFolderPath, Globals.ProjectMetaFileName);
				string mainWorldPath = Path.Join(projectFolderPath, "main.bvxw");
				string scriptsPath = Path.Join(projectFolderPath, "scripts");
				Directory.CreateDirectory(scriptsPath);
				Directory.CreateDirectory(Path.Join(scriptsPath, "server"));
				Directory.CreateDirectory(Path.Join(scriptsPath, "client"));
				Directory.CreateDirectory(Path.Join(scriptsPath, "modules"));

				// Restore the entire fetched project before reconciling the
				// locally editable world. Terrain material textures, scripts,
				// models and their index entries must survive the download.
				foreach ((string archivePath, byte[] content) in root.IO.FileStructure)
				{
					string relativePath = archivePath.SanitizePath().TrimStart('/');
					if (string.IsNullOrWhiteSpace(relativePath) ||
						Path.IsPathRooted(relativePath))
					{
						continue;
					}

					string destination = Path.GetFullPath(
						Path.Join(projectFolderPath, relativePath)
					);
					if (!PathUtils.IsPathInsideDirectory(
						destination,
						projectFolderPath))
					{
						throw new InvalidDataException(
							$"Downloaded project entry escapes the project folder: {archivePath}"
						);
					}

					string? destinationDirectory = Path.GetDirectoryName(destination);
					if (!string.IsNullOrWhiteSpace(destinationDirectory))
						Directory.CreateDirectory(destinationDirectory);
					File.WriteAllBytes(destination, content);
				}
				foreach ((string linkedId, string linkedPath) in root.IO.IndexToFile)
				{
					if (linkedId.StartsWith("world_", StringComparison.Ordinal))
						continue;

					string relativePath = linkedPath.SanitizePath().TrimStart('/');
					string assetPath = Path.GetFullPath(
						Path.Join(projectFolderPath, relativePath)
					);
					if (!PathUtils.IsPathInsideDirectory(
						assetPath,
						projectFolderPath) ||
						!File.Exists(assetPath))
					{
						continue;
					}
					PackedFormat.WriteMetaId(
						PackedFormat.GetMetaPath(assetPath),
						linkedId
					);
				}

				CreatorProjectMetadata metadata = new()
				{
					WorldId = parsedWorldId,
					UniverseId = root.UniverseID,
					ProjectName = projectName,
					MainWorld = "main.bvxw",
					IconID = null,
				};

				Interface.LoadOverlay?.SetStatus("Saving project files");
				File.WriteAllText(projectFilePath, System.Text.Json.JsonSerializer.Serialize(metadata, ProjectJSONGenerationContext.Default.CreatorProjectMetadata));
				PolyFormat.SaveWorldToFile(root, mainWorldPath);
				Interface.LoadOverlay?.SetProgress(3);

				Interface.LoadOverlay?.SetStatus("Opening project");
				Interface.LoadOverlay?.SetProgress(4);
				root.ForceDelete();
				rootDeleted = true;
				keepOverlayVisible = true;
				await CreateNewSession(projectFilePath);
			}
			finally
			{
				if (!rootDeleted)
				{
					root.ForceDelete();
				}

				tempViewport?.QueueFree();
			}
		}
		catch (Exception ex)
		{
			BV.PrintErr(ex);
			Interface.PopupAlert(ex.Message, "Error opening world creator");
		}
		finally
		{
			if (!keepOverlayVisible)
			{
				Interface.LoadOverlay?.Hide();
			}
		}
	}

	public async Task CreateNewSession(string projectFilePath = "", World? worldOverride = null)
	{
		bool openedSuccessfully = false;
		string? targetPlace = null;
		projectFilePath = ProjectSettings.GlobalizePath(projectFilePath);
		if (string.IsNullOrWhiteSpace(projectFilePath)
			|| (!File.Exists(projectFilePath) && !Directory.Exists(projectFilePath)))
		{
			throw new FileNotFoundException("Project path does not exist.", projectFilePath);
		}

		string extension = projectFilePath.GetExtension().ToLowerInvariant();
		if (Directory.Exists(projectFilePath) || extension == "bvxw" || extension == "bvworld")
		{
			string originFilePath = projectFilePath;
			if (extension == "bvxw" || extension == "bvworld")
			{
				projectFilePath += "/../";
			}
			string projectFileRoot = Path.GetFullPath(Path.Join(projectFilePath, Globals.ProjectMetaFileName));
			if (!File.Exists(projectFileRoot))
			{
				Interface.PopupAlert("Couldn't find the project file");
				return;
			}

			if (extension == "bvxw" || extension == "bvworld")
			{
				targetPlace = originFilePath;
			}

			projectFilePath = projectFileRoot;
		}

		projectFilePath = projectFilePath.SanitizePath();

		BV.Print("Opening ", projectFilePath);

		string folder = Path.GetFullPath(Path.Combine(projectFilePath, "../")).SanitizePath();
		CreatorSession session = new()
		{
			ProjectFolderPath = folder,
			ProjectFilePath = projectFilePath
		};
		AddChild(session);

		Interface.StatusBar?.SetStatus("Initializing...");

		Interface.LoadOverlay?.SetTitle("Opening project");
		Interface.LoadOverlay?.SetStatus("Initializing");
		Interface.LoadOverlay?.SetMaxProgress(2);
		StartupSplash.Singleton.Close();
		Interface.LoadOverlay?.Show();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		try
		{
			await session.Init();

			Interface.StatusBar?.SetStatus("Opening world...");
			Interface.LoadOverlay?.SetStatus("Opening world");
			Interface.LoadOverlay?.SetProgress(1);

			World? openedWorld = targetPlace != null
				? session.OpenWorld(Path.GetRelativePath(folder, targetPlace).SanitizePath(), worldOverride)
				: session.OpenMainWorld(worldOverride);
			if (openedWorld == null)
			{
				throw new InvalidDataException("The project did not open a world.");
			}

			Sessions.Add(session);
			openedSuccessfully = true;
			await ProjectManager.AddToRecents(folder);

			if (!string.IsNullOrWhiteSpace(PendingModelImportPath))
			{
				string modelPath = PendingModelImportPath;
				PendingModelImportPath = null;
				Interface.ImportModel(modelPath);
			}
		}
		catch (Exception ex)
		{
			BV.PrintErr(ex);
			Sessions.Remove(session);
			session.Dispose();
			Interface.PopupAlert(ex.Message, "Error opening project");
			throw;
		}
		finally
		{
			Interface.LoadOverlay?.Hide();
			Interface.StatusBar?.SetEmpty();
			if (!openedSuccessfully && Sessions.Count == 0)
			{
				StartupSplash.Singleton.Open();
			}
		}
	}

	public static void SaveCurrentFile(out float savingTime)
	{
		savingTime = 0f;

		if (World.Current == null) { CreatorService.Interface.StatusBar?.SetStatus("No current game opened, did not save"); return; }
		if (CurrentSession == null) { CreatorService.Interface.StatusBar?.SetStatus("No session, did not save"); return; }
		string placePath = CurrentSession.GlobalizePath(World.Current.WorldFilePath!);
		var start = Time.GetTicksUsec();

		Interface.LoadOverlay?.SetTitle("Saving world");
		Interface.LoadOverlay?.SetStatus("Saving world");
		Interface.LoadOverlay?.SetMaxProgress(2);
		Interface.LoadOverlay?.Show();

		try
		{
			PolyFormat.SaveWorldToFile(World.Current, placePath);
		}
		catch (Exception ex)
		{
			Interface.PopupAlert(ex.Message, "Error saving file");
			throw;
		}

		Interface.LoadOverlay?.SetStatus("Saving index...");
		Interface.LoadOverlay?.SetProgress(1);

		savingTime = (Time.GetTicksUsec() - start) / 1000f;
		CurrentSession.Save();
		Interface.StatusBar?.SetStatus("Saved to " + placePath + " at " + DateTime.Now.ToString("HH:mm:ss") + " in " + savingTime.ToString("0.00") + " milliseconds");
		Interface.LoadOverlay?.Hide();
	}

	public static void SaveCurrentFile()
	{
		SaveCurrentFile(out _);
	}

	public static void SaveCurrentFileAs()
	{
		if (World.Current == null) { Interface.StatusBar?.SetStatus("No current game opened, did not save"); return; }
		if (CurrentSession == null) { Interface.StatusBar?.SetStatus("No session, did not save"); return; }

		Interface.PromptFileSelect(new()
		{
			Title = "Save as...",
			CurrentDirectory = CurrentSession.ProjectFolderPath,
			Filters = ["*.bvxw;BrickVerse World", "*.bvworld;BrickVerse World"],
			DialogMode = DisplayServer.FileDialogMode.SaveFile,
		}, async paths =>
		{
			try
			{
				string path = paths[0];

				if (!path.EndsWith(".bvxw") && !path.EndsWith(".bvworld"))
				{
					path += ".bvxw";
				}

				if (!PathUtils.IsPathInsideDirectory(path, CurrentSession.ProjectFolderPath))
				{
					Interface.PopupAlert("World file cannot be saved outside of project directory.");
					return;
				}

				PolyFormat.SaveWorldToFile(World.Current, path);
				CurrentSession.RescanFolder();
			}
			catch (Exception ex)
			{
				Interface.PopupAlert(ex.Message, "Error saving file");
				throw;
			}
		});
	}

	public static async void PackCurrentProject()
	{
		if (World.Current == null) { BV.Print("No current game opened, did not save"); return; }
		string? exportPath = ProjectSettings.GlobalizePath("res://test.packed");

		await PackedFormat.PackProjectToFile(World.Current.LinkedSession.ProjectFolderPath, exportPath);
		Interface.StatusBar?.SetStatus("Packed to " + exportPath);
	}

	public static void Redo()
	{
		CurrentGame?.CreatorContext.History.Redo();
	}

	public static void Undo()
	{
		CurrentGame?.CreatorContext.History.Undo();
	}

	public static void OpenScript(Script script)
	{
		if (CurrentSession == null) return;
		if (script.LinkedScript != null)
		{
			string? scriptPath = script.LinkedScript.LinkedPath;
			if (scriptPath == null)
			{
				// TODO: We should have a popup dialog showing invalid references
				Interface.PopupAlert("Script's file reference's invalid, please reinsert the script from the file browser.");
				return;
			}
			BV.Print("Opening ", scriptPath);
			OpenFile(scriptPath);
		}
		else
		{
			Interface.PopupAlert("Script does not have file reference");
		}
	}

	public static async void OpenFile(string path, int lineNumber = 0)
	{
		if (CurrentSession == null) return;
		string pathRelative = path;
		path = CurrentSession.GlobalizePath(path);

		string ext = pathRelative.GetExtension();

		if (ext == "bvxw" || ext == "bvworld")
		{
			CurrentSession.OpenWorld(pathRelative);
			return;
		}
		else if (ext == "model")
		{
			if (World.Current == null) return;
			await CurrentSession.InsertModel(pathRelative, World.Current.Environment);
			return;
		}
		else if (pathRelative == Globals.ProjectInputMapName)
		{
			Interface.OpenInputManager();
			return;
		}

		PreferredEditorEnum userPref = CreatorSettingsService.Instance.Get<PreferredEditorEnum>(CreatorSettingKeys.CodeEditor.PreferredEditor);

		if (userPref == PreferredEditorEnum.BuiltIn)
		{
			FileTypeEnum codeCompletion = FileTypeEnum.Plaintext;
			if (Globals.ScriptFileExtensions.Contains(path.GetExtension()))
			{
				codeCompletion = FileTypeEnum.Lua;
			}

			Tabs.Singleton.Insert(new Tabs.TextEditorTab()
			{
				Session = CurrentSession,
				TargetPath = pathRelative,
				CodeCompletion = codeCompletion,
				Title = pathRelative.GetFile()
			});
			if (lineNumber > 0)
			{
				await Interface.ToSignal(Interface.GetTree(), SceneTree.SignalName.ProcessFrame);
				await Interface.ToSignal(Interface.GetTree(), SceneTree.SignalName.ProcessFrame);
				if (Tabs.Singleton.CurrentControl is TextEditorContainer editor)
					editor.EditorRoot.GoToLine(lineNumber);
			}
			return;
		}
		else if (userPref == PreferredEditorEnum.VSCode)
		{
			CurrentSession.CreateVSCodeConfig();
			// open in vscode
			System.Diagnostics.Process p = new();

			if (OS.HasFeature("macos"))
			{
				p.StartInfo.FileName = "open";
				p.StartInfo.Arguments = $"-a \"Visual Studio Code\" \"{path}\" \"{CurrentSession.ProjectFolderPath}\"";
			}
			else
			{
				p.StartInfo.FileName = "code";
				p.StartInfo.Arguments = $"\"{path}\" \"{CurrentSession.ProjectFolderPath}\"";
			}

			p.StartInfo.UseShellExecute = true;
			p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
			p.Start();
			return;
		}
		else if (userPref == PreferredEditorEnum.Zed)
		{
			// open in zed
			System.Diagnostics.Process p = new();

			if (OS.HasFeature("macos"))
			{
				p.StartInfo.FileName = "open";
				p.StartInfo.Arguments = $"-a Zed \"{path}\" \"{CurrentSession.ProjectFolderPath}\"";
			}
			else
			{
				p.StartInfo.FileName = "zed";
				p.StartInfo.Arguments = $"\"{path}\" \"{CurrentSession.ProjectFolderPath}\"";
			}

			p.StartInfo.UseShellExecute = true;
			p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
			p.Start();
			return;
		}

		OS.ShellOpen(path);
	}

	public override async void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_copy"))
		{
			await Clipboard.SetClipboardToSelected();
		}
		else if (@event.IsActionPressed("ui_paste_into"))
		{
			await Clipboard.PasteClipboard(true);
		}
		else if (@event.IsActionPressed("ui_paste"))
		{
			await Clipboard.PasteClipboard();
		}
		else if (@event.IsActionPressed("stop_playtest"))
		{
			StopLocalTest();
		}
		base._UnhandledKeyInput(@event);
	}

	public override void _Notification(int what)
	{
		if (what == NotificationApplicationFocusIn)
		{
			CurrentSession?.RescanFolder();
		}
	}

	public static ScriptTypeEnum GetScriptTypeFromPath(string filePath)
	{
		string fileName = filePath.GetFile();
		string fileExt = filePath.GetExtension();

		if (!Globals.ScriptFileExtensions.Contains(fileExt))
		{
			return ScriptTypeEnum.Unknown;
		}

		string baseName = fileName.GetBaseName();

		string[] parts = baseName.Split(".");

		string scriptType = "";

		if (parts.Length >= 2)
		{
			scriptType = parts[^1];
		}
		else if (parts.Length == 1)
		{
			scriptType = "";
		}

		return scriptType switch
		{
			"server" => ScriptTypeEnum.Server,
			"client" => ScriptTypeEnum.Client,
			_ => ScriptTypeEnum.Module
		};
	}

	public static string GetScriptNameFromPath(string filePath)
	{
		string fileName = filePath.GetFile();

		// Remove .luau extension
		string baseName = fileName.Replace(".luau", "");

		// Split by dots
		string[] parts = baseName.Split(".");

		string scriptName = "";

		if (parts.Length >= 2)
		{
			scriptName = parts[0];
		}
		else if (parts.Length == 1)
		{
			scriptName = parts[0];
		}

		return scriptName;
	}

	public async void StartLocalTest(bool atCamera = false)
	{
		if (World.Current == null) { BV.PrintErr("World is null, did not test"); return; }
		World game = World.Current;
		CreatorSession session = game.LinkedSession;

		// Check if current session is already open
		if (SessionToLocalTestID.ContainsKey(session))
		{
			StopLocalTest();
			CleanupLocalTest();
			LocalTestStopped.Invoke();
		}

		// Save current project
		SaveCurrentFile();

		string debugID = _localTestIDCounter++.ToString();

		LocalTestIDToSession.Add(debugID, session);
		SessionToLocalTestID.Add(session, debugID);
		await StartLocalTestOnEntry(session.ProjectFolderPath, game.WorldFilePath!, debugID, GD.RandRange(20000, 30000), false, atCamera ? game.CreatorContext.Freelook.Position : null);

		DebugConsole.Singleton.Clear();
		LocalTestStarted.Invoke();
		Tabs.Singleton?.RefreshCreatorPresence();
	}

	public async Task StartLocalTestOnEntry(string projectPath, string entryPath, string debugID, int port, bool isSubplace, Vector3? spawnPos = null)
	{
		await CreatorAPI.GetValidAccessTokenAsync();
		string tempPath = Path.GetTempPath();
		string placeFilePath = tempPath.PathJoin("bv_test_" + new DateTimeOffset(DateTime.Now).Millisecond + ".zip");

		await PackedFormat.PackProjectToFile(projectPath, placeFilePath, Interface.LoadOverlay.CreateProgressReporter("Starting local test..."));
		Interface.LoadOverlay?.Hide();
		StartLocalTestServer(placeFilePath, entryPath, debugID, port, isSubplace, spawnPos);
	}

	private void StartLocalTestServer(string placeFilePath, string entryPath, string debugID, int port, bool isSubplace = false, Vector3? spawnPos = null)
	{
		string exePath = OS.GetExecutablePath();

		List<string> args = ["--log-file", "user://logs/server.log", "-solo", placeFilePath, "-entry", entryPath, "-debug", $"127.0.0.1:{DebugServer.Port}", "-debug-id", debugID, "-port", port.ToString()];

		PlayTestPresentationEnum presentation = CreatorSettingsService.Instance.Get<PlayTestPresentationEnum>(CreatorSettingKeys.Creator.PlayTestPresentation);
		Rect2I? viewportRect = presentation == PlayTestPresentationEnum.Attached ? GetCurrentWorldViewportScreenRect() : null;
		if (!isSubplace && viewportRect.HasValue)
		{
			Rect2I rect = viewportRect.Value;
			args.Add($"-ltrect={rect.Position.X},{rect.Position.Y},{rect.Size.X},{rect.Size.Y}");
		}
		if (!isSubplace) args.Add($"-ltmode={presentation.ToString().ToLowerInvariant()}");

		if (spawnPos != null)
		{
			args.AddRange(["-spawnpos", $"v{(int)spawnPos.Value.X},{(int)spawnPos.Value.Y},{(int)spawnPos.Value.Z}"]);
		}

		if (isSubplace)
		{
			args.AddRange(["-subworld"]);
		}
		else
		{
			args.AddRange(["-nplr", LocalTestPlayerCount.ToString()]);
		}

		if (!OS.HasFeature("serverpov"))
		{
			args.InsertRange(0, ["--headless"]);
		}

		if (Globals.IsInGDEditor)
		{
			args.InsertRange(0, ["--remote-debug", "tcp://127.0.0.1:6007"]);
		}

		// Apply Creator token for loading unapproved assets
		if (CreatorAPI.Token != string.Empty)
		{
			args.InsertRange(0, ["-ctoken", CreatorAPI.Token]);
		}

		args.AddRange("--rendering-method", RenderingDeviceSwitcher.GetCurrentDriverName());

		// Ignore rendering method switcher flag, use the same one as creator's
		args.Add("-rmswignore");

		LocalTestWorlds.Add(placeFilePath);

		int procID = OS.CreateProcess(exePath, [.. args]);
		BV.Print(
			"Starting local play-test server",
			" (debug ID: ", debugID,
			", port: ", port,
			", creator auth: ", CreatorAPI.Token.Length > 0 ? "provided" : "missing",
			")"
		);

		LocalTestProcesses.Add(procID);
	}

	public async void StopLocalTest()
	{
		CloseRuntimeDebugWindows();
		if (!LocalTestActive) return;

		int[] processes = [.. LocalTestProcesses];
		DebugServer.SendTerminateProgram();
		// Runtime instances shut down cooperatively so server scripts receive
		// PlayerRemoved. A short timeout retains the old force-stop guarantee.
		await Globals.Singleton.WaitAsync(0.5f);
		foreach (int item in processes)
		{
			if (OS.IsProcessRunning(item)) OS.Kill(item);
		}
	}

	public static void MigrateCoordinates(World root)
	{
		string worldFilePath = root.WorldFilePath!;
		root.ForceDelete();
		root.LinkedSession.OpenWorld(worldFilePath, migrateCoords: true);
	}

	private void CleanupLocalTest()
	{
		CloseRuntimeDebugWindows();
		foreach (string item in LocalTestWorlds)
		{
			try
			{
				File.Delete(item);
			}
			catch (Exception ex)
			{
				BV.PrintErr(ex);
			}
		}
		LocalTestWorlds.Clear();
		LocalTestIDToSession.Clear();
		SessionToLocalTestID.Clear();
	}

	private static void CleanupSessions()
	{
		foreach (var session in Sessions)
		{
			session.Dispose();
		}
	}
}

[ScriptEnum("CreatorToolMode", IsCreatorOnly = true)]
public enum ToolModeEnum
{
	Select,
	Move,
	Rotate,
	Scale,
	Paint,
	Brush
}

public enum ScriptTypeEnum
{
	Server,
	Client,
	Module,
	Unknown
}

public enum FileTypeEnum
{
	Plaintext,
	Lua
}

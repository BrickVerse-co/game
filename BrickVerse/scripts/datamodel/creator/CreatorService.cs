// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Creator;
using BrickVerse.Creator.Debugger;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.Managers;
using BrickVerse.Creator.UI;
using BrickVerse.Creator.UI.Splashes;
using BrickVerse.Creator.Utils;
using BrickVerse.Formats;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using BrickVerse.Utils;
using BrickVerse.Datamodel.Services;
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
		DebugServer = new();
		DebugServer.Start();

		DisplayServer.WindowSetDropFilesCallback(Callable.From<string[]>(OnFilesDropped));
		base._Ready();
	}

	public override void _ExitTree()
	{
		Globals.BeforeQuit -= OnBeforeQuit;
		base._ExitTree();
	}

	private void OnBeforeQuit()
	{
		try
		{
			StopLocalTest();
		}
		catch (Exception ex)
		{
			PT.PrintErr("Error while quitting: ", ex);
		}
		try
		{
			CleanupSessions();
		}
		catch (Exception ex)
		{
			PT.PrintErr("Error while quitting: ", ex);
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
	}

	public override void _Process(double delta)
	{
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
			}
		}
		base._Process(delta);
	}

	public async Task CreateNewSessionByWorldId(string worldId, bool forceNew = false)
	{
		if (!long.TryParse(worldId, out long parsedWorldId) || parsedWorldId == 0)
		{
			PT.PrintErr("Invalid world id, world id 0 is reserved for local projects.");
			return;
		}

		bool keepOverlayVisible = false;

		try
		{
			PT.Print("Creating new session for world id ", worldId, " (forceNew=", forceNew, ")");
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
						// Open the existing project
						PT.Print("Found existing project for world id ", worldId, " at ", r.FolderPath);
						keepOverlayVisible = true;
						await CreateNewSession(r.FolderPath);
						return;
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
				if (fileType == PolyFileTypeEnum.PolyXML)
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
				}

				await DatamodelLoader.LoadWorldBytes(root, worldContent);
				Interface.LoadOverlay?.SetProgress(2);

				string projectName = string.IsNullOrWhiteSpace(root.Name) ? $"World {parsedWorldId}" : root.Name.Trim();
				string projectFolderName = projectName.SanitizeFileName();

				// Prompt to save to a project folder
				Interface.LoadOverlay?.SetStatus("Choosing project folder");
				string targetPath = await CreatorService.Interface.PromptFolderSelect(new()
				{
					Title = "Select a folder to create the project in",
					CurrentDirectory = ProjectSettings.GlobalizePath("user://projects/"),
				});

				if (string.IsNullOrWhiteSpace(targetPath))
				{
					return;
				}

				string projectFolderPath = targetPath;
				if (Directory.Exists(projectFolderPath))
				{
					bool hasExistingContent = Directory.GetFiles(projectFolderPath).Length != 0 || Directory.GetDirectories(projectFolderPath).Length != 0;
					if (hasExistingContent && new DirectoryInfo(projectFolderPath).Name != projectFolderName)
					{
						projectFolderPath = Path.Join(projectFolderPath, projectFolderName);
					}
				}

				Directory.CreateDirectory(projectFolderPath);

				string projectFilePath = Path.Join(projectFolderPath, Globals.ProjectMetaFileName);
				string mainWorldPath = Path.Join(projectFolderPath, "main.bvxw");
				string scriptsPath = Path.Join(projectFolderPath, "scripts");
				Directory.CreateDirectory(scriptsPath);
				Directory.CreateDirectory(Path.Join(scriptsPath, "server"));
				Directory.CreateDirectory(Path.Join(scriptsPath, "client"));
				Directory.CreateDirectory(Path.Join(scriptsPath, "modules"));

				CreatorProjectMetadata metadata = new()
				{
					WorldId = parsedWorldId,
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
			PT.PrintErr(ex);
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
		string? targetPlace = null;
		projectFilePath = ProjectSettings.GlobalizePath(projectFilePath);

		if (File.GetAttributes(projectFilePath) == FileAttributes.Directory || projectFilePath.GetExtension() == "bvxw" || projectFilePath.GetExtension() == "bvworld")
		{
			string originFilePath = projectFilePath;
			if (projectFilePath.GetExtension() == "bvxw" || projectFilePath.GetExtension() == "bvworld")
			{
				projectFilePath += "/../";
			}
			string projectFileRoot = Path.GetFullPath(Path.Join(projectFilePath, Globals.ProjectMetaFileName));
			if (!File.Exists(projectFileRoot))
			{
				Interface.PopupAlert("Couldn't find the project file");
				return;
			}

			if (originFilePath.GetExtension() == "bvxw" || originFilePath.GetExtension() == "bvworld")
			{
				targetPlace = originFilePath;
			}

			projectFilePath = projectFileRoot;
		}

		projectFilePath = projectFilePath.SanitizePath();

		PT.Print("Opening ", projectFilePath);

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
		Interface.LoadOverlay?.Show();

		try
		{
			await session.Init();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			Interface.PopupAlert(ex.Message, "Error opening project");
			throw;
		}

		Interface.StatusBar?.SetStatus("Opening world...");
		Interface.LoadOverlay?.SetStatus("Opening world");
		Interface.LoadOverlay?.SetProgress(1);

		// Open world
		if (targetPlace != null)
		{
			session.OpenWorld(Path.GetRelativePath(folder, targetPlace).SanitizePath(), worldOverride);
		}
		else
		{
			session.OpenMainWorld(worldOverride);
		}

		Interface.LoadOverlay?.Hide();

		Sessions.Add(session);

		// Add to recents
		await ProjectManager.AddToRecents(folder);

		// Close startup splash on open file
		StartupSplash.Singleton.Close();
		Interface.StatusBar?.SetEmpty();
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
		if (World.Current == null) { PT.Print("No current game opened, did not save"); return; }
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
			PT.Print("Opening ", scriptPath);
			OpenFile(scriptPath);
		}
		else
		{
			Interface.PopupAlert("Script does not have file reference");
		}
	}

	public static async void OpenFile(string path)
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
		if (World.Current == null) { PT.PrintErr("World is null, did not test"); return; }
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
	}

	public async Task StartLocalTestOnEntry(string projectPath, string entryPath, string debugID, int port, bool isSubplace, Vector3? spawnPos = null)
	{
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
		PT.Print("Starting server with args: ", string.Join(" ", args));

		LocalTestProcesses.Add(procID);
	}

	public async void StopLocalTest()
	{
		if (!LocalTestActive) return;
		foreach (int item in LocalTestProcesses)
		{
			OS.Kill(item);
		}
		DebugServer.SendTerminateProgram();
	}

	public static void MigrateCoordinates(World root)
	{
		string worldFilePath = root.WorldFilePath!;
		root.ForceDelete();
		root.LinkedSession.OpenWorld(worldFilePath, migrateCoords: true);
	}

	private void CleanupLocalTest()
	{
		foreach (string item in LocalTestWorlds)
		{
			try
			{
				File.Delete(item);
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
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

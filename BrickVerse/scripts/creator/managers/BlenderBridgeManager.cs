// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Creator.Managers;

/// <summary>Optional live round-trip bridge between EditableMeshPart and Blender.</summary>
public static class BlenderBridgeManager
{
	private static FileSystemWatcher? _watcher;
	private static WeakReference<EditableMeshPart>? _target;
	private static string? _exchangePath;
	private static DateTime _lastImportedWrite;
	private static long _generation;

	public static bool IsRunning => _watcher != null;

	public static bool StartForSelection()
	{
		EditableMeshPart? target = World.Current?.CreatorContext.Selections.GetSelected()
			.OfType<EditableMeshPart>().FirstOrDefault();
		if (target == null)
			return Report("Select an EditableMeshPart before starting the Blender bridge.");
		string? blender = FindBlender();
		if (blender == null)
			return Report("Blender was not found. Install Blender or set the BLENDER_PATH environment variable.");

		Stop();
		string directory = Path.Combine(
			Path.GetTempPath(),
			"BrickVerse",
			"BlenderBridge",
			Guid.NewGuid().ToString("N")
		);
		Directory.CreateDirectory(directory);
		_exchangePath = Path.Combine(directory, "brickverse-edit.obj");
		string scriptPath = Path.Combine(directory, "brickverse_bridge.py");
		EditableMeshObjCodec.Export(target.Geometry, _exchangePath);
		File.WriteAllText(scriptPath, BlenderScript);
		_lastImportedWrite = File.GetLastWriteTimeUtc(_exchangePath);
		_target = new(target);
		_watcher = new(directory, Path.GetFileName(_exchangePath))
		{
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
			EnableRaisingEvents = true,
		};
		_watcher.Changed += OnExchangeChanged;
		Process.Start(
			new ProcessStartInfo
			{
				FileName = blender,
				UseShellExecute = false,
				ArgumentList = { "--python", scriptPath, "--", _exchangePath },
			}
		);
		CreatorService.Interface.StatusBar?.SetStatus(
			"Blender bridge started. Save in Blender to synchronize the mesh."
		);
		return true;
	}

	public static void Stop()
	{
		bool wasRunning = _watcher != null;
		_generation++;
		if (_watcher != null)
		{
			_watcher.EnableRaisingEvents = false;
			_watcher.Changed -= OnExchangeChanged;
			_watcher.Dispose();
		}
		_watcher = null;
		_target = null;
		_exchangePath = null;
		if (wasRunning)
			CreatorService.Interface.StatusBar?.SetStatus("Blender live link stopped.");
	}

	public static bool PushSelection()
	{
		if (_exchangePath == null || _target == null || !_target.TryGetTarget(out EditableMeshPart? target))
			return Report("Start a Blender bridge before pushing changes.");
		EditableMeshObjCodec.Export(target.Geometry, _exchangePath);
		_lastImportedWrite = File.GetLastWriteTimeUtc(_exchangePath);
		CreatorService.Interface.StatusBar?.SetStatus("Pushed the current Creator mesh to Blender.");
		return true;
	}

	public static bool PullNow()
	{
		if (_exchangePath == null || _target == null)
			return Report("Start a Blender bridge before pulling changes.");
		_lastImportedWrite = DateTime.MinValue;
		ImportDeferred(_generation);
		return true;
	}

	public static bool OpenExchangeFolder()
	{
		if (_exchangePath == null)
			return Report("No Blender exchange session is active.");
		OS.ShellOpen(Path.GetDirectoryName(_exchangePath)!);
		return true;
	}

	public static bool UploadSelection()
	{
		EditableMeshPart? target = World.Current?.CreatorContext.Selections.GetSelected()
			.OfType<EditableMeshPart>().FirstOrDefault();
		if (target == null)
			return Report("Select an EditableMeshPart to upload.");
		string directory = Path.Combine(Path.GetTempPath(), "BrickVerse", "Uploads");
		Directory.CreateDirectory(directory);
		string safeName = string.Concat(target.Name.Select(character =>
			Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
		string path = Path.Combine(directory, safeName + ".obj");
		EditableMeshObjCodec.Export(target.Geometry, path);
		CreatorService.Interface.OpenUploadMeshMenu(path);
		return true;
	}

	private static void OnExchangeChanged(object sender, FileSystemEventArgs args)
	{
		long generation = ++_generation;
		Callable.From(() => ImportDeferred(generation)).CallDeferred();
	}

	private static void ImportDeferred(long generation)
	{
		if (generation != _generation || _exchangePath == null || _target == null)
			return;
		if (!_target.TryGetTarget(out EditableMeshPart? target) || !GodotObject.IsInstanceValid(target.GDNode))
		{
			Stop();
			return;
		}
		try
		{
			DateTime write = File.GetLastWriteTimeUtc(_exchangePath);
			if (write <= _lastImportedWrite)
				return;
			_lastImportedWrite = write;
			target.Geometry = EditableMeshObjCodec.Import(_exchangePath);
			World.Current?.CreatorContext.Selections.RefreshProperties();
			CreatorService.Interface.StatusBar?.SetStatus("Synchronized mesh changes from Blender");
		}
		catch (IOException)
		{
			Callable.From(() => ImportDeferred(generation)).CallDeferred();
		}
		catch (Exception error)
		{
			BV.PrintErr("Blender bridge import failed: ", error);
			Report("Blender synchronization failed: " + error.Message);
		}
	}

	private static string? FindBlender()
	{
		string? configured = System.Environment.GetEnvironmentVariable("BLENDER_PATH");
		if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
			return configured;
		string executable = OperatingSystem.IsWindows() ? "blender.exe" : "blender";
		foreach (string directory in (System.Environment.GetEnvironmentVariable("PATH") ?? "")
			.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			string candidate = Path.Combine(directory.Trim(), executable);
			if (File.Exists(candidate))
				return candidate;
		}
		if (OperatingSystem.IsWindows())
		{
			string root = Path.Combine(
				System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
				"Blender Foundation"
			);
			if (Directory.Exists(root))
				return Directory.EnumerateFiles(root, "blender.exe", SearchOption.AllDirectories)
					.OrderDescending().FirstOrDefault();
		}
		return null;
	}

	private static bool Report(string message)
	{
		CreatorService.Interface.StatusBar?.SetStatus(message);
		return false;
	}

	private const string BlenderScript = """
import bpy, os, sys
argv = sys.argv
path = argv[argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
last_mtime = 0.0
syncing = False

def import_exchange():
    global last_mtime, syncing
    syncing = True
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    try:
        bpy.ops.wm.obj_import(filepath=path)
    except Exception:
        bpy.ops.import_scene.obj(filepath=path)
    last_mtime = os.path.getmtime(path)
    syncing = False

import_exchange()

def brickverse_export(_):
    global last_mtime, syncing
    if syncing:
        return
    syncing = True
    temporary = path + ".tmp.obj"
    try:
        bpy.ops.wm.obj_export(filepath=temporary, export_materials=False)
    except Exception:
        bpy.ops.export_scene.obj(filepath=temporary, use_materials=False)
    os.replace(temporary, path)
    last_mtime = os.path.getmtime(path)
    syncing = False

def poll_creator():
    if not os.path.exists(path):
        return 0.5
    modified = os.path.getmtime(path)
    if not syncing and modified > last_mtime + 0.0001:
        import_exchange()
    return 0.5

bpy.app.handlers.save_post.append(brickverse_export)
if hasattr(bpy.app.handlers, "quit_pre"):
    bpy.app.handlers.quit_pre.append(brickverse_export)
bpy.app.timers.register(poll_creator, first_interval=0.5, persistent=True)
""";
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Scripting;
using System;
using System.IO;
using System.Linq;
using Godot;
#if CREATOR
using BrickVerse.Shared;
using BrickVerse.Utils;
#endif
using System.Collections.Generic;

namespace BrickVerse.Datamodel.Services;

[Static("IO")]
[ExplorerExclude]
[SaveIgnore]
public sealed partial class IOService : Instance
{
	private const string CreatorTempPath = "brickverse_creator_temp";
	private static readonly HashSet<string> AllowedExtensions = new(
		new[] { "bvxw", "bvworld", "bvxm", "bvmodel", "model", "json", "txt", "png", "jpg", "jpeg", "webp", "svg", "ogg", "wav", "mp3", "ogv" }
			.Concat(ScriptLanguageRegistry.Definitions.SelectMany(language => language.Extensions)),
		StringComparer.OrdinalIgnoreCase);

	internal Dictionary<string, byte[]> FileStructure = [];
	internal Dictionary<string, string> FileToIndex = [];
	internal Dictionary<string, string> IndexToFile = [];
	internal Dictionary<string, string> TempFileToIndex = [];
	internal Dictionary<string, string> TempIndexToFile = [];
	private static readonly string TempFilePath;

	static IOService()
	{
		TempFilePath = Path.GetFullPath(Path.Join(Path.GetTempPath(), CreatorTempPath));
	}

	[ScriptMethod(Permissions = Scripting.ScriptPermissionFlags.IORead)]
	public byte[]? ReadBytesFromPath(string path)
	{
		string extension = path.GetExtension();
		if (!AllowedExtensions.Contains(extension))
			throw new InvalidOperationException($"Reading .{extension} files is not allowed ({path}).");
#if CREATOR
		if (Root.SessionType == World.SessionTypeEnum.Creator)
		{
			string baseFolder = Root.LinkedSession?.ProjectFolderPath ?? TempFilePath;
			if (path.StartsWith("@temp/"))
			{
				baseFolder = TempFilePath;
			}

			string rp = Path.GetFullPath(Path.Join(baseFolder, path)).SanitizePath();

			if (!PathUtils.IsPathInsideDirectory(rp, baseFolder))
			{
				BV.PrintErr("Tried to access file beyond the project folder. ", path);
				return null;
			}

			if (!File.Exists(rp))
			{
				return null;
			}

			try
			{
				byte[] content = File.ReadAllBytes(rp);
				return content;
			}
			catch (Exception ex)
			{
				BV.PrintErr(ex);
				return null;
			}
		}
#endif

		if (FileStructure.TryGetValue(path, out byte[]? val))
		{
			return val;
		}

		return null;
	}

	[ScriptMethod(Permissions = Scripting.ScriptPermissionFlags.IORead)]
	public string? ReadTextFromPath(string path)
	{
		return ReadBytesFromPath(path)?.GetStringFromUtf8();
	}

	[ScriptMethod(Permissions = Scripting.ScriptPermissionFlags.IOWrite)]
	public void WriteBytesToPath(string path, byte[] bytes)
	{
		string extension = path.GetExtension();
		if (!AllowedExtensions.Contains(extension))
			throw new InvalidOperationException($"Writing .{extension} files is not allowed ({path}).");
#if CREATOR
		if (Root.SessionType == World.SessionTypeEnum.Creator)
		{
			string baseFolder = Root.LinkedSession.ProjectFolderPath;
			if (path.StartsWith("@temp/"))
			{
				baseFolder = TempFilePath;
			}

			string rp = Path.GetFullPath(Path.Join(baseFolder, path)).SanitizePath();

			if (!PathUtils.IsPathInsideDirectory(rp, baseFolder))
			{
				BV.PrintErr("Tried to access file beyond the project folder. ", path);
				return;
			}

			string fileBaseFolder = rp.GetBaseDir();

			if (!Directory.Exists(fileBaseFolder))
			{
				Directory.CreateDirectory(fileBaseFolder);
			}

			File.WriteAllBytes(rp, bytes);
			Root.LinkedSession.QueueRescanFolder();
			return;
		}
#endif

		FileStructure[path] = bytes;
	}

	[ScriptMethod(Permissions = Scripting.ScriptPermissionFlags.IOWrite)]
	public void WriteTextToPath(string path, string txt)
	{
		WriteBytesToPath(path, txt.ToUtf8Buffer());
	}

	[ScriptMethod(Permissions = Scripting.ScriptPermissionFlags.IORead)]
	public string[] ListProjectFiles()
	{
#if CREATOR
		string[] files = Directory.GetFiles(Root.LinkedSession.ProjectFolderPath, "*", SearchOption.AllDirectories);
		List<string> finalFiles = [];
		foreach (string item in files)
		{
			finalFiles.Add(Path.GetRelativePath(Root.LinkedSession.ProjectFolderPath, item).SanitizePath());
		}
		return [.. finalFiles];
#else
		return [];
#endif
	}

	[ScriptMethod(Permissions = Scripting.ScriptPermissionFlags.IORead)]
	public byte[]? ReadBytesFromID(string id)
	{
		string? path = GetPathFromID(id);
		if (path == null) return null;
		return ReadBytesFromPath(path);
	}


	[ScriptMethod(Permissions = Scripting.ScriptPermissionFlags.IORead)]
	public string? GetPathFromID(string indexID)
	{
		if (indexID.StartsWith("temp:"))
			if (TempIndexToFile.TryGetValue(indexID, out string? spath)) return spath;
		if (IndexToFile.TryGetValue(indexID, out string? path)) return path;
		return null;
	}

	public string GetIDFromPath(string path)
	{
		// check temp first
		if (path.StartsWith("@temp"))
			if (TempFileToIndex.TryGetValue(path, out string? sid)) return sid;

		if (FileToIndex.TryGetValue(path, out string? id)) return id;
		string newId = Guid.NewGuid().ToString();

#if CREATOR
		// temporary
		if (path.StartsWith("@temp"))
		{
			newId = "temp:" + newId;
			TempFileToIndex[path] = newId;
			TempIndexToFile[newId] = path;
			return newId;
		}

		// New file index
		if (Root.LinkedSession != null)
		{
			Root.LinkedSession.IndexToFile[newId] = path;
			Root.LinkedSession.SaveFileIndex();
		}
#endif

		// Fallsafe for non opened games
		FileToIndex[path] = newId;
		IndexToFile[newId] = path;

		return newId;
	}

#if CREATOR
	internal (string Path, string Id) ImportTemporaryFile(string sourcePath)
	{
		string extension = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
		if (!AllowedExtensions.Contains(extension))
			throw new InvalidOperationException($"Files with the .{extension} extension cannot be imported.");
		if (!File.Exists(sourcePath))
			throw new FileNotFoundException("The selected file no longer exists.", sourcePath);
		string temporaryPath = $"@temp/{Guid.NewGuid():N}.{extension}";
		string absolutePath = Path.GetFullPath(Path.Join(TempFilePath, temporaryPath));
		if (!PathUtils.IsPathInsideDirectory(absolutePath, TempFilePath))
			throw new InvalidOperationException("Could not create a safe temporary file path.");
		Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
		File.Copy(sourcePath, absolutePath, true);
		return (temporaryPath, GetIDFromPath(temporaryPath));
	}

	internal void DeleteTemporaryFile(string path, string id)
	{
		if (TempIndexToFile.TryGetValue(id, out string? registeredPath) && registeredPath == path)
		{
			TempIndexToFile.Remove(id);
			TempFileToIndex.Remove(path);
		}
		string absolutePath = Path.GetFullPath(Path.Join(TempFilePath, path));
		if (PathUtils.IsPathInsideDirectory(absolutePath, TempFilePath) && File.Exists(absolutePath))
			File.Delete(absolutePath);
	}
#endif
}

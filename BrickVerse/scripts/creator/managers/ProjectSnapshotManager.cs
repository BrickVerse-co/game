// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BrickVerse.Creator.Managers;

/// <summary>Creates atomic, checksum-verified snapshots of complete Creator projects.</summary>
public static class ProjectSnapshotManager
{
	private const string ManifestName = ".snapshot.json";

	public readonly record struct SnapshotInfo(string Path, DateTime CreatedUtc, int FileCount, long TotalBytes, bool Valid);

	public static async Task<SnapshotInfo> CreateAsync(string projectRoot, string snapshotsRoot, Action<string>? writeUnsavedFiles = null)
	{
		projectRoot = Path.GetFullPath(projectRoot);
		snapshotsRoot = Path.GetFullPath(snapshotsRoot);
		Directory.CreateDirectory(snapshotsRoot);
		string name = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss-fff");
		string finalPath = Path.Combine(snapshotsRoot, name);
		string stagingPath = Path.Combine(snapshotsRoot, ".partial-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(stagingPath);

		try
		{
			foreach (string source in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories))
			{
				string fullSource = Path.GetFullPath(source);
				if (IsWithin(fullSource, snapshotsRoot) || IsIgnored(projectRoot, fullSource)) continue;
				string relative = Path.GetRelativePath(projectRoot, fullSource);
				string destination = ResolveInside(stagingPath, relative);
				Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
				await using FileStream input = new(fullSource, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				await input.CopyToAsync(output);
			}

			writeUnsavedFiles?.Invoke(stagingPath);
			SnapshotInfo info = await WriteManifestAsync(stagingPath);
			Directory.Move(stagingPath, finalPath);
			return info with { Path = finalPath };
		}
		catch
		{
			if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true);
			throw;
		}
	}

	public static async Task<SnapshotInfo> ValidateAsync(string snapshotPath)
	{
		snapshotPath = Path.GetFullPath(snapshotPath);
		string manifestPath = Path.Combine(snapshotPath, ManifestName);
		if (!File.Exists(manifestPath)) return new(snapshotPath, Directory.GetCreationTimeUtc(snapshotPath), 0, 0, false);
		JsonNode? root = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath));
		if (root?["files"] is not JsonArray files) return new(snapshotPath, Directory.GetCreationTimeUtc(snapshotPath), 0, 0, false);
		int count = 0;
		long total = 0;
		foreach (JsonNode? node in files)
		{
			string? relative = node?["path"]?.GetValue<string>();
			string? expectedHash = node?["sha256"]?.GetValue<string>();
			long expectedSize = node?["size"]?.GetValue<long>() ?? -1;
			if (string.IsNullOrWhiteSpace(relative) || string.IsNullOrWhiteSpace(expectedHash)) return new(snapshotPath, Directory.GetCreationTimeUtc(snapshotPath), count, total, false);
			string file = ResolveInside(snapshotPath, relative);
			if (!File.Exists(file) || new FileInfo(file).Length != expectedSize) return new(snapshotPath, Directory.GetCreationTimeUtc(snapshotPath), count, total, false);
			string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file)));
			if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) return new(snapshotPath, Directory.GetCreationTimeUtc(snapshotPath), count, total, false);
			count++;
			total += expectedSize;
		}
		DateTime created = DateTime.TryParse(root["createdUtc"]?.GetValue<string>(), out DateTime parsed) ? parsed.ToUniversalTime() : Directory.GetCreationTimeUtc(snapshotPath);
		return new(snapshotPath, created, count, total, true);
	}

	public static void Prune(string snapshotsRoot, int keepCount)
	{
		if (!Directory.Exists(snapshotsRoot)) return;
		keepCount = Math.Max(1, keepCount);
		DirectoryInfo[] snapshots = new DirectoryInfo(snapshotsRoot).EnumerateDirectories()
			.Where(directory => !directory.Name.StartsWith(".partial-", StringComparison.Ordinal))
			.OrderByDescending(directory => directory.CreationTimeUtc)
			.ToArray();
		foreach (DirectoryInfo stale in snapshots.Skip(keepCount)) stale.Delete(recursive: true);
	}

	public static string ResolveInside(string root, string relativePath)
	{
		string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string result = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
		if (!result.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Snapshot path escapes its project root.");
		return result;
	}

	private static async Task<SnapshotInfo> WriteManifestAsync(string snapshotPath)
	{
		JsonArray files = [];
		long total = 0;
		foreach (string file in Directory.EnumerateFiles(snapshotPath, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != ManifestName).Order(StringComparer.OrdinalIgnoreCase))
		{
			byte[] data = await File.ReadAllBytesAsync(file);
			string relative = Path.GetRelativePath(snapshotPath, file).Replace('\\', '/');
			files.Add(new JsonObject { ["path"] = relative, ["size"] = data.LongLength, ["sha256"] = Convert.ToHexString(SHA256.HashData(data)) });
			total += data.LongLength;
		}
		DateTime created = DateTime.UtcNow;
		JsonObject manifest = new() { ["version"] = 1, ["createdUtc"] = created.ToString("O"), ["fileCount"] = files.Count, ["totalBytes"] = total, ["files"] = files };
		await File.WriteAllTextAsync(Path.Combine(snapshotPath, ManifestName), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
		return new(snapshotPath, created, files.Count, total, true);
	}

	private static bool IsIgnored(string projectRoot, string path)
	{
		string relative = Path.GetRelativePath(projectRoot, path).Replace('\\', '/');
		return relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
			|| relative.StartsWith(".bvproject/", StringComparison.OrdinalIgnoreCase)
			|| relative.Contains("/.godot/", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsWithin(string path, string directory)
	{
		string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
	}
}

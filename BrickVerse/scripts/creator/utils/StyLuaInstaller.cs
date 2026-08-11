// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Shared;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BrickVerse.Creator.Utils;

public static class StyLuaInstaller
{
	private const string Version = "2.5.2";
	private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
	private static readonly SemaphoreSlim InstallLock = new(1, 1);
	private static string? _validatedPath;

	public static async Task<string?> EnsureInstalledAsync(CancellationToken cancellationToken = default)
	{
		if (_validatedPath != null && File.Exists(_validatedPath)) return _validatedPath;

		string? pathExecutable = OS.HasFeature("windows") ? FindOnPath("stylua.exe") : FindOnPath("stylua");
		if (pathExecutable != null && await IsUsableAsync(pathExecutable, cancellationToken))
			return _validatedPath = pathExecutable;

		string? assetName = GetAssetName();
		if (assetName == null)
		{
			BV.PrintWarn("StyLua automatic installation is unavailable for this platform/architecture.");
			return null;
		}

		await InstallLock.WaitAsync(cancellationToken);
		try
		{
			string toolsDirectory = ProjectSettings.GlobalizePath("user://tools/stylua/" + Version);
			string executablePath = Path.Combine(toolsDirectory, OS.HasFeature("windows") ? "stylua.exe" : "stylua");
			if (File.Exists(executablePath) && await IsUsableAsync(executablePath, cancellationToken))
				return _validatedPath = executablePath;

			Directory.CreateDirectory(toolsDirectory);
			string archivePath = Path.Combine(toolsDirectory, assetName + ".download");
			string downloadUrl = $"https://github.com/JohnnyMorganz/StyLua/releases/download/v{Version}/{assetName}";

			BV.Print($"Installing StyLua {Version} for the Creator code editor...");
			using HttpResponseMessage response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			response.EnsureSuccessStatusCode();
			await using (FileStream archive = new(archivePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
				await response.Content.CopyToAsync(archive, cancellationToken);

			using (ZipArchive zip = ZipFile.OpenRead(archivePath))
			{
				ZipArchiveEntry? executable = zip.Entries.FirstOrDefault(entry =>
					string.Equals(Path.GetFileName(entry.FullName), Path.GetFileName(executablePath), StringComparison.OrdinalIgnoreCase));
				if (executable == null) throw new InvalidDataException("The StyLua release did not contain its executable.");
				executable.ExtractToFile(executablePath, true);
			}
			File.Delete(archivePath);

			if (!OperatingSystem.IsWindows())
			{
				File.SetUnixFileMode(executablePath,
					UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
					UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
					UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
			}

			if (!await IsUsableAsync(executablePath, cancellationToken))
				throw new InvalidDataException("The downloaded StyLua executable failed validation.");

			BV.Print($"StyLua {Version} installed successfully.");
			return _validatedPath = executablePath;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			BV.PrintWarn("Could not install StyLua: ", ex.Message);
			return null;
		}
		finally
		{
			InstallLock.Release();
		}
	}

	private static string? GetAssetName()
	{
		string architecture = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "x86_64",
			Architecture.Arm64 => "aarch64",
			_ => string.Empty,
		};
		if (architecture.Length == 0) return null;

		if (OS.HasFeature("windows") && architecture == "x86_64") return "stylua-windows-x86_64.zip";
		if (OS.HasFeature("macos")) return $"stylua-macos-{architecture}.zip";
		if (OS.HasFeature("linux")) return $"stylua-linux-{architecture}.zip";
		return null;
	}

	private static string? FindOnPath(string executable)
	{
		foreach (string directory in (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
		{
			if (string.IsNullOrWhiteSpace(directory)) continue;
			string candidate = Path.Combine(directory.Trim(), executable);
			if (File.Exists(candidate)) return candidate;
		}
		return null;
	}

	private static async Task<bool> IsUsableAsync(string executablePath, CancellationToken cancellationToken)
	{
		try
		{
			using Process process = new() { StartInfo = new ProcessStartInfo
			{
				FileName = executablePath,
				Arguments = "--version",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			} };
			if (!process.Start()) return false;
			await process.WaitForExitAsync(cancellationToken);
			return process.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}
}

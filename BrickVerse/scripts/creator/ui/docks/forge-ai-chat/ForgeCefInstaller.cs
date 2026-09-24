// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace BrickVerse.Creator.UI;

internal static class ForgeCefInstaller
{
	private const string Version = "0.20.0";
	private const string ArchiveUrl =
		"https://github.com/Lecrapouille/gdcef/releases/download/v0.20.0-godot4/gdcef-0.20.0_godot-4.5.zip";
	private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
	private static readonly SemaphoreSlim InstallLock = new(1, 1);

	public static async Task<string> EnsureInstalledAsync(
		Action<double?> reportProgress,
		CancellationToken cancellationToken
	)
	{
		await InstallLock.WaitAsync(cancellationToken);
		try
		{
			string platform = GetPlatformFolder();
			string installRoot = ProjectSettings.GlobalizePath($"user://forge/gdcef/{Version}");
			string manifestPath = Path.Combine(installRoot, "gdcef.gdextension");
			if (IsComplete(installRoot, platform))
				return manifestPath;

			string stagingRoot = installRoot + ".installing";
			string archivePath = Path.Combine(Path.GetTempPath(), $"brickverse-gdcef-{Version}.zip");
			if (Directory.Exists(stagingRoot))
				Directory.Delete(stagingRoot, true);
			Directory.CreateDirectory(stagingRoot);

			try
			{
				using System.Net.Http.HttpResponseMessage response = await Http.GetAsync(
					ArchiveUrl,
					HttpCompletionOption.ResponseHeadersRead,
					cancellationToken
				);
				response.EnsureSuccessStatusCode();
				long? length = response.Content.Headers.ContentLength;
				await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken))
				await using (FileStream output = new(archivePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
				{
					byte[] buffer = new byte[1024 * 1024];
					long received = 0;
					int read;
					while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
					{
						await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
						received += read;
						reportProgress(length > 0 ? (double)received / length.Value : null);
					}
				}

				ExtractCurrentPlatform(archivePath, stagingRoot, platform);
				WriteManifest(stagingRoot, platform);
				MakeExecutablesRunnable(stagingRoot, platform);
				if (!IsComplete(stagingRoot, platform))
					throw new InvalidDataException("The downloaded gdCEF runtime is incomplete.");

				if (Directory.Exists(installRoot))
					Directory.Delete(installRoot, true);
				Directory.Move(stagingRoot, installRoot);
				return manifestPath;
			}
			finally
			{
				if (File.Exists(archivePath)) File.Delete(archivePath);
				if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
			}
		}
		finally
		{
			InstallLock.Release();
		}
	}

	private static void ExtractCurrentPlatform(string archivePath, string destination, string platform)
	{
		using ZipArchive archive = ZipFile.OpenRead(archivePath);
		string marker = $"cef_artifacts/{platform}/";
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			int markerIndex = entry.FullName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
			if (markerIndex < 0 || entry.FullName.EndsWith('/')) continue;
			string relative = entry.FullName[(markerIndex + "cef_artifacts/".Length)..];
			string target = Path.GetFullPath(Path.Combine(destination, relative));
			if (!target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
				throw new InvalidDataException("The gdCEF archive contains an invalid path.");
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			entry.ExtractToFile(target, true);
		}
	}

	private static void WriteManifest(string root, string platform)
	{
		string library = platform switch
		{
			"windows" => "libgdcef.dll",
			"linux" => "libgdcef.so",
			"macos" => "libgdcef.dylib",
			_ => throw new PlatformNotSupportedException(),
		};
		string feature = platform == "windows" ? "windows.x86_64" : platform == "linux" ? "linux.x86_64" : "macos";
		string libraryPath = Path.Combine(root, platform, library).Replace('\\', '/');
		string dependencies = platform switch
		{
			"windows" => $"\n[dependencies]\nwindows.x86_64 = {{\"{Path.Combine(root, platform, "libcef.dll").Replace('\\', '/')}\": \"\"}}\n",
			"linux" => $"\n[dependencies]\nlinux.x86_64 = {{\"{Path.Combine(root, platform, "libcef.so").Replace('\\', '/')}\": \"\"}}\n",
			_ => string.Empty,
		};
		File.WriteAllText(
			Path.Combine(root, "gdcef.gdextension"),
			$"[configuration]\nentry_symbol = \"gdcef_library_init\"\ncompatibility_minimum = 4.2\n\n[libraries]\n{feature}.debug = \"{libraryPath}\"\n{feature}.release = \"{libraryPath}\"\n{dependencies}"
		);
	}

	private static bool IsComplete(string root, string platform)
	{
		string platformRoot = Path.Combine(root, platform);
		return File.Exists(Path.Combine(root, "gdcef.gdextension"))
			&& File.Exists(Path.Combine(platformRoot, platform == "windows" ? "libgdcef.dll" : platform == "linux" ? "libgdcef.so" : "libgdcef.dylib"))
			&& (platform == "macos"
				? Directory.Exists(Path.Combine(platformRoot, "gdCefRenderProcess.app"))
				: File.Exists(Path.Combine(platformRoot, platform == "windows" ? "gdCefRenderProcess.exe" : "gdCefRenderProcess")));
	}

	private static string GetPlatformFolder()
	{
		if (OS.GetName() == "macOS" && RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
			throw new PlatformNotSupportedException("Forge's embedded browser currently requires an Apple Silicon Mac.");
		return OS.GetName() switch
		{
			"Windows" => "windows",
			"Linux" or "FreeBSD" or "NetBSD" or "OpenBSD" or "BSD" => "linux",
			"macOS" => "macos",
			_ => throw new PlatformNotSupportedException("Forge's embedded browser supports Windows, Linux, and macOS."),
		};
	}

	private static void MakeExecutablesRunnable(string root, string platform)
	{
		if (OperatingSystem.IsWindows() || platform == "windows") return;
		foreach (string file in Directory.EnumerateFiles(Path.Combine(root, platform), "*", SearchOption.AllDirectories))
		{
			if (platform == "linux" && !file.EndsWith(".so", StringComparison.Ordinal) && Path.GetFileName(file) != "gdCefRenderProcess") continue;
			if (platform == "macos" && !file.Contains($"{Path.DirectorySeparatorChar}Contents{Path.DirectorySeparatorChar}MacOS{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
			File.SetUnixFileMode(file, File.GetUnixFileMode(file) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
		}
	}
}

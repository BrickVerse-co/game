// (c) 2026 Meta Games LLC. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Microsoft.Win32;
using Environment = System.Environment;
using FileAccessMode = System.IO.FileAccess;
using FileModeType = System.IO.FileMode;
using FileShareMode = System.IO.FileShare;
using HttpClient = System.Net.Http.HttpClient;
using HttpCompletionOption = System.Net.Http.HttpCompletionOption;
using HttpResponseMessage = System.Net.Http.HttpResponseMessage;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using System.Runtime.Versioning;

public partial class BrickVerseLauncherInstaller : Node
{
	[Signal]
	public delegate void InstallStartedEventHandler();

	[Signal]
	public delegate void DownloadProgressChangedEventHandler(float progress);

	[Signal]
	public delegate void InstallCompletedEventHandler();

	[Signal]
	public delegate void InstallFailedEventHandler(string reason);

	[ExportGroup("GitHub")]
	[Export]
	public string GitHubOwner { get; set; } = "BrickVerse-co";

	[Export]
	public string GitHubRepository { get; set; } = "launcher";

	[Export]
	public bool AllowPrereleases { get; set; } = false;

	[ExportGroup("Asset Matching")]
	[Export]
	public string WindowsAssetContains { get; set; } = "setup";

	[Export]
	public string MacAssetContains { get; set; } = "mac";

	[Export]
	public string LinuxAssetContains { get; set; } = "appimage";

	[ExportGroup("Behavior")]
	[Export]
	public bool InstallOnStartup { get; set; } = true;

	[Export]
	public bool SkipInEditor { get; set; } = true;

	[Export]
	public bool DeleteInstallerAfterInstall { get; set; } = true;

	[Export]
	public bool VerifyProtocolAfterInstall { get; set; } = true;

	[Export]
	public int DownloadTimeoutSeconds { get; set; } = 300;

	private static readonly HttpClient HttpClient = CreateHttpClient();

	private CancellationTokenSource? _installationCancellation;

	public override async void _Ready()
	{
		if (!InstallOnStartup)
			return;

		if (SkipInEditor && Engine.IsEditorHint())
			return;

		await EnsureInstalledAsync();
	}

	public override void _ExitTree()
	{
		_installationCancellation?.Cancel();
		_installationCancellation?.Dispose();
	}

	public async Task<bool> EnsureInstalledAsync()
	{
		if (IsLauncherInstalled())
		{
			GD.Print("BrickVerse Launcher is already installed.");
			return true;
		}

		_installationCancellation?.Cancel();
		_installationCancellation?.Dispose();

		_installationCancellation = new CancellationTokenSource(
			TimeSpan.FromSeconds(Math.Max(30, DownloadTimeoutSeconds))
		);

		CancellationToken cancellationToken = _installationCancellation.Token;

		try
		{
			EmitSignal(SignalName.InstallStarted);
			EmitSignal(SignalName.DownloadProgressChanged, 0.0f);

			ReleaseAsset asset = await GetReleaseAssetAsync(cancellationToken);

			GD.Print($"Downloading BrickVerse Launcher asset: {asset.Name}");

			string downloadedPath = await DownloadAssetAsync(asset, cancellationToken);

			try
			{
				EmitSignal(SignalName.DownloadProgressChanged, 1.0f);

				bool installed = await InstallDownloadedAssetAsync(
					downloadedPath,
					cancellationToken
				);

				if (!installed)
					return Fail("The launcher installer returned an error.");

				if (VerifyProtocolAfterInstall)
					await WaitForProtocolRegistrationAsync(cancellationToken);

				EmitSignal(SignalName.InstallCompleted);
				GD.Print("BrickVerse Launcher installed successfully.");

				return true;
			}
			finally
			{
				if (DeleteInstallerAfterInstall)
					TryDelete(downloadedPath);
			}
		}
		catch (OperationCanceledException)
		{
			return Fail("Launcher installation was cancelled or timed out.");
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			return Fail(exception.Message);
		}
	}

	public void CancelInstallation()
	{
		_installationCancellation?.Cancel();
	}

	public bool IsLauncherInstalled()
	{
		if (OperatingSystem.IsWindows())
			return IsInstalledOnWindows();

		if (OperatingSystem.IsMacOS())
			return IsInstalledOnMac();

		if (OperatingSystem.IsLinux())
			return IsInstalledOnLinux();

		return false;
	}

	private async Task<ReleaseAsset> GetReleaseAssetAsync(CancellationToken cancellationToken)
	{
		string endpoint = AllowPrereleases
			? $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepository}/releases"
			: $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepository}/releases/latest";

		using HttpResponseMessage response = await HttpClient.GetAsync(endpoint, cancellationToken);

		response.EnsureSuccessStatusCode();

		await using Stream responseStream = await response.Content.ReadAsStreamAsync(
			cancellationToken
		);

		using JsonDocument document = await JsonDocument.ParseAsync(
			responseStream,
			cancellationToken: cancellationToken
		);

		JsonElement release = SelectRelease(document.RootElement);

		if (!release.TryGetProperty("assets", out JsonElement assetsElement))
			throw new InvalidDataException("The GitHub release does not contain an assets array.");

		List<ReleaseAsset> assets = new();

		foreach (JsonElement assetElement in assetsElement.EnumerateArray())
		{
			string name = assetElement.GetProperty("name").GetString() ?? string.Empty;

			string downloadUrl =
				assetElement.GetProperty("browser_download_url").GetString() ?? string.Empty;

			long size = assetElement.TryGetProperty("size", out JsonElement sizeElement)
				? sizeElement.GetInt64()
				: 0;

			if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(downloadUrl))
			{
				assets.Add(new ReleaseAsset(name, downloadUrl, size));
			}
		}

		ReleaseAsset? selectedAsset = SelectPlatformAsset(assets);

		if (selectedAsset is null)
		{
			string availableAssets = string.Join(", ", assets.Select(asset => asset.Name));

			throw new FileNotFoundException(
				$"No compatible launcher asset was found for {OS.GetName()}. "
					+ $"Available assets: {availableAssets}"
			);
		}

		return selectedAsset;
	}

	private JsonElement SelectRelease(JsonElement root)
	{
		if (root.ValueKind == JsonValueKind.Object)
			return root;

		if (root.ValueKind != JsonValueKind.Array)
			throw new InvalidDataException("GitHub returned an unexpected releases response.");

		foreach (JsonElement release in root.EnumerateArray())
		{
			bool draft =
				release.TryGetProperty("draft", out JsonElement draftElement)
				&& draftElement.GetBoolean();

			bool prerelease =
				release.TryGetProperty("prerelease", out JsonElement prereleaseElement)
				&& prereleaseElement.GetBoolean();

			if (!draft && (AllowPrereleases || !prerelease))
				return release;
		}

		throw new InvalidDataException("No suitable published GitHub release was found.");
	}

	private ReleaseAsset? SelectPlatformAsset(IReadOnlyCollection<ReleaseAsset> assets)
	{
		return OS.GetName() switch
		{
			"Windows" => FindAsset(assets, WindowsAssetContains, new[] { ".exe" }),

			"macOS" => FindAsset(assets, MacAssetContains, new[] { ".pkg", ".dmg", ".zip" }),

			"Linux" => FindAsset(
				assets,
				LinuxAssetContains,
				new[] { ".AppImage", ".appimage", ".deb" }
			),

			_ => throw new PlatformNotSupportedException($"Unsupported platform: {OS.GetName()}"),
		};
	}

	private static ReleaseAsset? FindAsset(
		IEnumerable<ReleaseAsset> assets,
		string preferredText,
		IReadOnlyList<string> extensions
	)
	{
		IEnumerable<ReleaseAsset> platformAssets = assets.Where(asset =>
			extensions.Any(extension =>
				asset.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
			)
		);

		if (!string.IsNullOrWhiteSpace(preferredText))
		{
			ReleaseAsset? preferredAsset = platformAssets.FirstOrDefault(asset =>
				asset.Name.Contains(preferredText, StringComparison.OrdinalIgnoreCase)
			);

			if (preferredAsset is not null)
				return preferredAsset;
		}

		return platformAssets.FirstOrDefault();
	}

	private async Task<string> DownloadAssetAsync(
		ReleaseAsset asset,
		CancellationToken cancellationToken
	)
	{
		string downloadDirectory = Path.Combine(OS.GetUserDataDir(), "launcher-download");

		Directory.CreateDirectory(downloadDirectory);

		string destinationPath = Path.Combine(downloadDirectory, SanitizeFilename(asset.Name));

		using HttpResponseMessage response = await HttpClient.GetAsync(
			asset.DownloadUrl,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken
		);

		response.EnsureSuccessStatusCode();

		long totalBytes = response.Content.Headers.ContentLength ?? asset.Size;

		await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);

		await using FileStream destination = new(
			destinationPath,
			FileModeType.Create,
			FileAccessMode.Write,
			FileShareMode.None,
			81920,
			useAsync: true
		);

		byte[] buffer = new byte[81920];
		long downloadedBytes = 0;

		while (true)
		{
			int bytesRead = await source.ReadAsync(
				buffer.AsMemory(0, buffer.Length),
				cancellationToken
			);

			if (bytesRead == 0)
				break;

			await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

			downloadedBytes += bytesRead;

			if (totalBytes > 0)
			{
				float progress = Math.Clamp((float)downloadedBytes / totalBytes, 0.0f, 1.0f);

				CallDeferred(MethodName.EmitDownloadProgress, progress);
			}
		}

		await destination.FlushAsync(cancellationToken);

		return destinationPath;
	}

	private void EmitDownloadProgress(float progress)
	{
		EmitSignal(SignalName.DownloadProgressChanged, progress);
	}

	private async Task<bool> InstallDownloadedAssetAsync(
		string downloadedPath,
		CancellationToken cancellationToken
	)
	{
		if (OperatingSystem.IsWindows())
		{
			return await InstallWindowsAsync(
				downloadedPath,
				cancellationToken
			);
		}

		if (OperatingSystem.IsMacOS())
		{
			return await InstallMacAsync(
				downloadedPath,
				cancellationToken
			);
		}

		if (OperatingSystem.IsLinux())
		{
			return await InstallLinuxAsync(
				downloadedPath,
				cancellationToken
			);
		}

		throw new PlatformNotSupportedException(
			"BrickVerse Launcher installation is unsupported on this platform."
		);
	}

	[SupportedOSPlatform("windows")]
	private static async Task<bool> InstallWindowsAsync(
			string installerPath,
			CancellationToken cancellationToken
		)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = installerPath,

			// electron-builder NSIS silent install.
			Arguments = "/S",

			UseShellExecute = true,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
		};

		return await RunProcessAsync(startInfo, cancellationToken);
	}

	private static async Task<bool> InstallMacAsync(
		string installerPath,
		CancellationToken cancellationToken
	)
	{
		string extension = Path.GetExtension(installerPath);

		if (extension.Equals(".pkg", StringComparison.OrdinalIgnoreCase))
		{
			/*
			 * Opens the native macOS Installer.
			 *
			 * Installing a system-wide PKG silently generally requires
			 * elevated authorization, so the native installer is safer.
			 */
			var startInfo = new ProcessStartInfo
			{
				FileName = "/usr/bin/open",
				UseShellExecute = false,
			};

			startInfo.ArgumentList.Add(installerPath);

			return await RunProcessAsync(startInfo, cancellationToken, waitForExit: false);
		}

		if (
			extension.Equals(".dmg", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
		)
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = "/usr/bin/open",
				UseShellExecute = false,
			};

			startInfo.ArgumentList.Add(installerPath);

			return await RunProcessAsync(startInfo, cancellationToken, waitForExit: false);
		}

		throw new NotSupportedException($"Unsupported macOS launcher format: {extension}");
	}

	private static async Task<bool> InstallLinuxAsync(
		string downloadedPath,
		CancellationToken cancellationToken
	)
	{
		if (downloadedPath.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
		{
			var openPackage = new ProcessStartInfo
			{
				FileName = "xdg-open",
				UseShellExecute = false,
			};

			openPackage.ArgumentList.Add(downloadedPath);

			return await RunProcessAsync(openPackage, cancellationToken, waitForExit: false);
		}

		if (!downloadedPath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException("Linux launcher asset must be an AppImage or DEB.");
		}

		string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		string installationDirectory = Path.Combine(homeDirectory, ".local", "bin");

		Directory.CreateDirectory(installationDirectory);

		string installedPath = Path.Combine(installationDirectory, "brickverse-launcher.AppImage");

		File.Copy(downloadedPath, installedPath, overwrite: true);

		var chmod = new ProcessStartInfo { FileName = "/bin/chmod", UseShellExecute = false };

		chmod.ArgumentList.Add("+x");
		chmod.ArgumentList.Add(installedPath);

		if (!await RunProcessAsync(chmod, cancellationToken))
			return false;

		/*
		 * Start it once so Electron can register brickverse://.
		 *
		 * The launcher should recognize --register-protocol-only,
		 * register the protocol, and exit without showing its launch UI.
		 */
		var registerProtocol = new ProcessStartInfo
		{
			FileName = installedPath,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		registerProtocol.ArgumentList.Add("--register-protocol-only");

		return await RunProcessAsync(registerProtocol, cancellationToken);
	}

	private async Task WaitForProtocolRegistrationAsync(CancellationToken cancellationToken)
	{
		const int attempts = 20;
		TimeSpan delay = TimeSpan.FromMilliseconds(500);

		for (int attempt = 0; attempt < attempts; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (IsLauncherInstalled())
				return;

			await Task.Delay(delay, cancellationToken);
		}

		throw new InvalidOperationException(
			"The launcher finished installing, but the brickverse:// "
				+ "protocol was not registered."
		);
	}

	[SupportedOSPlatform("windows")]
	private static bool IsInstalledOnWindows()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return false;

		return ProtocolCommandExists(
				Registry.CurrentUser,
				@"Software\Classes\brickverse\shell\open\command"
			)
			|| ProtocolCommandExists(
				Registry.LocalMachine,
				@"Software\Classes\brickverse\shell\open\command"
			);
	}

	[SupportedOSPlatform("windows")]
	private static bool ProtocolCommandExists(RegistryKey registryRoot, string keyPath)
	{
		try
		{
			using RegistryKey? key = registryRoot.OpenSubKey(keyPath);

			string? command = key?.GetValue(null)?.ToString();

			if (string.IsNullOrWhiteSpace(command))
				return false;

			string? executablePath = ExtractExecutablePath(command);

			return executablePath is not null && File.Exists(executablePath);
		}
		catch
		{
			return false;
		}
	}

	private static bool IsInstalledOnMac()
	{
		string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		string[] applicationPaths =
		{
			"/Applications/BrickVerse Launcher.app",
			Path.Combine(homeDirectory, "Applications", "BrickVerse Launcher.app"),
		};

		return applicationPaths.Any(Directory.Exists);
	}

	private static bool IsInstalledOnLinux()
	{
		string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		string[] executablePaths =
		{
			Path.Combine(homeDirectory, ".local", "bin", "brickverse-launcher.AppImage"),
			Path.Combine(homeDirectory, ".local", "bin", "brickverse-launcher"),
			"/usr/bin/brickverse-launcher",
			"/usr/local/bin/brickverse-launcher",
		};

		string[] desktopFilePaths =
		{
			Path.Combine(
				homeDirectory,
				".local",
				"share",
				"applications",
				"brickverse-launcher.desktop"
			),
			"/usr/share/applications/brickverse-launcher.desktop",
		};

		return executablePaths.Any(File.Exists) && desktopFilePaths.Any(File.Exists);
	}

	private static async Task<bool> RunProcessAsync(
		ProcessStartInfo startInfo,
		CancellationToken cancellationToken,
		bool waitForExit = true
	)
	{
		using Process? process = Process.Start(startInfo);

		if (process is null)
			return false;

		if (!waitForExit)
			return true;

		await process.WaitForExitAsync(cancellationToken);

		return process.ExitCode == 0;
	}

	private bool Fail(string reason)
	{
		GD.PushError($"BrickVerse Launcher installation failed: {reason}");
		EmitSignal(SignalName.InstallFailed, reason);
		return false;
	}

	private static string? ExtractExecutablePath(string command)
	{
		command = command.Trim();

		if (command.StartsWith('"'))
		{
			int closingQuote = command.IndexOf('"', 1);

			if (closingQuote > 1)
				return command[1..closingQuote];
		}

		int firstSpace = command.IndexOf(' ');

		return firstSpace > 0 ? command[..firstSpace] : command;
	}

	private static string SanitizeFilename(string filename)
	{
		foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
			filename = filename.Replace(invalidCharacter, '_');

		return filename;
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch (Exception exception)
		{
			GD.PushWarning($"Could not delete launcher download: {exception.Message}");
		}
	}

	private static HttpClient CreateHttpClient()
	{
		var client = new HttpClient();

		client.DefaultRequestHeaders.UserAgent.Add(
			new ProductInfoHeaderValue("BrickVerse-Client", "1.0")
		);

		client.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
		);

		client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

		return client;
	}

	private sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);
}

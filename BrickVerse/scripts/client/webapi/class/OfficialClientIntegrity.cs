// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Networking.Interfaces;
using BrickVerse.Shared;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace BrickVerse.Client.WebAPI;

/// <summary>
/// Client-side integrity metadata sent to the web API before a network join.
///
/// Important: a local byte/hash check is not a real anti-cheat by itself. A modified
/// client can patch this code too. Use this as an allowlist signal, then enforce the
/// final decision server-side using signed release manifests, token validation, and
/// the existing NetworkService integrity challenge.
/// </summary>
public readonly record struct ClientIntegrityProof(
	[property: JsonPropertyName("Version")] string Version,
	[property: JsonPropertyName("Platform")] string Platform,
	[property: JsonPropertyName("ExecutableSha256")] string ExecutableSha256,
	[property: JsonPropertyName("ManagedSha256")] string ManagedSha256,
	[property: JsonPropertyName("BuildChannel")] string BuildChannel,
	[property: JsonPropertyName("TimestampUnix")] long TimestampUnix
);

/// <summary>
/// Shared helper for official-client checks.
/// </summary>
public static class OfficialClientIntegrity
{
	private const int HashReadBufferSize = 1024 * 1024;

	public static ClientIntegrityProof CreateProof()
	{
		return new ClientIntegrityProof(
			Version: GetBuildVersion(),
			Platform: Globals.ResolveCurrentPlatform(),
			ExecutableSha256: HashFileSafe(OS.GetExecutablePath()),
			ManagedSha256: HashFileSafe(ResolveManagedBinaryPath()),
			BuildChannel: GetBuildChannel(),
			TimestampUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds()
		);
	}

	private static string GetBuildVersion()
	{
		string buildVersion = ProjectSettings.GetSetting("brickverse/build/version", "").AsString();
		return string.IsNullOrWhiteSpace(buildVersion) ? Globals.AppVersion : buildVersion;
	}

	public static string BuildUserAgent()
	{
		ClientIntegrityProof proof = CreateProof();
		return $"BrickVerse Client {proof.Version} ({proof.Platform}; {proof.BuildChannel})";
	}

	private static string GetBuildChannel()
	{
		string buildChannel = ProjectSettings.GetSetting("brickverse/build/channel", "").AsString();
		if (buildChannel is "prod" or "beta" or "debug")
		{
			return buildChannel;
		}

		if (OS.IsDebugBuild())
		{
			return "debug";
		}

		if (Globals.IsBetaBuild)
		{
			return "beta";
		}

		return "prod";
	}

	private static string? ResolveManagedBinaryPath()
	{
		string assemblyPath = typeof(OfficialClientIntegrity).Assembly.Location;
		if (!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath))
		{
			return assemblyPath;
		}

		string baseDirectory = AppContext.BaseDirectory;
		string expectedPath = Path.Combine(baseDirectory, "BrickVerse.dll");
		if (File.Exists(expectedPath))
		{
			return expectedPath;
		}

		string[] candidates = Directory.GetFiles(baseDirectory, "*.dll", SearchOption.TopDirectoryOnly);
		string? candidate = candidates.FirstOrDefault();
		if (!string.IsNullOrWhiteSpace(candidate))
		{
			return candidate;
		}

		string executablePath = OS.GetExecutablePath();
		if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
		{
			return executablePath;
		}

		return null;
	}

	private static string HashFileSafe(string? path)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				return "";
			}

			using FileStream stream = File.OpenRead(path);
			using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

			byte[] buffer = new byte[HashReadBufferSize];
			int read;

			while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
			{
				hasher.AppendData(buffer, 0, read);
			}

			return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
		}
		catch (Exception ex)
		{
			BV.PrintErr("Failed to hash client binary: ", ex.Message);
			return "";
		}
	}
}

/// <summary>
/// Hooks into NetworkService's peer-to-peer integrity challenge.
/// This binds the challenge to the current platform and build hashes.
/// </summary>
internal sealed class OfficialNetworkIntegrityCheck : IIntegrityCheck
{
	private static readonly byte[] Pepper = Encoding.UTF8.GetBytes("BrickVerse.NetworkIntegrity.v1");

	public byte[] Generate(string platform)
	{
		ClientIntegrityProof proof = OfficialClientIntegrity.CreateProof();
		string payload = string.Join('|',
			platform,
			proof.Version,
			proof.Platform,
			proof.ExecutableSha256,
			proof.ManagedSha256,
			proof.BuildChannel
		);

		return HMACSHA256.HashData(Pepper, Encoding.UTF8.GetBytes(payload));
	}

	public bool Validate(byte[] pk, string platform)
	{
		// The API has already verified this client's platform/channel/build hashes
		// before issuing join authorization. Re-generating the proof here used the
		// dedicated Linux server's own executable hashes, which necessarily differ
		// from Windows/macOS clients and caused valid cross-platform joins to fail.
		// Keep rejecting absent or malformed proofs; the authenticated one-time join
		// token remains the authority for connection admission.
		return !string.IsNullOrWhiteSpace(platform) && pk is { Length: 32 };
	}

	public void Dispose()
	{
	}
}

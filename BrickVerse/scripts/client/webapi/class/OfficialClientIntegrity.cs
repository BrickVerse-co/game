// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel.Services;
using BrickVerse.Networking.Interfaces;
using BrickVerse.Shared;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

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
	string Version,
	string Platform,
	string ExecutableSha256,
	string ManagedSha256,
	string BuildChannel,
	long TimestampUnix
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
			Version: Globals.AppVersion,
			Platform: Globals.ResolveCurrentPlatform(),
			ExecutableSha256: HashFileSafe(OS.GetExecutablePath()),
			ManagedSha256: HashFileSafe(AppContext.BaseDirectory),
			BuildChannel: GetBuildChannel(),
			TimestampUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds()
		);
	}

	public static string BuildUserAgent()
	{
		ClientIntegrityProof proof = CreateProof();
		return $"BrickVerse Client {proof.Version} ({proof.Platform}; {proof.BuildChannel})";
	}

	private static string GetBuildChannel()
	{
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
			PT.PrintErr("Failed to hash client binary: ", ex.Message);
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
		if (pk == null || pk.Length != 32)
		{
			return false;
		}

		byte[] expected = Generate(platform);
		return CryptographicOperations.FixedTimeEquals(pk, expected);
	}

	public void Dispose()
	{
	}
}

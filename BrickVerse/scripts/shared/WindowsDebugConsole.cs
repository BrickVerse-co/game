// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BrickVerse.Shared;

/// <summary>
/// Enables a native Windows console for production GUI exports when explicitly
/// requested. This runs before the startup banner so early Godot/.NET output is visible.
/// </summary>
public static class WindowsDebugConsole
{
	private const uint GenericRead = 0x80000000;
	private const uint GenericWrite = 0x40000000;
	private const uint OpenExisting = 3;
	private const int StdInputHandle = -10;
	private const int StdOutputHandle = -11;
	private const int StdErrorHandle = -12;
	private static bool _initialized;

	public static bool IsEnabled { get; private set; }

	public static void Initialize(IReadOnlyDictionary<string, string> commandLine)
	{
		if (_initialized) return;
		_initialized = true;

		if (!OperatingSystem.IsWindows()) return;
		if (!commandLine.ContainsKey("console") && !MarkerRequestsConsole()) return;

		try
		{
			if (GetConsoleWindow() == IntPtr.Zero && !AllocConsole()) return;
			RedirectStandardStreams();
			Console.OutputEncoding = Encoding.UTF8;
			Console.Title = "BrickVerse Debug Console";
			IsEnabled = true;
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BrickVerse debug console attached.");
		}
		catch (Exception error)
		{
			System.Diagnostics.Debug.WriteLine($"Could not open BrickVerse debug console: {error}");
		}
	}

	private static bool MarkerRequestsConsole()
	{
		foreach (string path in MarkerPaths())
		{
			try
			{
				if (File.Exists(path)
					&& string.Equals(File.ReadAllText(path).Trim(), "true", StringComparison.OrdinalIgnoreCase))
					return true;
			}
			catch
			{
				// A locked or inaccessible optional marker must never prevent startup.
			}
		}

		return false;
	}

	private static IEnumerable<string> MarkerPaths()
	{
		string executableDirectory = AppContext.BaseDirectory;
		yield return Path.Combine(executableDirectory, "launch-console.txt");

		string localPrograms = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"Programs",
			"BrickVerse"
		);
		string canonicalPath = Path.Combine(localPrograms, "launch-console.txt");
		if (!string.Equals(
			Path.GetFullPath(canonicalPath),
			Path.GetFullPath(Path.Combine(executableDirectory, "launch-console.txt")),
			StringComparison.OrdinalIgnoreCase
		))
			yield return canonicalPath;
	}

	private static void RedirectStandardStreams()
	{
		IntPtr output = CreateFileW("CONOUT$", GenericWrite, FileShare.ReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
		if (output != new IntPtr(-1))
		{
			SetStdHandle(StdOutputHandle, output);
			StreamWriter writer = new(new FileStream(new SafeFileHandle(output, ownsHandle: false), FileAccess.Write))
			{
				AutoFlush = true,
			};
			Console.SetOut(writer);
			Console.SetError(writer);
			SetStdHandle(StdErrorHandle, output);
		}

		IntPtr input = CreateFileW("CONIN$", GenericRead, FileShare.ReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
		if (input != new IntPtr(-1))
		{
			SetStdHandle(StdInputHandle, input);
			Console.SetIn(new StreamReader(new FileStream(new SafeFileHandle(input, ownsHandle: false), FileAccess.Read)));
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AllocConsole();

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetConsoleWindow();

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetStdHandle(int standardHandle, IntPtr handle);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateFileW(
		string fileName,
		uint desiredAccess,
		FileShare shareMode,
		IntPtr securityAttributes,
		uint creationDisposition,
		uint flagsAndAttributes,
		IntPtr templateFile
	);
}

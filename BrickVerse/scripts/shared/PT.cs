// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Scripting;
#if CREATOR
using Polytoria.Creator.UI;
#endif
using System;

namespace Polytoria.Shared;

public static class PT
{
	public static int OwnerThreadId { get; private set; }

	static PT()
	{
		OwnerThreadId = System.Environment.CurrentManagedThreadId;
	}

	public static void Print(params object?[] str)
	{
		string result = "";
		foreach (object? s in str)
		{
			result += s?.ToString();
		}
		if (Globals.GDAvailable)
		{
			GD.Print(result);
		}
		else
		{
			Console.WriteLine("[WARN] " + result);
		}
		DispatchLog(new() { Content = result, LogType = LogDispatcher.LogTypeEnum.Info });
	}

	/// <summary>
	/// Verbose printing
	/// </summary>
	/// <param name="str"></param>
	public static void PrintV(params object?[] str)
	{
		string result = "";
		foreach (object? s in str)
		{
			result += s?.ToString();
		}
		if (Globals.GDAvailable)
		{
			GD.Print(result);
		}
		else
		{
			Console.WriteLine("[WARN] " + result);
		}
	}

	public static void PrintWarn(params object?[] str)
	{
		string result = "";
		foreach (object? s in str)
		{
			result += s?.ToString();
		}
		if (Globals.GDAvailable)
		{
			GD.PrintRich($"[color=yellow][WARN] {result}[/color]");
		}
		else
		{
			Console.WriteLine("[WARN] " + result);
		}
		DispatchLog(new() { Content = result, LogType = LogDispatcher.LogTypeEnum.Warning });
	}

	public static void PrintErr(params object?[] str)
	{
		string result = "";
		foreach (object? s in str)
		{
			result += s?.ToString();
		}
		if (Globals.GDAvailable)
		{
			GD.PrintRich($"[color=red][ERROR] {result}[/color]");
			GD.PushError(result);
		}
		else
		{
			Console.WriteLine("[ERROR] " + result);
		}
		DispatchLog(new() { Content = result, LogType = LogDispatcher.LogTypeEnum.Error });
	}

	/// <summary>
	/// Print error verbose
	/// </summary>
	/// <param name="str"></param>
	public static void PrintErrV(params object?[] str)
	{
		string result = "";
		foreach (object? s in str)
		{
			result += s?.ToString();
		}
		if (Globals.GDAvailable)
		{
			GD.PrintRich($"[color=red][ERROR] {result}[/color]");
			GD.PushError(result);
		}
		else
		{
			Console.WriteLine("[ERROR] " + result);
		}
	}

	public static void DispatchLog(LogDispatcher.LogData data)
	{
		try
		{
#if CREATOR
			// TODO: Turn this into an event instead?
			CallOnMainThread(() =>
			{
				DebugConsole.Singleton?.NewLog(data);
			});
#endif
			if (data.LogType != LogDispatcher.LogTypeEnum.Info)
				World.Current?.ScriptService?.Logger.DispatchLog(data);
		}
		catch (Exception ex)
		{
			// Failed to dispatch log
			if (Globals.GDAvailable)
			{
				GD.PrintRich($"[color=red][ERROR] [Log Dispatch] {ex}[/color]");
			}
			else
			{
				Console.WriteLine("[ERROR] [Log Dispatch] " + ex);
			}
		}
	}

	public static bool IsMainThread()
	{
		return System.Environment.CurrentManagedThreadId == OwnerThreadId;
	}

	public static void CallOnMainThread(Action a)
	{
		if (IsMainThread() || !Globals.GDAvailable)
		{
			a();
		}
		else
		{
			Callable.From(a).CallDeferred();
		}
	}

	public static void CallDeferred(Action a)
	{
		if (!Globals.GDAvailable)
		{
			a();
		}
		else
		{
			Callable.From(a).CallDeferred();
		}
	}
}

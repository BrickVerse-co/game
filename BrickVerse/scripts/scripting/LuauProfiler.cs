// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Datamodel;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;

namespace BrickVerse.Scripting;

public static class LuauProfiler
{
	private sealed class Counters
	{
		public readonly object Sync = new();
		public long Calls;
		public double TotalMilliseconds;
		public double MaximumMilliseconds;
		public double LastMilliseconds;
	}

	public readonly record struct Sample(string Name, string Path, long Calls, double TotalMilliseconds, double AverageMilliseconds, double MaximumMilliseconds, double LastMilliseconds);
	private static readonly ConcurrentDictionary<Script, Counters> _scripts = [];

	public static long Timestamp() => Stopwatch.GetTimestamp();

	public static void Record(Script script, long startedAt)
	{
		double elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
		Counters counters = _scripts.GetOrAdd(script, static _ => new());
		lock (counters.Sync)
		{
			counters.Calls++;
			counters.TotalMilliseconds += elapsed;
			counters.LastMilliseconds = elapsed;
			counters.MaximumMilliseconds = Math.Max(counters.MaximumMilliseconds, elapsed);
		}
	}

	public static Sample[] Snapshot() => _scripts
		.Where(static pair => !pair.Key.IsDeleted)
		.Select(static pair =>
		{
			Script script = pair.Key;
			Counters counters = pair.Value;
			lock (counters.Sync)
			{
				return new Sample(script.Name, script.LuaPath, counters.Calls, counters.TotalMilliseconds,
					counters.Calls == 0 ? 0 : counters.TotalMilliseconds / counters.Calls,
					counters.MaximumMilliseconds, counters.LastMilliseconds);
			}
		})
		.OrderByDescending(static sample => sample.TotalMilliseconds)
		.ToArray();

	public static void Remove(Script script) => _scripts.TryRemove(script, out _);
	public static void Reset() => _scripts.Clear();
}

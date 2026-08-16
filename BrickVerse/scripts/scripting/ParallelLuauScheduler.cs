using BrickVerse.Datamodel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BrickVerse.Scripting;

/// <summary>Bounded FIFO worker scheduler. Each Actor is serial; separate Actors may run concurrently.</summary>
internal static class ParallelLuauScheduler
{
	private sealed class DomainQueue
	{
		public readonly object Sync = new();
		public Task Tail = Task.CompletedTask;
		public bool Retired;
		public int Pending;
	}

	private static readonly ConcurrentDictionary<Actor, DomainQueue> Domains = [];
	private static readonly ConditionalWeakTable<Actor, object> RetiredActors = new();
	private static readonly SemaphoreSlim Workers = new(Math.Max(1, System.Environment.ProcessorCount - 1));
	private static long _scheduled;
	private static long _completed;

	internal static int WorkerLimit => Math.Max(1, System.Environment.ProcessorCount - 1);
	internal static long ScheduledCount => Interlocked.Read(ref _scheduled);
	internal static long CompletedCount => Interlocked.Read(ref _completed);
	internal static int DomainCount => Domains.Count;

	internal static Task Schedule(Actor actor, Action action)
	{
		if (RetiredActors.TryGetValue(actor, out _)) return Task.CompletedTask;
		DomainQueue queue = Domains.GetOrAdd(actor, static _ => new());
		lock (queue.Sync)
		{
			if (queue.Retired) return Task.CompletedTask;
			queue.Pending++;
			Interlocked.Increment(ref _scheduled);
			Task previous = queue.Tail;
			queue.Tail = RunQueued(previous, queue, actor, action);
			return queue.Tail;
		}
	}

	private static async Task RunQueued(Task previous, DomainQueue queue, Actor actor, Action action)
	{
		try { await previous.ConfigureAwait(false); } catch { /* A failed callback must not poison the Actor queue. */ }
		await Workers.WaitAsync().ConfigureAwait(false);
		try
		{
			using IDisposable scope = ParallelLuauContext.Enter(actor, true);
			action();
		}
		finally
		{
			Workers.Release();
			Interlocked.Increment(ref _completed);
			lock (queue.Sync) queue.Pending--;
		}
	}

	internal static Task Retire(Actor actor)
	{
		RetiredActors.GetValue(actor, static _ => new object());
		if (!Domains.TryGetValue(actor, out DomainQueue? queue)) return Task.CompletedTask;
		lock (queue.Sync)
		{
			queue.Retired = true;
			return RetireAfter(queue.Tail, actor, queue);
		}
	}

	private static async Task RetireAfter(Task tail, Actor actor, DomainQueue queue)
	{
		try { await tail.ConfigureAwait(false); } catch { }
		Domains.TryRemove(new KeyValuePair<Actor, DomainQueue>(actor, queue));
	}
}

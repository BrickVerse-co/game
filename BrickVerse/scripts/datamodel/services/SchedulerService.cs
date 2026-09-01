// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel.Services;

/// <summary>Runs cancellable delayed and repeating callbacks on the world thread.</summary>
[Static("Scheduler"), ExplorerExclude, SaveIgnore]
public sealed partial class SchedulerService : Instance
{
	private sealed class ScheduledTask
	{
		public required BVCallback Callback;
		public required double Interval;
		public required double Remaining;
		public int RepetitionsLeft;
		public int Iteration;
	}

	private readonly Dictionary<int, ScheduledTask> _tasks = [];
	private readonly Queue<Action> _pendingChanges = [];
	private int _nextHandle = 1;
	private bool _processing;

	[ScriptProperty] public BVSignal<int> TaskCompleted { get; private set; } = new();
	[ScriptProperty] public BVSignal<int> TaskCanceled { get; private set; } = new();
	[ScriptProperty] public int ActiveTaskCount => _tasks.Count;

	public override void Init()
	{
		base.Init();
		SetProcess(true);
	}

	public override void PreDelete()
	{
		_tasks.Clear();
		_pendingChanges.Clear();
		base.PreDelete();
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		_processing = true;
		foreach ((int handle, ScheduledTask task) in _tasks.ToArray())
		{
			task.Remaining -= Math.Max(0, delta);
			if (task.Remaining > 0) continue;

			task.Iteration++;
			task.Callback.InvokeDirect([handle, task.Iteration]);
			if (task.RepetitionsLeft == 1)
			{
				_tasks.Remove(handle);
				TaskCompleted.Invoke(handle);
				continue;
			}

			if (task.RepetitionsLeft > 1) task.RepetitionsLeft--;
			task.Remaining += task.Interval;
			if (task.Remaining <= 0) task.Remaining = task.Interval;
		}
		_processing = false;
		while (_pendingChanges.Count > 0) _pendingChanges.Dequeue().Invoke();
	}

	[ScriptMethod]
	public int Delay(double seconds, BVCallback callback) => CreateTask(seconds, 1, callback);

	[ScriptMethod]
	public int Repeat(double interval, BVCallback callback, int repetitions = -1)
	{
		if (repetitions == 0 || repetitions < -1) throw new ArgumentOutOfRangeException(nameof(repetitions), "Repetitions must be -1 or greater than zero.");
		return CreateTask(interval, repetitions, callback);
	}

	[ScriptMethod]
	public int NextFrame(BVCallback callback) => CreateTask(0, 1, callback);

	[ScriptMethod]
	public bool Cancel(int handle)
	{
		if (!_tasks.ContainsKey(handle)) return false;
		void Remove()
		{
			if (_tasks.Remove(handle)) TaskCanceled.Invoke(handle);
		}
		if (_processing) _pendingChanges.Enqueue(Remove); else Remove();
		return true;
	}

	[ScriptMethod] public bool IsScheduled(int handle) => _tasks.ContainsKey(handle);

	[ScriptMethod]
	public double GetTimeRemaining(int handle) => _tasks.TryGetValue(handle, out ScheduledTask? task) ? Math.Max(0, task.Remaining) : -1;

	[ScriptMethod]
	public void CancelAll()
	{
		foreach (int handle in _tasks.Keys.ToArray()) Cancel(handle);
	}

	private int CreateTask(double seconds, int repetitions, BVCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
		int handle = _nextHandle++;
		if (_nextHandle <= 0) _nextHandle = 1;
		ScheduledTask task = new() { Callback = callback, Interval = Math.Max(seconds, 0.000001), Remaining = seconds, RepetitionsLeft = repetitions };
		if (_processing) _pendingChanges.Enqueue(() => _tasks[handle] = task); else _tasks[handle] = task;
		return handle;
	}
}

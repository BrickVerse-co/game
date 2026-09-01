// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel.Services;

/// <summary>Schedules temporary instances for deterministic, cancellable cleanup.</summary>
[Static("Debris"), ExplorerExclude, SaveIgnore]
public sealed partial class DebrisService : Instance
{
	private readonly Dictionary<Instance, double> _remaining = [];

	[ScriptProperty] public BVSignal<Instance, double> ItemAdded { get; private set; } = new();
	[ScriptProperty] public BVSignal<Instance> ItemCanceled { get; private set; } = new();
	[ScriptProperty] public BVSignal<Instance> ItemExpired { get; private set; } = new();

	[ScriptProperty] public int Count => _remaining.Count;

	public override void Init()
	{
		base.Init();
		SetProcess(true);
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		foreach ((Instance item, double timeLeft) in _remaining.ToArray())
		{
			if (item.IsDeleted)
			{
				_remaining.Remove(item);
				continue;
			}

			double next = timeLeft - Math.Max(0, delta);
			if (next > 0)
			{
				_remaining[item] = next;
				continue;
			}

			_remaining.Remove(item);
			ItemExpired.Invoke(item);
			item.Delete();
		}
	}

	[ScriptMethod]
	public void AddItem(Instance item, double lifetime = 10)
	{
		ValidateItem(item);
		if (!double.IsFinite(lifetime) || lifetime < 0) throw new ArgumentOutOfRangeException(nameof(lifetime));
		_remaining[item] = lifetime;
		ItemAdded.Invoke(item, lifetime);
	}

	[ScriptMethod]
	public bool Cancel(Instance item)
	{
		if (item == null || !_remaining.Remove(item)) return false;
		ItemCanceled.Invoke(item);
		return true;
	}

	[ScriptMethod] public bool IsQueued(Instance item) => item != null && _remaining.ContainsKey(item);

	[ScriptMethod]
	public double GetRemainingLifetime(Instance item) => item != null && _remaining.TryGetValue(item, out double value) ? value : -1;

	[ScriptMethod]
	public Instance[] GetQueuedItems() => [.. _remaining.Keys.Where(item => !item.IsDeleted)];

	[ScriptMethod]
	public void Clear(bool destroyItems = false)
	{
		Instance[] items = [.. _remaining.Keys];
		_remaining.Clear();
		foreach (Instance item in items)
		{
			if (item.IsDeleted) continue;
			if (destroyItems) item.Delete();
			else ItemCanceled.Invoke(item);
		}
	}

	private void ValidateItem(Instance item)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (item.IsDeleted) throw new InvalidOperationException("Cannot schedule a deleted instance.");
		if (item.Root != Root) throw new InvalidOperationException("The instance belongs to another world.");
		if (item.GetType().IsDefined(typeof(StaticAttribute), false)) throw new InvalidOperationException("World services cannot be scheduled for deletion.");
	}
}

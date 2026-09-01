// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel.Services;

/// <summary>Prewarms and reuses cloned DataModel instances for frequently spawned objects.</summary>
[Static("ObjectPool"), ExplorerExclude, SaveIgnore]
public sealed partial class ObjectPoolService : Instance
{
	private sealed class Pool
	{
		public required Instance Template;
		public required int MaximumSize;
		public readonly Queue<Instance> Available = [];
		public readonly HashSet<Instance> Active = [];
	}

	private readonly Dictionary<string, Pool> _pools = new(StringComparer.OrdinalIgnoreCase);

	[ScriptProperty] public BVSignal<string, Instance> Acquired { get; private set; } = new();
	[ScriptProperty] public BVSignal<string, Instance> Released { get; private set; } = new();
	[ScriptProperty] public BVSignal<string> PoolCreated { get; private set; } = new();
	[ScriptProperty] public BVSignal<string> PoolDestroyed { get; private set; } = new();

	public override void PreDelete()
	{
		foreach (Pool pool in _pools.Values)
			foreach (Instance item in pool.Available)
				if (!item.IsDeleted) item.Delete();
		_pools.Clear();
		base.PreDelete();
	}

	[ScriptMethod]
	public void CreatePool(string name, Instance template, int prewarmCount = 0, int maximumSize = 100)
	{
		name = ValidateName(name);
		ArgumentNullException.ThrowIfNull(template);
		if (template.Root != Root || template.IsDeleted) throw new InvalidOperationException("The template must be a live instance in this world.");
		if (template.GetType().IsDefined(typeof(StaticAttribute), false)) throw new InvalidOperationException("Static services cannot be pooled.");
		if (maximumSize < 1) throw new ArgumentOutOfRangeException(nameof(maximumSize));
		if (prewarmCount < 0 || prewarmCount > maximumSize) throw new ArgumentOutOfRangeException(nameof(prewarmCount));
		if (_pools.ContainsKey(name)) throw new InvalidOperationException($"A pool named '{name}' already exists.");

		Pool pool = new() { Template = template, MaximumSize = maximumSize };
		_pools.Add(name, pool);
		for (int i = 0; i < prewarmCount; i++) pool.Available.Enqueue(CreateInactive(pool));
		PoolCreated.Invoke(name);
	}

	[ScriptMethod]
	public Instance Acquire(string name, Instance? parent = null)
	{
		Pool pool = GetPool(name);
		Prune(pool);
		Instance item;
		if (pool.Available.Count > 0) item = pool.Available.Dequeue();
		else
		{
			if (pool.Active.Count >= pool.MaximumSize) throw new InvalidOperationException($"Pool '{name}' has reached its maximum size.");
			item = (Instance)pool.Template.Clone();
		}
		item.IsHidden = false;
		item.Parent = parent ?? Root.Environment;
		pool.Active.Add(item);
		Acquired.Invoke(name, item);
		return item;
	}

	[ScriptMethod]
	public bool Release(string name, Instance item)
	{
		Pool pool = GetPool(name);
		if (item == null || !pool.Active.Remove(item) || item.IsDeleted) return false;
		item.IsHidden = true;
		item.Parent = Root.TemporaryContainer;
		pool.Available.Enqueue(item);
		Released.Invoke(name, item);
		return true;
	}

	[ScriptMethod]
	public void Prewarm(string name, int count)
	{
		Pool pool = GetPool(name);
		Prune(pool);
		if (count < 0 || pool.Active.Count + pool.Available.Count + count > pool.MaximumSize) throw new ArgumentOutOfRangeException(nameof(count));
		for (int i = 0; i < count; i++) pool.Available.Enqueue(CreateInactive(pool));
	}

	[ScriptMethod] public int GetAvailableCount(string name) { Pool pool = GetPool(name); Prune(pool); return pool.Available.Count; }
	[ScriptMethod] public int GetActiveCount(string name) { Pool pool = GetPool(name); Prune(pool); return pool.Active.Count; }
	[ScriptMethod] public string[] GetPoolNames() => [.. _pools.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

	[ScriptMethod]
	public bool DestroyPool(string name, bool destroyActive = false)
	{
		if (!_pools.Remove(name, out Pool? pool)) return false;
		foreach (Instance item in pool.Available) if (!item.IsDeleted) item.Delete();
		if (destroyActive) foreach (Instance item in pool.Active) if (!item.IsDeleted) item.Delete();
		PoolDestroyed.Invoke(name);
		return true;
	}

	private Instance CreateInactive(Pool pool)
	{
		Instance item = (Instance)pool.Template.Clone(Root.TemporaryContainer);
		item.IsHidden = true;
		return item;
	}

	private Pool GetPool(string name) => _pools.TryGetValue(ValidateName(name), out Pool? pool) ? pool : throw new KeyNotFoundException($"Pool '{name}' does not exist.");
	private static string ValidateName(string name)
	{
		string normalized = name?.Trim() ?? "";
		if (normalized.Length is < 1 or > 64) throw new ArgumentException("Pool names must contain 1-64 characters.", nameof(name));
		return normalized;
	}
	private static void Prune(Pool pool)
	{
		pool.Active.RemoveWhere(item => item.IsDeleted);
		if (pool.Available.Any(item => item.IsDeleted))
		{
			Instance[] valid = [.. pool.Available.Where(item => !item.IsDeleted)];
			pool.Available.Clear();
			foreach (Instance item in valid) pool.Available.Enqueue(item);
		}
	}
}

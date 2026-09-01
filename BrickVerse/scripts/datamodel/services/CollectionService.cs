// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel.Services;

/// <summary>Provides world-wide instance tagging, queries, and reactive tag lifecycle events.</summary>
[Static("CollectionService")]
public sealed partial class CollectionService : Instance
{
	[ScriptProperty] public BVSignal<string, Instance> TagAdded { get; private set; } = new();
	[ScriptProperty] public BVSignal<string, Instance> TagRemoved { get; private set; } = new();
	[ScriptProperty] public BVSignal<string, Instance> TaggedInstanceEntered { get; private set; } = new();
	[ScriptProperty] public BVSignal<string, Instance> TaggedInstanceExiting { get; private set; } = new();

	public override void Init()
	{
		Root.InstanceEnteredTree += OnInstanceEntered;
		Root.InstanceExitingTree += OnInstanceExiting;
		base.Init();
	}

	public override void PreDelete()
	{
		Root.InstanceEnteredTree -= OnInstanceEntered;
		Root.InstanceExitingTree -= OnInstanceExiting;
		base.PreDelete();
	}

	[ScriptMethod]
	public Instance[] GetTagged(string tag)
	{
		string normalized = NormalizeTag(tag);
		return Root.GetDescendants().Where(instance => instance.HasTag(normalized)).ToArray();
	}

	[ScriptMethod]
	public string[] GetAllTags() => Root.GetDescendants()
		.SelectMany(instance => instance.Tags)
		.Distinct(StringComparer.Ordinal)
		.OrderBy(tag => tag, StringComparer.Ordinal)
		.ToArray();

	[ScriptMethod]
	public void AddTag(Instance instance, string tag)
	{
		ArgumentNullException.ThrowIfNull(instance);
		if (instance.Root != Root) throw new InvalidOperationException("The instance belongs to another world.");
		instance.AddTag(NormalizeTag(tag));
	}

	[ScriptMethod]
	public void RemoveTag(Instance instance, string tag)
	{
		ArgumentNullException.ThrowIfNull(instance);
		if (instance.Root != Root) throw new InvalidOperationException("The instance belongs to another world.");
		instance.RemoveTag(NormalizeTag(tag));
	}

	[ScriptMethod]
	public bool HasTag(Instance instance, string tag)
	{
		ArgumentNullException.ThrowIfNull(instance);
		return instance.Root == Root && instance.HasTag(NormalizeTag(tag));
	}

	[ScriptMethod]
	public string[] GetTags(Instance instance)
	{
		ArgumentNullException.ThrowIfNull(instance);
		if (instance.Root != Root) return [];
		return [.. instance.Tags];
	}

	internal void NotifyTagAdded(Instance instance, string tag) => TagAdded.Invoke(tag, instance);
	internal void NotifyTagRemoved(Instance instance, string tag) => TagRemoved.Invoke(tag, instance);

	private void OnInstanceEntered(Instance instance)
	{
		foreach (string tag in instance.Tags) TaggedInstanceEntered.Invoke(tag, instance);
	}

	private void OnInstanceExiting(Instance instance)
	{
		foreach (string tag in instance.Tags) TaggedInstanceExiting.Invoke(tag, instance);
	}

	internal static string NormalizeTag(string tag)
	{
		string normalized = tag?.Trim() ?? "";
		if (normalized.Length == 0) throw new ArgumentException("Tags cannot be empty.", nameof(tag));
		if (normalized.Length > 64) throw new ArgumentException("Tags cannot exceed 64 characters.", nameof(tag));
		if (normalized.Any(char.IsControl)) throw new ArgumentException("Tags cannot contain control characters.", nameof(tag));
		return normalized;
	}
}

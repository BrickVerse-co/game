// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Creator.Managers;

/// <summary>Non-destructive, editor-only scene visibility controls.</summary>
public static class SceneVisibilityManager
{
	private static readonly ConditionalWeakTable<World, HashSet<Dynamic>> Hidden = new();

	public static bool HasHiddenObjects =>
		World.Current != null && Hidden.TryGetValue(World.Current, out HashSet<Dynamic>? set) && set.Count > 0;
	public static int GetHiddenCount(World world) => Current(world).Count;

	public static void HideSelection()
	{
		World? world = World.Current;
		if (world == null) return;
		Instance[] selection = [.. world.CreatorContext.Selections.SelectedInstances];
		if (selection.Length == 0) return;
		HashSet<Dynamic> next = Current(world);
		foreach (Dynamic dynamic in SceneDynamics(world))
			if (selection.Any(selected => ReferenceEquals(dynamic, selected) || dynamic.IsDescendantOf(selected)))
				next.Add(dynamic);
		Commit(world, "Hide Selection", next);
		world.CreatorContext.Selections.DeselectAll();
		CreatorService.Interface.StatusBar?.SetStatus("Selection hidden in editor · Shift+H to show all");
	}

	public static void IsolateSelection()
	{
		World? world = World.Current;
		if (world == null) return;
		Instance[] selection = [.. world.CreatorContext.Selections.SelectedInstances];
		if (selection.Length == 0) return;
		HashSet<Dynamic> next = [];
		foreach (Dynamic dynamic in SceneDynamics(world))
		{
			bool keep = selection.Any(selected =>
				ReferenceEquals(dynamic, selected)
				|| dynamic.IsDescendantOf(selected)
				|| selected.IsDescendantOf(dynamic)
			);
			if (!keep) next.Add(dynamic);
		}
		Commit(world, "Isolate Selection", next);
		CreatorService.Interface.StatusBar?.SetStatus("Selection isolated in editor · Shift+H to exit");
	}

	public static void ShowAll()
	{
		World? world = World.Current;
		if (world == null || !HasHiddenObjects) return;
		Commit(world, "Show All Editor Objects", []);
		CreatorService.Interface.StatusBar?.SetStatus("All editor-hidden objects are visible");
	}

	private static void Commit(World world, string title, HashSet<Dynamic> next)
	{
		HashSet<Dynamic> before = Current(world);
		HashSet<Dynamic> after = [.. next.Where(IsUsable)];
		Apply(world, after);
		world.CreatorContext.History.RecordAppliedAction(
			title,
			new BVCallback(_ => Apply(world, after)),
			new BVCallback(_ => Apply(world, before))
		);
	}

	private static HashSet<Dynamic> Current(World world) =>
		Hidden.TryGetValue(world, out HashSet<Dynamic>? hidden)
			? [.. hidden.Where(IsUsable)]
			: [];

	private static void Apply(World world, HashSet<Dynamic> hidden)
	{
		HashSet<Dynamic> clean = [.. hidden.Where(IsUsable)];
		Hidden.Remove(world);
		Hidden.Add(world, clean);
		foreach (Dynamic dynamic in SceneDynamics(world))
			if (GodotObject.IsInstanceValid(dynamic.GDNode3D))
				dynamic.GDNode3D.Visible = !clean.Contains(dynamic) && !dynamic.IsHidden;
	}

	private static IEnumerable<Dynamic> SceneDynamics(World world) =>
		world.Environment.GetDescendants().OfType<Dynamic>().Where(IsUsable);

	private static bool IsUsable(Dynamic dynamic) =>
		dynamic != null && !dynamic.IsDeleted && dynamic.GDNode3D != null;
}

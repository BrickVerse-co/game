// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;

namespace BrickVerse.Creator.Managers;

/// <summary>Hierarchy-aware selection operations shared by menus and Explorer.</summary>
public static class SelectionTools
{
	public static bool SelectDescendants()
	{
		if (!TryGet(out World world, out Instance[] selected)) return false;
		Instance[] matches = selected.SelectMany(item => item.GetDescendants()).Where(IsSelectable).Distinct().ToArray();
		return Replace(world, matches, "descendant");
	}

	public static bool SelectSiblings()
	{
		if (!TryGet(out World world, out Instance[] selected)) return false;
		Instance[] matches = selected.Where(item => item.Parent != null)
			.SelectMany(item => item.Parent!.GetChildren()).Where(IsSelectable).Distinct().ToArray();
		return Replace(world, matches, "sibling");
	}

	public static bool SelectSameClass()
	{
		if (!TryGet(out World world, out Instance[] selected)) return false;
		HashSet<string> classes = selected.Select(item => item.ClassName).ToHashSet();
		Instance[] matches = world.Environment.GetDescendants()
			.Where(item => classes.Contains(item.ClassName) && IsSelectable(item)).ToArray();
		return Replace(world, matches, "matching");
	}

	private static bool TryGet(out World world, out Instance[] selected)
	{
		world = World.Current!;
		selected = world?.CreatorContext.Selections.GetSelected() ?? [];
		if (world != null && selected.Length > 0) return true;
		CreatorService.Interface.StatusBar?.SetStatus("Select one or more objects first");
		return false;
	}

	private static bool Replace(World world, Instance[] matches, string description)
	{
		if (matches.Length == 0)
		{
			CreatorService.Interface.StatusBar?.SetStatus($"No {description} objects found");
			return false;
		}
		CreatorSelections selections = world.CreatorContext.Selections;
		selections.DeselectAll();
		foreach (Instance match in matches) selections.Select(match);
		CreatorService.Interface.StatusBar?.SetStatus($"Selected {matches.Length} {description} object{(matches.Length == 1 ? "" : "s")}");
		return true;
	}

	private static bool IsSelectable(Instance instance) => !instance.IsDeleted && instance.Parent != null;
}

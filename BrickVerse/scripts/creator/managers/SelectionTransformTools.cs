// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Scripting;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Creator.Managers;

public static class SelectionTransformTools
{
	public enum Axis { X, Y, Z }

	public static bool AlignToActive(Axis axis)
	{
		Dynamic[] targets = GetTopLevelSelection();
		if (targets.Length < 2) return ReportRequirement("Select at least two 3D objects to align");
		Vector3 anchor = targets[^1].Position;
		Dictionary<Dynamic, Transform3D> before = Capture(targets);
		foreach (Dynamic target in targets[..^1])
		{
			Vector3 position = target.Position;
			SetAxis(ref position, axis, GetAxis(anchor, axis));
			target.Position = position;
		}
		Record($"Align selection on {axis}", before, Capture(targets));
		return true;
	}

	public static bool Distribute(Axis axis)
	{
		Dynamic[] targets = GetTopLevelSelection();
		if (targets.Length < 3) return ReportRequirement("Select at least three 3D objects to distribute");
		Dynamic[] ordered = targets.OrderBy(target => GetAxis(target.Position, axis)).ToArray();
		float first = GetAxis(ordered[0].Position, axis);
		float last = GetAxis(ordered[^1].Position, axis);
		float step = (last - first) / (ordered.Length - 1);
		Dictionary<Dynamic, Transform3D> before = Capture(targets);
		for (int index = 1; index < ordered.Length - 1; index++)
		{
			Vector3 position = ordered[index].Position;
			SetAxis(ref position, axis, first + step * index);
			ordered[index].Position = position;
		}
		Record($"Distribute selection on {axis}", before, Capture(targets));
		return true;
	}

	private static Dynamic[] GetTopLevelSelection()
	{
		if (World.Current == null) return [];
		HashSet<Instance> selected = [.. World.Current.CreatorContext.Selections.SelectedInstances];
		return selected.OfType<Dynamic>().Where(item =>
		{
			Instance? parent = item.Parent;
			while (parent != null)
			{
				if (selected.Contains(parent)) return false;
				parent = parent.Parent;
			}
			return true;
		}).ToArray();
	}

	private static Dictionary<Dynamic, Transform3D> Capture(IEnumerable<Dynamic> targets) =>
		targets.ToDictionary(target => target, target => target.GetGlobalTransform());

	private static void Record(string title, Dictionary<Dynamic, Transform3D> before, Dictionary<Dynamic, Transform3D> after)
	{
		void Apply(Dictionary<Dynamic, Transform3D> state)
		{
			foreach ((Dynamic target, Transform3D transform) in state)
				if (GodotObject.IsInstanceValid(target.GDNode)) target.SetGlobalTransform(transform);
		}
		World.Current!.CreatorContext.History.RecordAppliedAction(title,
			new BVCallback(_ => Apply(after)), new BVCallback(_ => Apply(before)));
		World.Current.CreatorContext.Selections.RefreshProperties();
	}

	private static bool ReportRequirement(string message)
	{
		CreatorService.Interface.StatusBar?.SetStatus(message);
		return false;
	}

	private static float GetAxis(Vector3 value, Axis axis) => axis switch { Axis.X => value.X, Axis.Y => value.Y, _ => value.Z };
	private static void SetAxis(ref Vector3 value, Axis axis, float component)
	{
		if (axis == Axis.X) value.X = component;
		else if (axis == Axis.Y) value.Y = component;
		else value.Z = component;
	}
}

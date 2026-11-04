// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Scripting;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Creator.Managers;

public static class SelectionTransformTools
{
	private static Transform3D? _copiedTransform;

	public static bool HasCopiedTransform => _copiedTransform.HasValue;
	public enum Axis
	{
		X,
		Y,
		Z,
	}

	public static bool AlignToActive(Axis axis)
	{
		Dynamic[] targets = GetTopLevelSelection();
		if (targets.Length < 2)
			return ReportRequirement("Select at least two 3D objects to align");
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
		if (targets.Length < 3)
			return ReportRequirement("Select at least three 3D objects to distribute");
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

	public static bool ResetLocalTransform()
	{
		Dynamic[] targets = GetTopLevelSelection();
		if (targets.Length == 0) return ReportRequirement("Select at least one 3D object to reset");
		Dictionary<Dynamic, Transform3D> before = Capture(targets);
		foreach (Dynamic target in targets)
		{
			target.LocalPosition = Vector3.Zero;
			target.LocalRotation = Vector3.Zero;
		}
		Record("Reset local transforms", before, Capture(targets));
		return true;
	}

	public static bool SnapSelectionToGrid()
	{
		Dynamic[] targets = GetTopLevelSelection();
		if (targets.Length == 0) return ReportRequirement("Select at least one 3D object to snap");
		float increment = CreatorService.Interface.UserMoveSnapping;
		if (increment <= 0) return ReportRequirement("Move snapping must be greater than zero");
		Dictionary<Dynamic, Transform3D> before = Capture(targets);
		foreach (Dynamic target in targets)
			target.Position = target.Position.Snap(increment);
		Record("Snap selection to grid", before, Capture(targets));
		return true;
	}

	public static bool DropSelectionToSurface()
	{
		World? world = World.Current;
		Dynamic[] targets = GetTopLevelSelection();
		if (world == null || targets.Length == 0)
			return ReportRequirement("Select at least one 3D object to drop");
		Dictionary<Dynamic, Transform3D> before = Capture(targets);
		Instance[] ignore =
		[
			.. targets.SelectMany(target =>
				new Instance[] { target }.Concat(target.GetDescendants())
			),
		];
		int moved = 0;
		foreach (Dynamic target in targets)
		{
			Aabb bounds = target.CalculateBounds();
			Vector3 origin = bounds.GetCenter() + Vector3.Up * 0.05f;
			Datamodel.Environment.RayResult? hit = world.Environment.Raycast(
				origin,
				Vector3.Down,
				10000,
				ignore
			);
			if (hit == null) continue;
			float offset = hit.Value.Position.Y - bounds.Position.Y;
			target.Position += Vector3.Up * offset;
			moved++;
		}
		if (moved == 0) return ReportRequirement("No surface was found below the selection");
		Record("Drop selection to surface", before, Capture(targets));
		CreatorService.Interface.StatusBar?.SetStatus($"Dropped {moved} object{(moved == 1 ? "" : "s")} to surface");
		return true;
	}

	public static bool CopyTransform()
	{
		Dynamic? target = GetTopLevelSelection().LastOrDefault();
		if (target == null) return ReportRequirement("Select a 3D object to copy its transform");
		_copiedTransform = target.GetGlobalTransform();
		CreatorService.Interface.StatusBar?.SetStatus($"Copied transform from {target.Name}");
		return true;
	}

	public static bool PasteTransform()
	{
		Dynamic[] targets = GetTopLevelSelection();
		if (!_copiedTransform.HasValue) return ReportRequirement("Copy a transform first");
		if (targets.Length == 0) return ReportRequirement("Select at least one 3D object to paste to");
		Dictionary<Dynamic, Transform3D> before = Capture(targets);
		foreach (Dynamic target in targets) target.SetGlobalTransform(_copiedTransform.Value);
		Record("Paste transform", before, Capture(targets));
		return true;
	}

	private static Dynamic[] GetTopLevelSelection()
	{
		if (World.Current == null)
			return [];
		HashSet<Instance> selected = [.. World.Current.CreatorContext.Selections.SelectedInstances];
		return selected
			.OfType<Dynamic>()
			.Where(item =>
			{
				Instance? parent = item.Parent;
				while (parent != null)
				{
					if (selected.Contains(parent))
						return false;
					parent = parent.Parent;
				}
				return true;
			})
			.ToArray();
	}

	private static Dictionary<Dynamic, Transform3D> Capture(IEnumerable<Dynamic> targets) =>
		targets.ToDictionary(target => target, target => target.GetGlobalTransform());

	private static void Record(
		string title,
		Dictionary<Dynamic, Transform3D> before,
		Dictionary<Dynamic, Transform3D> after
	)
	{
		void Apply(Dictionary<Dynamic, Transform3D> state)
		{
			foreach ((Dynamic target, Transform3D transform) in state)
				if (GodotObject.IsInstanceValid(target.GDNode))
					target.SetGlobalTransform(transform);
		}
		World.Current!.CreatorContext.History.RecordAppliedAction(
			title,
			new BVCallback(_ => Apply(after)),
			new BVCallback(_ => Apply(before))
		);
		World.Current.CreatorContext.Selections.RefreshProperties();
	}

	private static bool ReportRequirement(string message)
	{
		CreatorService.Interface.StatusBar?.SetStatus(message);
		return false;
	}

	private static float GetAxis(Vector3 value, Axis axis) =>
		axis switch
		{
			Axis.X => value.X,
			Axis.Y => value.Y,
			_ => value.Z,
		};

	private static void SetAxis(ref Vector3 value, Axis axis, float component)
	{
		if (axis == Axis.X)
			value.X = component;
		else if (axis == Axis.Y)
			value.Y = component;
		else
			value.Z = component;
	}
}

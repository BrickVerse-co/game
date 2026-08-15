using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Datamodel;

public enum PathStatus { NotComputed, Success, NoPath }

[Instantiable]
public sealed partial class PathfindingPath : Instance
{
	private Vector3[] _waypoints = [];
	[ScriptProperty] public PathStatus Status { get; private set; } = PathStatus.NotComputed;
	[ScriptProperty] public BVSignal<int> Blocked { get; private set; } = new();
	[ScriptProperty] public BVSignal Unblocked { get; private set; } = new();
	[ScriptMethod] public Vector3[] GetWaypoints() => [.. _waypoints];

	internal void SetResult(Vector3[] waypoints)
	{
		_waypoints = waypoints;
		Status = waypoints.Length > 0 ? PathStatus.Success : PathStatus.NoPath;
		OnPropertyChanged(nameof(Status));
	}

	internal void ReportBlocked(int waypoint) => Blocked.Invoke(waypoint);
}

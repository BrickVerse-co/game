using BrickVerse.Attributes;
using Godot;
using System.Threading.Tasks;

namespace BrickVerse.Datamodel.Services;

[Static("PathfindingService")]
public sealed partial class PathfindingService : Instance
{
	[ScriptMethod]
	public Task<PathfindingPath> ComputeAsync(Vector3 start, Vector3 finish, bool optimize = true)
	{
		PathfindingPath path = Root.New<PathfindingPath>();
		Vector3[] points = NavigationServer3D.MapGetPath(Root.World3D.NavigationMap, start, finish, optimize);
		path.SetResult(points);
		return Task.FromResult(path);
	}

	[ScriptMethod]
	public int CheckOcclusion(PathfindingPath path, int startWaypoint = 0)
	{
		Vector3[] points = path.GetWaypoints();
		for (int i = Mathf.Max(0, startWaypoint); i + 1 < points.Length; i++)
		{
			Datamodel.Environment.RayResult? hit = Root.Environment.Raycast(points[i] + Vector3.Up * 0.5f, points[i + 1] - points[i], points[i].DistanceTo(points[i + 1]));
			if (hit != null) { path.ReportBlocked(i + 1); return i + 1; }
		}
		return -1;
	}
}

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel.Services;

/// <summary>A bounded, iterative solid boolean implementation. Unlike recursive BSP,
/// malformed or highly tessellated creator meshes cannot overflow the process stack.</summary>
internal static class RuntimeVoxelBoolean
{
	private const int LongestAxisCells = 52;
	internal readonly record struct Input(Vector3[] Triangles, bool Subtract);

	internal static Vector3[] Bake(IReadOnlyList<Input> inputs)
	{
		Vector3[] all = [.. inputs.SelectMany(input => input.Triangles)];
		if (all.Length == 0) return [];
		Vector3 min = all[0], max = all[0];
		foreach (Vector3 point in all) { min = min.Min(point); max = max.Max(point); }
		Vector3 extent = max - min; float longest = Mathf.Max(extent.X, Mathf.Max(extent.Y, extent.Z));
		if (longest <= 0.00001f) return [];
		float cell = longest / LongestAxisCells;
		min -= Vector3.One * cell; max += Vector3.One * cell; extent = max - min;
		int nx = Math.Max(1, Mathf.CeilToInt(extent.X / cell));
		int ny = Math.Max(1, Mathf.CeilToInt(extent.Y / cell));
		int nz = Math.Max(1, Mathf.CeilToInt(extent.Z / cell));
		bool[,,] occupied = new bool[nx, ny, nz];
		Input[] positive = [.. inputs.Where(input => !input.Subtract)];
		Input[] negative = [.. inputs.Where(input => input.Subtract)];

		for (int x = 0; x < nx; x++) for (int y = 0; y < ny; y++) for (int z = 0; z < nz; z++)
				{
					Vector3 point = min + new Vector3(x + 0.5f, y + 0.500137f, z + 0.500271f) * cell;
					bool inside = positive.Any(input => Contains(input.Triangles, point));
					if (inside && negative.Any(input => Contains(input.Triangles, point))) inside = false;
					occupied[x, y, z] = inside;
				}

		List<Vector3> result = [];
		for (int x = 0; x < nx; x++) for (int y = 0; y < ny; y++) for (int z = 0; z < nz; z++)
				{
					if (!occupied[x, y, z]) continue;
					Vector3 p = min + new Vector3(x, y, z) * cell;
					if (!At(occupied, x - 1, y, z)) Face(result, p, Vector3.Back * cell, Vector3.Up * cell, false);
					if (!At(occupied, x + 1, y, z)) Face(result, p + Vector3.Right * cell, Vector3.Back * cell, Vector3.Up * cell, true);
					if (!At(occupied, x, y - 1, z)) Face(result, p, Vector3.Right * cell, Vector3.Back * cell, false);
					if (!At(occupied, x, y + 1, z)) Face(result, p + Vector3.Up * cell, Vector3.Right * cell, Vector3.Back * cell, true);
					if (!At(occupied, x, y, z - 1)) Face(result, p, Vector3.Up * cell, Vector3.Right * cell, false);
					if (!At(occupied, x, y, z + 1)) Face(result, p + Vector3.Back * cell, Vector3.Up * cell, Vector3.Right * cell, true);
				}
		return Smooth([.. result]);
	}

	private static Vector3[] Smooth(Vector3[] triangles)
	{
		Dictionary<Vector3, int> lookup = []; List<Vector3> vertices = []; int[] indices = new int[triangles.Length];
		for (int i = 0; i < triangles.Length; i++)
		{
			if (!lookup.TryGetValue(triangles[i], out int index)) { index = vertices.Count; lookup[triangles[i]] = index; vertices.Add(triangles[i]); }
			indices[i] = index;
		}
		List<HashSet<int>> neighbors = Enumerable.Range(0, vertices.Count).Select(_ => new HashSet<int>()).ToList();
		for (int i = 0; i + 2 < indices.Length; i += 3)
		{
			Link(neighbors, indices[i], indices[i + 1]); Link(neighbors, indices[i + 1], indices[i + 2]); Link(neighbors, indices[i + 2], indices[i]);
		}
		for (int pass = 0; pass < 3; pass++)
		{
			Relax(vertices, neighbors, 0.38f);
			Relax(vertices, neighbors, -0.39f);
		}
		Vector3[] output = new Vector3[indices.Length]; for (int i = 0; i < indices.Length; i++) output[i] = vertices[indices[i]]; return output;
	}

	private static void Link(List<HashSet<int>> neighbors, int a, int b) { if (a == b) return; neighbors[a].Add(b); neighbors[b].Add(a); }
	private static void Relax(List<Vector3> vertices, List<HashSet<int>> neighbors, float amount)
	{
		Vector3[] next = [.. vertices];
		for (int i = 0; i < vertices.Count; i++)
		{
			if (neighbors[i].Count == 0) continue; Vector3 average = Vector3.Zero;
			foreach (int neighbor in neighbors[i]) average += vertices[neighbor];
			next[i] = vertices[i].Lerp(average / neighbors[i].Count, amount);
		}
		for (int i = 0; i < vertices.Count; i++) vertices[i] = next[i];
	}

	private static bool At(bool[,,] cells, int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < cells.GetLength(0) && y < cells.GetLength(1) && z < cells.GetLength(2) && cells[x, y, z];

	private static void Face(List<Vector3> output, Vector3 origin, Vector3 u, Vector3 v, bool flip)
	{
		Vector3 a = origin, b = origin + u, c = origin + u + v, d = origin + v;
		if (flip) { output.Add(a); output.Add(c); output.Add(b); output.Add(a); output.Add(d); output.Add(c); }
		else { output.Add(a); output.Add(b); output.Add(c); output.Add(a); output.Add(c); output.Add(d); }
	}

	private static bool Contains(Vector3[] triangles, Vector3 point)
	{
		int hits = 0; Vector3 direction = new(1f, 0.000173f, 0.000317f);
		for (int i = 0; i + 2 < triangles.Length; i += 3)
			if (RayTriangle(point, direction, triangles[i], triangles[i + 1], triangles[i + 2])) hits++;
		return (hits & 1) != 0;
	}

	private static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
	{
		Vector3 edge1 = b - a, edge2 = c - a, h = direction.Cross(edge2); float det = edge1.Dot(h);
		if (Mathf.Abs(det) < 0.0000001f) return false;
		float inv = 1f / det; Vector3 s = origin - a; float u = inv * s.Dot(h); if (u < 0 || u > 1) return false;
		Vector3 q = s.Cross(edge1); float v = inv * direction.Dot(q); if (v < 0 || u + v > 1) return false;
		return inv * edge2.Dot(q) > 0.000001f;
	}
}

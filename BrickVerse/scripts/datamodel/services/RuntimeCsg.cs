using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel.Services;

/// <summary>Small runtime BSP boolean kernel. It deliberately stores only positions;
/// EditableMesh regenerates normals after the topology operation.</summary>
internal static class RuntimeCsg
{
	private const float Epsilon = 0.00001f;

	internal sealed class Polygon
	{
		public List<Vector3> Vertices;
		public Plane Plane;
		public Polygon(IEnumerable<Vector3> vertices) { Vertices = [.. vertices]; Plane = MakePlane(Vertices); }
		public Polygon Clone() => new(Vertices);
		public void Flip() { Vertices.Reverse(); Plane = new Plane(-Plane.Normal, -Plane.D); }
	}

	internal sealed class Solid
	{
		public List<Polygon> Polygons;
		public Solid(IEnumerable<Polygon> polygons) => Polygons = [.. polygons];
		public Solid Union(Solid other)
		{
			Node a = new(Polygons.Select(p => p.Clone())); Node b = new(other.Polygons.Select(p => p.Clone()));
			a.ClipTo(b); b.ClipTo(a); b.Invert(); b.ClipTo(a); b.Invert(); a.Build(b.AllPolygons());
			return new Solid(a.AllPolygons());
		}
		public Solid Subtract(Solid other)
		{
			Node a = new(Polygons.Select(p => p.Clone())); Node b = new(other.Polygons.Select(p => p.Clone()));
			a.Invert(); a.ClipTo(b); b.ClipTo(a); b.Invert(); b.ClipTo(a); b.Invert(); a.Build(b.AllPolygons()); a.Invert();
			return new Solid(a.AllPolygons());
		}
	}

	private sealed class Node
	{
		private Plane? _plane; private readonly List<Polygon> _polygons = []; private Node? _front; private Node? _back;
		public Node() { }
		public Node(IEnumerable<Polygon> polygons) => Build(polygons);
		public void Invert() { foreach (Polygon p in _polygons) p.Flip(); if (_plane is Plane plane) _plane = new Plane(-plane.Normal, -plane.D); _front?.Invert(); _back?.Invert(); (_front, _back) = (_back, _front); }
		public List<Polygon> ClipPolygons(IEnumerable<Polygon> polygons)
		{
			if (_plane == null) return [.. polygons];
			List<Polygon> front = [], back = [];
			foreach (Polygon polygon in polygons) Split(_plane.Value, polygon, front, back, front, back);
			if (_front != null) front = _front.ClipPolygons(front);
			if (_back != null) back = _back.ClipPolygons(back); else back.Clear();
			front.AddRange(back); return front;
		}
		public void ClipTo(Node other) { List<Polygon> clipped = other.ClipPolygons(_polygons); _polygons.Clear(); _polygons.AddRange(clipped); _front?.ClipTo(other); _back?.ClipTo(other); }
		public List<Polygon> AllPolygons() { List<Polygon> all = [.. _polygons]; if (_front != null) all.AddRange(_front.AllPolygons()); if (_back != null) all.AddRange(_back.AllPolygons()); return all; }
		public void Build(IEnumerable<Polygon> source)
		{
			List<Polygon> polygons = [.. source]; if (polygons.Count == 0) return; _plane ??= polygons[0].Plane;
			List<Polygon> front = [], back = [];
			foreach (Polygon polygon in polygons) Split(_plane.Value, polygon, _polygons, _polygons, front, back);
			if (front.Count > 0) { _front ??= new Node(); _front.Build(front); }
			if (back.Count > 0) { _back ??= new Node(); _back.Build(back); }
		}
	}

	public static Solid FromMesh(Godot.Mesh mesh, Transform3D transform)
	{
		Vector3[] faces = mesh.GetFaces(); List<Polygon> polygons = [];
		for (int i = 0; i + 2 < faces.Length; i += 3)
		{
			Vector3 a = transform * faces[i], b = transform * faces[i + 1], c = transform * faces[i + 2];
			if ((b - a).Cross(c - a).LengthSquared() > Epsilon * Epsilon) polygons.Add(new Polygon([a, b, c]));
		}
		return new Solid(polygons);
	}

	private static Plane MakePlane(IReadOnlyList<Vector3> vertices)
	{
		Vector3 normal = (vertices[1] - vertices[0]).Cross(vertices[2] - vertices[0]).Normalized();
		return new Plane(normal, normal.Dot(vertices[0]));
	}

	private static void Split(Plane plane, Polygon polygon, List<Polygon> coplanarFront, List<Polygon> coplanarBack, List<Polygon> front, List<Polygon> back)
	{
		const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3; int polygonType = 0; List<int> types = [];
		foreach (Vector3 vertex in polygon.Vertices) { float distance = plane.Normal.Dot(vertex) - plane.D; int type = distance < -Epsilon ? Back : distance > Epsilon ? Front : Coplanar; polygonType |= type; types.Add(type); }
		switch (polygonType)
		{
			case Coplanar: (plane.Normal.Dot(polygon.Plane.Normal) > 0 ? coplanarFront : coplanarBack).Add(polygon); break;
			case Front: front.Add(polygon); break;
			case Back: back.Add(polygon); break;
			case Spanning:
				List<Vector3> f = [], b = [];
				for (int i = 0; i < polygon.Vertices.Count; i++)
				{
					int j = (i + 1) % polygon.Vertices.Count, ti = types[i], tj = types[j]; Vector3 vi = polygon.Vertices[i], vj = polygon.Vertices[j];
					if (ti != Back) f.Add(vi); if (ti != Front) b.Add(vi);
					if ((ti | tj) == Spanning) { float t = (plane.D - plane.Normal.Dot(vi)) / plane.Normal.Dot(vj - vi); Vector3 v = vi.Lerp(vj, t); f.Add(v); b.Add(v); }
				}
				if (f.Count >= 3) front.Add(new Polygon(f)); if (b.Count >= 3) back.Add(new Polygon(b)); break;
		}
	}
}

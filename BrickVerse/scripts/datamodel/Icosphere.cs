using BrickVerse.Attributes;
using Godot;
using System;
using System.Collections.Generic;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class Icosphere : Entity
{
	private MeshInstance3D _visual = null!;
	private CollisionShape3D _collision = null!;
	private ArrayMesh _mesh = null!;
	private StandardMaterial3D _material = null!;
	private int _subdivisions = 2;
	private Color _color = Colors.White;

	[Editable, ScriptProperty]
	public int Subdivisions
	{
		get => _subdivisions;
		set { int next = Math.Clamp(value, 0, 5); if (_subdivisions == next) return; _subdivisions = next; Rebuild(); OnPropertyChanged(); OnPropertyChanged(nameof(FaceCount)); }
	}

	[ScriptProperty] public int FaceCount => 20 * (int)Math.Pow(4, _subdivisions);

	[Editable, ScriptProperty]
	public override Color Color
	{
		get => _color;
		set { _color = value; if (_material != null) { _material.AlbedoColor = value; _material.Transparency = value.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled; } OnPropertyChanged(); }
	}

	public override void Init()
	{
		base.Init(); Name = "Icosphere"; _material = new StandardMaterial3D { AlbedoColor = _color };
		_visual = new MeshInstance3D { MaterialOverride = _material }; _collision = new CollisionShape3D();
		GDNode3D.AddChild(_visual); GDNode3D.AddChild(_collision); AddCollisionShape(_collision); Rebuild();
	}

	internal override void OnNodeSizeChanged(Vector3 newSize) { if (_visual != null) _visual.Scale = newSize; if (_collision != null) _collision.Scale = newSize; base.OnNodeSizeChanged(newSize); }
	internal override (Godot.Mesh Mesh, Transform3D Transform)[] GetBooleanGeometry() => _mesh == null ? [] : [(_mesh, GDNode3D.GlobalTransform.ScaledLocal(NodeSize))];
	public override Aabb GetSelfBound() => _visual?.Mesh?.GetAabb() ?? base.GetSelfBound();

	private void Rebuild()
	{
		if (_visual == null) return;
		float t = (1f + Mathf.Sqrt(5f)) / 2f;
		List<Vector3> vertices = [new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)];
		for (int i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].Normalized() * 0.5f;
		List<(int A, int B, int C)> faces = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)];
		for (int level = 0; level < _subdivisions; level++)
		{
			Dictionary<(int, int), int> cache = []; List<(int, int, int)> next = [];
			int Mid(int a, int b) { (int, int) key = a < b ? (a, b) : (b, a); if (cache.TryGetValue(key, out int id)) return id; Vector3 point = ((vertices[a] + vertices[b]) * 0.5f).Normalized() * 0.5f; id = vertices.Count; vertices.Add(point); cache[key] = id; return id; }
			foreach ((int a, int b, int c) in faces) { int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a); next.Add((a, ab, ca)); next.Add((b, bc, ab)); next.Add((c, ca, bc)); next.Add((ab, bc, ca)); }
			faces = next;
		}
		SurfaceTool tool = new(); tool.Begin(Godot.Mesh.PrimitiveType.Triangles);
		foreach ((int a, int b, int c) in faces) { Add(tool, vertices[a]); Add(tool, vertices[b]); Add(tool, vertices[c]); }
		_mesh = tool.Commit(); _visual.Mesh = _mesh; _visual.Scale = NodeSize; _collision.Shape = _mesh.CreateTrimeshShape(); _collision.Scale = NodeSize; UpdateNegateHighlight();
	}
	private static void Add(SurfaceTool tool, Vector3 point) { tool.SetNormal(point.Normalized()); tool.AddVertex(point); }
}

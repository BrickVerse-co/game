// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A scene entity backed by runtime-editable mesh geometry.</summary>
[Instantiable]
public sealed partial class EditableMeshPart : Entity
{
	private EditableMesh _geometry = CreateBoxGeometry();
	private MeshInstance3D _visual = null!;
	private bool _wireframe;
	private EditableMesh? _modifierPreview;
	private CollisionTypeEnum _collisionType = CollisionTypeEnum.Convex;
	private readonly ShaderMaterial _wireframeMaterial = new()
	{
		Shader = new Shader
		{
			Code = "shader_type spatial; render_mode wireframe, unshaded, cull_disabled; uniform vec4 line_color : source_color = vec4(0.25, 0.7, 1.0, 1.0); void fragment(){ ALBEDO=line_color.rgb; ALPHA=line_color.a; }",
		},
	};

	[ScriptProperty]
	public EditableMesh Geometry
	{
		get => _geometry;
		set
		{
			if (value == null || ReferenceEquals(_geometry, value))
				return;
			if (_visual != null)
			{
				_geometry.Committed -= OnGeometryCommitted;
				_geometry.UnbindFrom(_visual);
			}
			_geometry = value;
			if (_visual != null)
			{
				_geometry.BindTo(_visual);
				_geometry.Committed += OnGeometryCommitted;
				RebuildCollision();
			}
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public EditableMeshModifierStack Modifiers { get; } = new();

	[Editable, ScriptProperty, DefaultValue(CollisionTypeEnum.Convex)]
	public CollisionTypeEnum CollisionType
	{
		get => _collisionType;
		set
		{
			if (_collisionType == value)
				return;
			_collisionType = value;
			RebuildCollision();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool Wireframe
	{
		get => _wireframe;
		set
		{
			if (_wireframe == value)
				return;
			_wireframe = value;
			if (_visual != null)
				_visual.MaterialOverride = value ? _wireframeMaterial : null;
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		_visual = new MeshInstance3D { Name = "EditableMesh" };
		GDNode3D.AddChild(_visual);
		_geometry.BindTo(_visual);
		_geometry.Committed += OnGeometryCommitted;
		base.Init();
		RebuildCollision();
	}

	public override void PreDelete()
	{
		_modifierPreview?.UnbindFrom(_visual);
		_geometry.Committed -= OnGeometryCommitted;
		_geometry.UnbindFrom(_visual);
		base.PreDelete();
	}

	internal override (Godot.Mesh Mesh, Transform3D Transform)[] GetBooleanGeometry() =>
		_visual.Mesh == null ? [] : [(_visual.Mesh, _visual.GlobalTransform)];

	public override Aabb GetSelfBound() =>
		_visual?.Mesh?.GetAabb() ?? base.GetSelfBound();

	[ScriptMethod]
	public void RefreshModifierPreview()
	{
		_modifierPreview?.UnbindFrom(_visual);
		_geometry.UnbindFrom(_visual);
		_modifierPreview = Modifiers.Evaluate(_geometry);
		_modifierPreview.BindTo(_visual);
		RebuildCollision();
	}

	[ScriptMethod]
	public void ClearModifierPreview()
	{
		_modifierPreview?.UnbindFrom(_visual);
		_modifierPreview = null;
		_geometry.BindTo(_visual);
		RebuildCollision();
	}

	[ScriptMethod]
	public void ApplyModifiers()
	{
		EditableMesh evaluated = Modifiers.Evaluate(_geometry);
		ClearModifierPreview();
		Geometry = evaluated;
		Modifiers.Clear();
	}

	private void OnGeometryCommitted() => RebuildCollision();

	private void RebuildCollision()
	{
		if (_visual == null || !GodotObject.IsInstanceValid(_visual))
			return;
		ClearCollisionShapes();
		Godot.Mesh? mesh = _visual.Mesh;
		if (mesh == null || mesh.GetSurfaceCount() == 0)
			return;
		CollisionShape3D collision = new() { Name = "EditableMeshCollision" };
		switch (_collisionType)
		{
			case CollisionTypeEnum.Bounds:
				Aabb bounds = mesh.GetAabb();
				collision.Shape = new BoxShape3D { Size = bounds.Size.Max(Vector3.One * 0.001f) };
				collision.Position = bounds.GetCenter();
				break;
			case CollisionTypeEnum.Exact:
				ConcavePolygonShape3D exact = new();
				exact.SetFaces(mesh.GetFaces());
				collision.Shape = exact;
				break;
			default:
				collision.Shape = new ConvexPolygonShape3D { Points = mesh.GetFaces() };
				break;
		}
		GDNode3D.AddChild(collision, false, Node.InternalMode.Back);
		AddCollisionShape(collision);
		UpdateCollision();
	}

	[ScriptEnum("EditableMeshCollisionType")]
	public enum CollisionTypeEnum
	{
		Bounds,
		Convex,
		Exact,
	}

	private static EditableMesh CreateBoxGeometry()
	{
		EditableMesh mesh = new() { AutoCommit = false };
		int[] vertices =
		[
			mesh.AddVertex(new Vector3(-0.5f, -0.5f, -0.5f)),
			mesh.AddVertex(new Vector3(0.5f, -0.5f, -0.5f)),
			mesh.AddVertex(new Vector3(0.5f, 0.5f, -0.5f)),
			mesh.AddVertex(new Vector3(-0.5f, 0.5f, -0.5f)),
			mesh.AddVertex(new Vector3(-0.5f, -0.5f, 0.5f)),
			mesh.AddVertex(new Vector3(0.5f, -0.5f, 0.5f)),
			mesh.AddVertex(new Vector3(0.5f, 0.5f, 0.5f)),
			mesh.AddVertex(new Vector3(-0.5f, 0.5f, 0.5f)),
		];
		int[][] triangles =
		[
			[0, 2, 1], [0, 3, 2],
			[4, 5, 6], [4, 6, 7],
			[0, 1, 5], [0, 5, 4],
			[3, 7, 6], [3, 6, 2],
			[0, 4, 7], [0, 7, 3],
			[1, 2, 6], [1, 6, 5],
		];
		foreach (int[] triangle in triangles)
			mesh.AddTriangle(
				vertices[triangle[0]],
				vertices[triangle[1]],
				vertices[triangle[2]]
			);
		mesh.ProjectUVs();
		mesh.Commit();
		mesh.AutoCommit = true;
		return mesh;
	}
}

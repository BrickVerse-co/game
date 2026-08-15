using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class UnionOperation : Entity
{
	private EditableMesh? _geometry;
	private MeshInstance3D _visual = null!;
	private CollisionShape3D _collision = null!;

	[ScriptProperty] public EditableMesh? Geometry => _geometry;
	[ScriptProperty] public BVSignal GeometryChanged { get; private set; } = new();

	public override void Init()
	{
		base.Init();
		_visual = new MeshInstance3D(); _collision = new CollisionShape3D();
		GDNode3D.AddChild(_visual); GDNode3D.AddChild(_collision); AddCollisionShape(_collision);
		Anchored = true; Name = "UnionOperation";
	}

	internal void SetGeometry(EditableMesh geometry)
	{
		_geometry = geometry; ArrayMesh mesh = geometry.GetArrayMesh(); _visual.Mesh = mesh;
		Vector3[] faces = mesh.GetFaces();
		_collision.Shape = faces.Length >= 3 ? new ConcavePolygonShape3D { Data = faces } : null;
		GeometryChanged.Invoke(); OnPropertyChanged(nameof(Geometry));
	}

	[ScriptMethod]
	public System.Threading.Tasks.Task RebuildAsync() => Root.Geometry.RebuildAsync(this);

	public override Aabb GetSelfBound() => _visual?.Mesh?.GetAabb() ?? base.GetSelfBound();
}

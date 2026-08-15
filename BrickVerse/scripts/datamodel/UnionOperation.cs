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
	private StandardMaterial3D _material = null!;
	private Color _color = Colors.White;

	[ScriptProperty] public EditableMesh? Geometry => _geometry;
	[ScriptProperty] public BVSignal GeometryChanged { get; private set; } = new();

	public override void Init()
	{
		base.Init();
		_material = new StandardMaterial3D
		{
			AlbedoColor = _color,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			VertexColorUseAsAlbedo = true,
		};
		_visual = new MeshInstance3D { MaterialOverride = _material }; _collision = new CollisionShape3D();
		GDNode3D.AddChild(_visual); GDNode3D.AddChild(_collision); AddCollisionShape(_collision);
		Anchored = true; Name = "UnionOperation";
	}

	[Editable, ScriptProperty]
	public override Color Color
	{
		get => _color;
		set
		{
			if (_color == value) return;
			_color = value;
			if (_material != null)
			{
				_material.AlbedoColor = value;
				_material.Transparency = value.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
			}
			OnPropertyChanged();
		}
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

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class Beam : Instance
{
	private Attachment? _attachment0, _attachment1;
	private bool _enabled = true;
	private float _width0 = 0.2f, _width1 = 0.2f;
	private Color _color = Colors.White;
	private int _segments = 10;
	private float _curveSize0, _curveSize1;
	private ImmediateMesh _mesh = null!;
	private MeshInstance3D _visual = null!;
	private StandardMaterial3D _material = null!;

	[Editable, ScriptProperty] public Attachment? Attachment0 { get => _attachment0; set { _attachment0 = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Attachment? Attachment1 { get => _attachment1; set { _attachment1 = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0.2f)] public float Width0 { get => _width0; set { _width0 = Mathf.Max(0, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0.2f)] public float Width1 { get => _width1; set { _width1 = Mathf.Max(0, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Color Color { get => _color; set { _color = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(10)] public int Segments { get => _segments; set { _segments = Mathf.Clamp(value, 1, 100); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float CurveSize0 { get => _curveSize0; set { _curveSize0 = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float CurveSize1 { get => _curveSize1; set { _curveSize1 = value; OnPropertyChanged(); } }

	public override void Init()
	{
		_mesh = new(); _material = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		_visual = new() { Mesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
		GDNode.AddChild(_visual, @internal: Node.InternalMode.Back); SetProcess(true); base.Init();
	}
	public override void Process(double delta) { Draw(); base.Process(delta); }
	private void Draw()
	{
		_mesh.ClearSurfaces();
		if (!_enabled || _attachment0 == null || _attachment1 == null) return;
		Vector3 a = _attachment0.WorldPosition, b = _attachment1.WorldPosition;
		Vector3 camera = Root.Environment.CurrentCamera?.Position ?? (a + Vector3.Up);
		_mesh.SurfaceBegin(Godot.Mesh.PrimitiveType.TriangleStrip, _material);
		for (int i = 0; i <= _segments; i++)
		{
			float t = i / (float)_segments, u = 1 - t;
			Vector3 p = u*u*u*a + 3*u*u*t*(a + _attachment0.WorldAxis*_curveSize0) + 3*u*t*t*(b - _attachment1.WorldAxis*_curveSize1) + t*t*t*b;
			Vector3 tangent = (b - a).Normalized(); Vector3 side = tangent.Cross((camera - p).Normalized()).Normalized() * Mathf.Lerp(_width0, _width1, t) * 0.5f;
			_mesh.SurfaceSetColor(_color); _mesh.SurfaceAddVertex(p - side); _mesh.SurfaceSetColor(_color); _mesh.SurfaceAddVertex(p + side);
		}
		_mesh.SurfaceEnd();
	}
}

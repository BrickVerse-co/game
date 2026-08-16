// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;
using System.Collections.Generic;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class Trail : Instance
{
	private readonly List<(Vector3 A, Vector3 B, double Age)> _points = [];
	private Attachment? _attachment0, _attachment1;
	private bool _enabled = true;
	private float _lifetime = 1, _minLength = 0.1f;
	private Color _color = Colors.White;
	private ImmediateMesh _mesh = null!; private StandardMaterial3D _material = null!;
	[Editable, ScriptProperty] public Attachment? Attachment0 { get => _attachment0; set { _attachment0 = value; Clear(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Attachment? Attachment1 { get => _attachment1; set { _attachment1 = value; Clear(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float Lifetime { get => _lifetime; set { _lifetime = Mathf.Max(0.01f, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0.1f)] public float MinLength { get => _minLength; set { _minLength = Mathf.Max(0, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Color Color { get => _color; set { _color = value; OnPropertyChanged(); } }
	public override void Init()
	{
		_mesh = new(); _material = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		GDNode.AddChild(new MeshInstance3D { Mesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true }, @internal: Node.InternalMode.Back); SetProcess(true); base.Init();
	}
	public override void Process(double delta)
	{
		for (int i = _points.Count - 1; i >= 0; i--) { var p = _points[i]; p.Age += delta; if (p.Age > _lifetime) _points.RemoveAt(i); else _points[i] = p; }
		if (_enabled && _attachment0 != null && _attachment1 != null)
		{
			Vector3 a = _attachment0.WorldPosition, b = _attachment1.WorldPosition;
			if (_points.Count == 0 || (_points[^1].A - a).Length() >= _minLength || (_points[^1].B - b).Length() >= _minLength) _points.Add((a, b, 0));
		}
		Draw(); base.Process(delta);
	}
	[ScriptMethod] public void Clear() { _points.Clear(); _mesh?.ClearSurfaces(); }
	private void Draw()
	{
		_mesh.ClearSurfaces(); if (_points.Count < 2) return; _mesh.SurfaceBegin(Godot.Mesh.PrimitiveType.TriangleStrip, _material);
		foreach (var p in _points) { float alpha = 1 - (float)(p.Age / _lifetime); _mesh.SurfaceSetColor(new Color(_color, _color.A * alpha)); _mesh.SurfaceAddVertex(p.A); _mesh.SurfaceSetColor(new Color(_color, _color.A * alpha)); _mesh.SurfaceAddVertex(p.B); }
		_mesh.SurfaceEnd();
	}
}

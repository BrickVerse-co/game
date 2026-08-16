using BrickVerse.Attributes;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace BrickVerse.Datamodel;

/// <summary>A sparse, paintable GPU-instanced grass layer intended as a Terrain child.</summary>
[Instantiable]
public sealed partial class TerrainGrass : Instance
{
	private readonly record struct Blade(Vector3 Position, Vector3 Normal, float Random, float HeightScale, float WidthScale, Color Tint);
	private readonly List<Blade> _blades = [];
	private MultiMeshInstance3D _renderer = null!;
	private ShaderMaterial _material = null!;
	private string _serialisedCoverage = "";
	private float _density = 1.2f, _height = 1.4f, _width = 0.13f, _heightVariation = 0.35f, _drawDistance = 180f;
	private Color _baseColor = new("327a32"), _tipColor = new("83c95b");
	private bool _deformToSurface = true;
	private float _surfaceOffset = -0.1f, _paintDensityScale = 1f, _paintHeightScale = 1f, _paintWidthScale = 1f;
	private Color _paintColor = Colors.White;

	[Editable, ScriptProperty, DefaultValue("")] public string SerialisedCoverage { get => _serialisedCoverage; set { _serialisedCoverage = value ?? ""; Decode(); Refresh(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float Density { get => _density; set { _density = Mathf.Clamp(value, 0.05f, 8f); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float BladeHeight { get => _height; set { _height = Mathf.Clamp(value, 0.05f, 12f); Refresh(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float BladeWidth { get => _width; set { _width = Mathf.Clamp(value, 0.01f, 3f); Refresh(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float HeightVariation { get => _heightVariation; set { _heightVariation = Mathf.Clamp(value, 0, 1); UpdateShader(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Color BaseColor { get => _baseColor; set { _baseColor = value; UpdateShader(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Color TipColor { get => _tipColor; set { _tipColor = value; UpdateShader(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float DrawDistance { get => _drawDistance; set { _drawDistance = Mathf.Clamp(value, 8, 1000); Refresh(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool DeformToSurface { get => _deformToSurface; set { _deformToSurface = value; Refresh(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float SurfaceOffset { get => _surfaceOffset; set { _surfaceOffset = Mathf.Clamp(value, -2f, 2f); Refresh(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float PaintDensityScale { get => _paintDensityScale; set { _paintDensityScale = Mathf.Clamp(value, 0.05f, 8f); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float PaintHeightScale { get => _paintHeightScale; set { _paintHeightScale = Mathf.Clamp(value, 0.05f, 8f); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float PaintWidthScale { get => _paintWidthScale; set { _paintWidthScale = Mathf.Clamp(value, 0.05f, 8f); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Color PaintColor { get => _paintColor; set { _paintColor = value; OnPropertyChanged(); } }

	public override void Init()
	{
		base.Init(); Name = "TerrainGrass";
		_renderer = new MultiMeshInstance3D { Name = "GrassRenderer" }; GDNode.AddChild(_renderer, false, Node.InternalMode.Back);
		_material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } }; UpdateShader(); Refresh();
	}

	public override void Process(double delta) { UpdateShader(); base.Process(delta); }

	[ScriptMethod]
	public void Paint(Vector3 position, Vector3 normal, float radius, float strength = 1)
	{
		int count = Math.Clamp(Mathf.RoundToInt(Mathf.Pi * radius * radius * _density * _paintDensityScale * Mathf.Clamp(strength, 0.05f, 1f)), 1, 3000);
		RandomNumberGenerator random = new(); random.Seed = (ulong)(Mathf.Abs(position.GetHashCode()) + _blades.Count + 1);
		Vector3 n = normal.LengthSquared() > 0.1f ? normal.Normalized() : Vector3.Up;
		Vector3 tangent = Mathf.Abs(n.Dot(Vector3.Up)) < 0.95f ? n.Cross(Vector3.Up).Normalized() : Vector3.Right;
		Vector3 bitangent = n.Cross(tangent).Normalized(); float minimum = 0.14f / Mathf.Max(_density, 0.05f);
		for (int i = 0; i < count; i++)
		{
			float angle = random.Randf() * Mathf.Tau, distance = Mathf.Sqrt(random.Randf()) * radius;
			Vector3 point = position + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * distance;
			if (_blades.Exists(blade => blade.Position.DistanceSquaredTo(point) < minimum * minimum)) continue;
			_blades.Add(new Blade(point, n, random.Randf(), _paintHeightScale, _paintWidthScale, _paintColor));
		}
		Encode(); Refresh();
	}

	[ScriptMethod]
	public void Erase(Vector3 position, float radius) { _blades.RemoveAll(blade => blade.Position.DistanceSquaredTo(position) <= radius * radius); Encode(); Refresh(); }
	[ScriptMethod] public void Clear() { _blades.Clear(); Encode(); Refresh(); }

	private void Refresh()
	{
		if (_renderer == null) return;
		MultiMesh multimesh = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, UseColors = true, Mesh = CreateBladeMesh(), InstanceCount = _blades.Count, VisibleInstanceCount = _blades.Count };
		for (int i = 0; i < _blades.Count; i++)
		{
			Blade blade = _blades[i]; Basis basis = _deformToSurface ? new Basis(new Quaternion(Vector3.Up, blade.Normal)) : Basis.Identity;
			basis = basis.Rotated(blade.Normal, blade.Random * Mathf.Tau);
			multimesh.SetInstanceTransform(i, new Transform3D(basis, blade.Position + blade.Normal * _surfaceOffset));
			multimesh.SetInstanceCustomData(i, new Color(blade.Random, blade.HeightScale, blade.WidthScale, 1));
			multimesh.SetInstanceColor(i, blade.Tint);
		}
		_renderer.Multimesh = multimesh; _renderer.VisibilityRangeEnd = _drawDistance;
	}

	private ArrayMesh CreateBladeMesh()
	{
		SurfaceTool st = new(); st.Begin(Godot.Mesh.PrimitiveType.Triangles); int segments = 4;
		for (int cross = 0; cross < 2; cross++) for (int segment = 0; segment < segments; segment++)
		{
			float y0 = (float)segment / segments, y1 = (float)(segment + 1) / segments, w0 = _width * (1 - y0), w1 = _width * (1 - y1);
			Vector3 side = cross == 0 ? Vector3.Right : Vector3.Back; Vector3 a = -side * w0 + Vector3.Up * (_height * y0), b = side * w0 + Vector3.Up * (_height * y0), c = side * w1 + Vector3.Up * (_height * y1), d = -side * w1 + Vector3.Up * (_height * y1);
			Add(st, a, new(0, y0)); Add(st, b, new(1, y0)); Add(st, c, new(1, y1)); Add(st, a, new(0, y0)); Add(st, c, new(1, y1)); Add(st, d, new(0, y1));
		}
		st.GenerateNormals(); ArrayMesh mesh = st.Commit(); mesh.SurfaceSetMaterial(0, _material); return mesh;
	}
	private static void Add(SurfaceTool st, Vector3 vertex, Vector2 uv) { st.SetUV(uv); st.AddVertex(vertex); }
	private void UpdateShader() { if (_material == null) return; _material.SetShaderParameter("base_color", _baseColor); _material.SetShaderParameter("tip_color", _tipColor); _material.SetShaderParameter("height_variation", _heightVariation); Vector3 wind = Root?.Environment?.WindDirection ?? Vector3.Right; _material.SetShaderParameter("wind_direction", new Vector2(wind.X, wind.Z).Normalized()); _material.SetShaderParameter("wind_strength", Root?.Environment?.WindStrength ?? 0.28f); _material.SetShaderParameter("wind_speed", Root?.Environment?.WindSpeed ?? 1.5f); }

	private void Encode()
	{
		using MemoryStream stream = new(); using BinaryWriter writer = new(stream); writer.Write(_blades.Count);
		foreach (Blade b in _blades) { writer.Write(b.Position.X); writer.Write(b.Position.Y); writer.Write(b.Position.Z); writer.Write(b.Normal.X); writer.Write(b.Normal.Y); writer.Write(b.Normal.Z); writer.Write(b.Random); writer.Write(b.HeightScale); writer.Write(b.WidthScale); writer.Write(b.Tint.R); writer.Write(b.Tint.G); writer.Write(b.Tint.B); writer.Write(b.Tint.A); }
		_serialisedCoverage = Convert.ToBase64String(stream.ToArray()); OnPropertyChanged(nameof(SerialisedCoverage));
	}
	private void Decode()
	{
		_blades.Clear(); if (string.IsNullOrEmpty(_serialisedCoverage)) return;
		try
		{
			using MemoryStream stream = new(Convert.FromBase64String(_serialisedCoverage)); using BinaryReader reader = new(stream);
			int count = Math.Clamp(reader.ReadInt32(), 0, 2_000_000); bool legacy = count > 0 && (stream.Length - 4) / count < 52;
			for (int i = 0; i < count; i++)
			{
				Vector3 position = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); Vector3 normal = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); float random = reader.ReadSingle();
				_blades.Add(legacy ? new Blade(position, normal, random, 1, 1, Colors.White) : new Blade(position, normal, random, reader.ReadSingle(), reader.ReadSingle(), new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())));
			}
		}
		catch { _blades.Clear(); }
	}

	private const string ShaderCode = "shader_type spatial; render_mode cull_disabled; uniform vec4 base_color : source_color; uniform vec4 tip_color : source_color; uniform float height_variation=0.35; uniform vec2 wind_direction=vec2(1.0,0.0); uniform float wind_strength=0.28; uniform float wind_speed=1.5; varying float blade_y; varying vec4 blade_tint; void vertex(){ blade_y=UV.y; blade_tint=COLOR; float h=(1.0+(INSTANCE_CUSTOM.x-0.5)*height_variation)*INSTANCE_CUSTOM.y; VERTEX.y*=h; VERTEX.xz*=INSTANCE_CUSTOM.z; vec3 world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz; float wave=sin(TIME*wind_speed+dot(world.xz,wind_direction)*0.12+INSTANCE_CUSTOM.x*19.0); VERTEX.xz+=vec2(wave)*wind_direction*wind_strength*UV.y*UV.y; } void fragment(){ vec4 gradient=mix(base_color,tip_color,blade_y)*blade_tint; ALBEDO=gradient.rgb; ALPHA=gradient.a; ROUGHNESS=0.9; }";
}

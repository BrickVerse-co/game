// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>
/// A deterministic, spline-driven procedural placement primitive. Control points are local to
/// the instance and are stored as text so they remain editable and portable in world files.
/// </summary>
[Instantiable]
public sealed partial class PCGSpline : Dynamic
{
	public enum InterpolationModeEnum { Linear, CatmullRom }
	private readonly List<Vector3> _points = [];
	private string _controlPoints = "0,0,0;0,0,-12;12,0,-24";
	private bool _closed;
	private InterpolationModeEnum _interpolation = InterpolationModeEnum.CatmullRom;
	private bool _enabled = true;
	private float _spacing = 3f;
	private float _lateralJitter;
	private float _verticalJitter;
	private float _scaleJitter;
	private float _randomYaw = 180f;
	private int _seed = 1;
	private Color _splineColor = new("55b8ff");
	private Node3D _generated = null!;
	private MeshInstance3D _preview = null!;
	private readonly List<Placement> _placements = [];
	private readonly HashSet<Dynamic> _templates = [];
	private bool _rebuildQueued;
	private readonly record struct Placement(Dynamic Template, Transform3D Transform);
	public int GeneratedInstanceCount { get; private set; }

	[Editable, ScriptProperty, DefaultValue("0,0,0;0,0,-12;12,0,-24")]
	public string ControlPoints { get => _controlPoints; set { _controlPoints = value ?? ""; ParsePoints(); Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public bool Closed { get => _closed; set { _closed = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(InterpolationModeEnum.CatmullRom)] public InterpolationModeEnum Interpolation { get => _interpolation; set { _interpolation = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(3f)] public float Spacing { get => _spacing; set { _spacing = Mathf.Clamp(value, 0.1f, 1000f); Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float LateralJitter { get => _lateralJitter; set { _lateralJitter = Mathf.Clamp(value, 0, 1000); Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float VerticalJitter { get => _verticalJitter; set { _verticalJitter = Mathf.Clamp(value, 0, 1000); Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float ScaleJitter { get => _scaleJitter; set { _scaleJitter = Mathf.Clamp(value, 0, 0.95f); Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(180f)] public float RandomYaw { get => _randomYaw; set { _randomYaw = Mathf.Clamp(value, 0, 180); Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1)] public int Seed { get => _seed; set { _seed = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Color SplineColor { get => _splineColor; set { _splineColor = value; DrawPreview(); OnPropertyChanged(); } }

	public override void Init()
	{
		_generated = new Node3D { Name = "GeneratedInstances" };
		_preview = new MeshInstance3D { Name = "SplinePreview", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		GDNode.AddChild(_generated, false, Node.InternalMode.Back);
		GDNode.AddChild(_preview, false, Node.InternalMode.Back);
		ChildAdded.Connect(OnTemplateAdded);
		ChildRemoved.Connect(OnTemplateRemoved);
		ParsePoints();
		Rebuild();
		base.Init();
	}

	public override void PreDelete()
	{
		ChildAdded.Disconnect(OnTemplateAdded); ChildRemoved.Disconnect(OnTemplateRemoved);
		foreach (Dynamic template in _templates) DisconnectTemplate(template);
		_templates.Clear(); base.PreDelete();
	}

	[ScriptMethod]
	public void AddPoint(Vector3 point)
	{
		_points.Add(point); StorePoints(); Rebuild();
	}

	[ScriptMethod]
	public void SetPoint(int index, Vector3 point)
	{
		if ((uint)index >= (uint)_points.Count) throw new ArgumentOutOfRangeException(nameof(index));
		_points[index] = point; StorePoints(); Rebuild();
	}

	internal void SetPointDuringEdit(int index, Vector3 point)
	{
		if ((uint)index >= (uint)_points.Count) throw new ArgumentOutOfRangeException(nameof(index));
		_points[index] = point; StorePoints(); DrawPreview();
	}

	[ScriptMethod]
	public void InsertPoint(int index, Vector3 point)
	{
		if ((uint)index > (uint)_points.Count) throw new ArgumentOutOfRangeException(nameof(index));
		_points.Insert(index, point); StorePoints(); Rebuild();
	}

	[ScriptMethod]
	public void RemovePoint(int index)
	{
		if ((uint)index >= (uint)_points.Count) throw new ArgumentOutOfRangeException(nameof(index));
		_points.RemoveAt(index); StorePoints(); Rebuild();
	}

	[ScriptMethod] public void ClearPoints() { _points.Clear(); StorePoints(); Rebuild(); }
	[ScriptMethod] public int GetPointCount() => _points.Count;
	[ScriptMethod] public Vector3 GetPoint(int index) => (uint)index < (uint)_points.Count ? _points[index] : throw new ArgumentOutOfRangeException(nameof(index));
	[ScriptMethod] public Vector3 Sample(float alpha) => SampleCurve(Mathf.Clamp(alpha, 0, 1));
	[ScriptMethod] public Vector3 GetTangent(float alpha) { float epsilon = 0.001f; return (SampleCurve(Mathf.Min(1, alpha + epsilon)) - SampleCurve(Mathf.Max(0, alpha - epsilon))).Normalized(); }
	[ScriptMethod] public float GetLength()
	{
		if (_points.Count < 2) return 0; int steps = (_closed ? _points.Count : _points.Count - 1) * 32; float length = 0; Vector3 previous = SampleCurve(0);
		for (int i = 1; i <= steps; i++) { Vector3 current = SampleCurve(i / (float)steps); length += previous.DistanceTo(current); previous = current; }
		return length;
	}
	[ScriptMethod] public void Regenerate() => Rebuild();
	[ScriptMethod]
	public Instance[] Bake()
	{
		if (Parent == null) return [];
		List<Instance> baked = [];
		foreach (Placement placement in _placements)
		{
			if (placement.Template.Clone(Parent) is not Dynamic clone) continue;
			clone.Name = placement.Template.Name + "_Spline";
			clone.SetGlobalTransform(GDNode3D.GlobalTransform * placement.Transform * placement.Template.GDNode3D.Transform);
#if CREATOR
			clone.CreatorInserted();
#endif
			baked.Add(clone);
		}
		return [.. baked];
	}

	private void Rebuild()
	{
		if (_generated == null || _preview == null) return;
		DrawPreview();
		List<(Vector3 position, Vector3 tangent)> samples = BuildDistanceSamples();
		RefreshTemplates(); ClearGenerated(); _placements.Clear();
		Dynamic[] templates = [.. _templates.Where(static template => GodotObject.IsInstanceValid(template.GDNode3D))];
		GeneratedInstanceCount = _enabled && templates.Length > 0 ? samples.Count : 0;
		RandomNumberGenerator random = new() { Seed = unchecked((ulong)(uint)_seed) };
		for (int i = 0; i < samples.Count && _enabled; i++)
		{
			if (templates.Length == 0) break;
			(Vector3 position, Vector3 tangent) = samples[i];
			Dynamic template = templates[random.RandiRange(0, templates.Length - 1)];
			Vector3 forward = tangent.LengthSquared() > 0.0001f ? tangent.Normalized() : Vector3.Forward;
			Vector3 right = Vector3.Up.Cross(forward).Normalized();
			if (right.LengthSquared() < 0.0001f) right = Vector3.Right;
			Vector3 up = forward.Cross(right).Normalized();
			position += right * random.RandfRange(-_lateralJitter, _lateralJitter) + up * random.RandfRange(-_verticalJitter, _verticalJitter);
			float scale = random.RandfRange(1f - _scaleJitter, 1f + _scaleJitter);
			Basis basis = new Basis(right, up, -forward).Rotated(Vector3.Up, Mathf.DegToRad(random.RandfRange(-_randomYaw, _randomYaw))).Scaled(Vector3.One * scale);
			Transform3D transform = new(basis, position); _placements.Add(new Placement(template, transform));
			Node3D holder = new() { Transform = transform, Name = $"Placement{i}" };
			Node duplicate = template.GDNode3D.Duplicate();
			if (duplicate is Node3D duplicate3D) duplicate3D.Visible = true;
			holder.AddChild(duplicate); _generated.AddChild(holder);
		}
	}

	private void RefreshTemplates()
	{
		HashSet<Dynamic> current = [.. GetChildren().OfType<Dynamic>()];
		foreach (Dynamic old in _templates.Except(current)) DisconnectTemplate(old);
		foreach (Dynamic template in current.Except(_templates)) ConnectTemplate(template);
		_templates.Clear(); foreach (Dynamic template in current) _templates.Add(template);
	}

	private void ConnectTemplate(Dynamic template)
	{
		if (GodotObject.IsInstanceValid(template.GDNode3D)) template.GDNode3D.Visible = false;
		template.PropertyChanged.Connect(OnTemplatePropertyChanged); template.TransformChanged += QueueRebuild;
	}

	private void DisconnectTemplate(Dynamic template)
	{
		template.PropertyChanged.Disconnect(OnTemplatePropertyChanged); template.TransformChanged -= QueueRebuild;
		if (GodotObject.IsInstanceValid(template.GDNode3D)) template.GDNode3D.Visible = true;
	}

	private void OnTemplatePropertyChanged(string propertyName) => QueueRebuild();

	private void ClearGenerated()
	{
		foreach (Node child in _generated.GetChildren()) { _generated.RemoveChild(child); child.QueueFree(); }
	}

	private void OnTemplateAdded(Instance child) { if (child is Dynamic) QueueRebuild(); }
	private void OnTemplateRemoved(Instance child) { if (child is Dynamic dynamic) { if (_templates.Remove(dynamic)) DisconnectTemplate(dynamic); QueueRebuild(); } }
	private void QueueRebuild()
	{
		if (_rebuildQueued) return; _rebuildQueued = true;
		Callable.From(() => { _rebuildQueued = false; if (GodotObject.IsInstanceValid(GDNode)) Rebuild(); }).CallDeferred();
	}

	private List<(Vector3 position, Vector3 tangent)> BuildDistanceSamples()
	{
		List<(Vector3 position, Vector3 tangent)> result = [];
		if (_points.Count < 2) return result;
		const int resolutionPerSegment = 24;
		int segmentCount = _closed ? _points.Count : _points.Count - 1;
		List<Vector3> polyline = [];
		for (int i = 0; i <= segmentCount * resolutionPerSegment; i++)
			polyline.Add(SampleCurve(i / (float)(segmentCount * resolutionPerSegment)));
		float carried = 0;
		Vector3 previous = polyline[0];
		result.Add((previous, polyline[1] - previous));
		for (int i = 1; i < polyline.Count; i++)
		{
			Vector3 target = polyline[i];
			float length = previous.DistanceTo(target);
			while (carried + length >= _spacing)
			{
				float step = _spacing - carried;
				Vector3 tangent = (target - previous).Normalized();
				previous += tangent * step;
				result.Add((previous, tangent));
				length -= step; carried = 0;
			}
			carried += length; previous = target;
		}
		return result;
	}

	private Vector3 SampleCurve(float alpha)
	{
		if (_points.Count == 0) return Vector3.Zero;
		if (_points.Count == 1) return _points[0];
		int count = _closed ? _points.Count : _points.Count - 1;
		float scaled = Mathf.Clamp(alpha, 0, 1) * count;
		int segment = Mathf.Min(Mathf.FloorToInt(scaled), count - 1);
		float t = scaled - segment;
		if (_interpolation == InterpolationModeEnum.Linear) return PointAt(segment).Lerp(PointAt(segment + 1), t);
		Vector3 p0 = PointAt(segment - 1), p1 = PointAt(segment), p2 = PointAt(segment + 1), p3 = PointAt(segment + 2);
		return 0.5f * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
	}

	private Vector3 PointAt(int index)
	{
		if (_closed) return _points[(index % _points.Count + _points.Count) % _points.Count];
		return _points[Mathf.Clamp(index, 0, _points.Count - 1)];
	}

	private void DrawPreview()
	{
		if (_preview == null) return;
		ImmediateMesh mesh = new();
		if (_points.Count >= 2)
		{
			StandardMaterial3D material = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = _splineColor, NoDepthTest = true };
			mesh.SurfaceBegin(Godot.Mesh.PrimitiveType.LineStrip, material);
			int steps = (_closed ? _points.Count : _points.Count - 1) * 24;
			for (int i = 0; i <= steps; i++) mesh.SurfaceAddVertex(SampleCurve(i / (float)steps));
			mesh.SurfaceEnd();
		}
		_preview.Mesh = mesh;
#if CREATOR
		_preview.Visible = true;
#else
		_preview.Visible = false;
#endif
	}

	private void ParsePoints()
	{
		_points.Clear();
		foreach (string encoded in _controlPoints.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string[] values = encoded.Split(',', StringSplitOptions.TrimEntries);
			if (values.Length == 3 && float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) && float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) && float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
				_points.Add(new Vector3(x, y, z));
		}
	}

	private void StorePoints()
	{
		_controlPoints = string.Join(';', _points.Select(static p => string.Create(CultureInfo.InvariantCulture, $"{p.X:R},{p.Y:R},{p.Z:R}")));
		OnPropertyChanged(nameof(ControlPoints));
	}
}

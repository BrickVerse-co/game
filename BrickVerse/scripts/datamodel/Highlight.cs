// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>Draws a configurable fill and outline around a Part or Model.</summary>
[Instantiable]
public sealed partial class Highlight : Instance
{
	private readonly StandardMaterial3D _fillMaterial = new();
	private readonly StandardMaterial3D _outlineMaterial = new();
	private MeshInstance3D? _fillMesh;
	private MeshInstance3D? _outlineMesh;
	private Instance? _adornee;
	private bool _enabled = true;
	private Color _fillColor = new(1, 0, 0);
	private float _fillTransparency = 0.5f;
	private Color _outlineColor = Colors.White;
	private float _outlineTransparency;
	private DepthModeEnum _depthMode = DepthModeEnum.AlwaysOnTop;

	[Editable, ScriptProperty]
	public Instance? Adornee { get => _adornee; set { _adornee = value; OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }

	[Editable, ScriptProperty]
	public Color FillColor { get => _fillColor; set { _fillColor = value; RefreshMaterials(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float FillTransparency { get => _fillTransparency; set { _fillTransparency = Mathf.Clamp(value, 0, 1); RefreshMaterials(); OnPropertyChanged(); } }

	[Editable, ScriptProperty]
	public Color OutlineColor { get => _outlineColor; set { _outlineColor = value; RefreshMaterials(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(0.0f)]
	public float OutlineTransparency { get => _outlineTransparency; set { _outlineTransparency = Mathf.Clamp(value, 0, 1); RefreshMaterials(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(DepthModeEnum.AlwaysOnTop)]
	public DepthModeEnum DepthMode { get => _depthMode; set { _depthMode = value; RefreshMaterials(); OnPropertyChanged(); } }

	public override void Init()
	{
		SetProcess(true);
		base.Init();
	}

	public override void Ready()
	{
		CreateMeshes();
		base.Ready();
	}

	public override void Process(double delta)
	{
		UpdateHighlight();
		base.Process(delta);
	}

	public override void PreDelete()
	{
		_fillMesh?.QueueFree();
		_outlineMesh?.QueueFree();
		base.PreDelete();
	}

	private void CreateMeshes()
	{
		if (_fillMesh != null) return;

		_fillMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		_fillMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		_fillMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

		_outlineMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		_outlineMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		_outlineMaterial.CullMode = BaseMaterial3D.CullModeEnum.Front;

		_fillMesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = Vector3.One, Material = _fillMaterial },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		};
		_outlineMesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = Vector3.One, Material = _outlineMaterial },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		};
		Root.GDNode.AddChild(_fillMesh, @internal: Node.InternalMode.Back);
		Root.GDNode.AddChild(_outlineMesh, @internal: Node.InternalMode.Back);
		RefreshMaterials();
		UpdateHighlight();
	}

	private void RefreshMaterials()
	{
		_fillMaterial.AlbedoColor = new Color(_fillColor, 1 - _fillTransparency);
		_outlineMaterial.AlbedoColor = new Color(_outlineColor, 1 - _outlineTransparency);
		bool alwaysOnTop = _depthMode == DepthModeEnum.AlwaysOnTop;
		_fillMaterial.NoDepthTest = alwaysOnTop;
		_outlineMaterial.NoDepthTest = alwaysOnTop;
	}

	private void UpdateHighlight()
	{
		if (_fillMesh == null || _outlineMesh == null) return;
		Dynamic? target = (_adornee ?? Parent) as Dynamic;
		if (!_enabled || target == null)
		{
			_fillMesh.Visible = false;
			_outlineMesh.Visible = false;
			return;
		}

		Aabb bounds = target.CalculateBounds();
		bool visible = bounds.Size != Vector3.Zero;
		_fillMesh.Visible = visible && _fillTransparency < 1;
		_outlineMesh.Visible = visible && _outlineTransparency < 1;
		if (!visible) return;

		_fillMesh.GlobalTransform = new Transform3D(Basis.FromScale(bounds.Size), bounds.GetCenter());
		Vector3 outlineSize = bounds.Size + Vector3.One * 0.04f;
		_outlineMesh.GlobalTransform = new Transform3D(Basis.FromScale(outlineSize), bounds.GetCenter());
	}

	[ScriptEnum("HighlightDepthMode")]
	public enum DepthModeEnum
	{
		AlwaysOnTop = 1,
		Occluded,
	}
}

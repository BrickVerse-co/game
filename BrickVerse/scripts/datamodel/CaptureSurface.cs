// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A placeable surface displaying a live SceneCapture texture.</summary>
[Instantiable]
public sealed partial class CaptureSurface : Dynamic
{
	private MeshInstance3D _display = null!;
	private QuadMesh _quad = null!;
	private StandardMaterial3D _material = null!;
	private SceneCapture? _source;
	private Color _tint = Colors.White;
	private bool _unshaded = true;
	private bool _doubleSided;

	[Editable, ScriptProperty]
	public SceneCapture? Source
	{
		get => _source;
		set
		{
			if (_source == value) return;
			if (_source != null) _source.TextureChanged.Disconnect(RefreshTexture);
			_source = value;
			if (_source != null) _source.TextureChanged.Connect(RefreshTexture);
			RefreshTexture();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color Tint { get => _tint; set { _tint = value; ApplyMaterial(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Unshaded { get => _unshaded; set { _unshaded = value; ApplyMaterial(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool DoubleSided { get => _doubleSided; set { _doubleSided = value; ApplyMaterial(); OnPropertyChanged(); } }

	public override void Init()
	{
		base.Init();
		_quad = new QuadMesh { Size = new Vector2(Size.X, Size.Y), FlipFaces = true };
		_material = new StandardMaterial3D();
		_display = new MeshInstance3D { Mesh = _quad, MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		GDNode.AddChild(_display, false, Node.InternalMode.Back);
		ApplyMaterial();
	}

	public override void PreDelete()
	{
		if (_source != null) _source.TextureChanged.Disconnect(RefreshTexture);
		base.PreDelete();
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		if (_quad != null) _quad.Size = new Vector2(newSize.X, newSize.Y);
		base.OnNodeSizeChanged(newSize);
	}

	private void RefreshTexture() { if (_material != null) _material.AlbedoTexture = _source?.GetTexture(); }
	private void ApplyMaterial()
	{
		if (_material == null) return;
		_material.AlbedoColor = _tint;
		_material.ShadingMode = _unshaded ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel;
		_material.CullMode = DoubleSided ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back;
		_material.Transparency = _tint.A < 0.999f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
		RefreshTexture();
	}
}

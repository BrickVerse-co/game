// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Data;
using Godot;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class UIGradient : Instance
{
	private ColorSeries _color = ColorSeries.New(Colors.White, Colors.Black);
	private float _rotation;
	private float _transparency;
	private bool _enabled = true;
	private TextureRect? _overlay;

	[Editable, ScriptProperty] public ColorSeries Color { get => _color; set { _color = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0f)] public float Rotation { get => _rotation; set { _rotation = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0f)] public float Transparency { get => _transparency; set { _transparency = Mathf.Clamp(value, 0, 1); Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; Apply(); OnPropertyChanged(); } }

	public override void EnterTree() { Apply(); base.EnterTree(); }
	public override void ExitTree() { Cleanup(); base.ExitTree(); }
	public override void PreDelete() { Cleanup(); base.PreDelete(); }
	public override void HiddenChanged(bool to) { Apply(); base.HiddenChanged(to); }

	private void Apply()
	{
		if (IsHidden || !_enabled || Parent is not UIField field || field.NodeControl == null) { Cleanup(); return; }
		if (_overlay == null)
		{
			_overlay = new TextureRect { MouseFilter = Control.MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
			_overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			_overlay.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mul };
			field.NodeControl.AddChild(_overlay);
		}
		float radians = Mathf.DegToRad(_rotation); Vector2 direction = new(Mathf.Cos(radians), Mathf.Sin(radians));
		_overlay.Texture = new GradientTexture2D { Gradient = _color.ToGradient(), FillFrom = new Vector2(0.5f, 0.5f) - direction * 0.5f, FillTo = new Vector2(0.5f, 0.5f) + direction * 0.5f, Width = 256, Height = 256 };
		_overlay.Modulate = new Color(1, 1, 1, 1 - _transparency);
	}
	private void Cleanup() { _overlay?.QueueFree(); _overlay = null; }
}

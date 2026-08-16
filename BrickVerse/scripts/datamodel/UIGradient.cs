// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Data;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class UIGradient : Instance
{
	private const string TextShaderCode = """
		shader_type canvas_item;
		uniform sampler2D gradient_texture;
		uniform float gradient_rotation = 0.0;
		uniform float gradient_alpha = 1.0;
		void fragment() {
			vec4 source = texture(TEXTURE, UV) * COLOR;
			vec2 direction = vec2(cos(gradient_rotation), sin(gradient_rotation));
			float position = clamp(dot(UV - vec2(0.5), direction) + 0.5, 0.0, 1.0);
			vec4 gradient = texture(gradient_texture, vec2(position, 0.5));
			COLOR = source * vec4(gradient.rgb, mix(1.0, gradient.a, gradient_alpha));
		}
		""";
	private const string BorderShaderCode = """
		shader_type canvas_item;
		uniform float border_width = 0.02;
		void fragment() {
			float edge = min(min(UV.x, 1.0 - UV.x), min(UV.y, 1.0 - UV.y));
			vec4 color = texture(TEXTURE, UV);
			color.a *= 1.0 - step(border_width, edge);
			COLOR = color;
		}
		""";

	private ColorSeries _color = ColorSeries.New(Colors.White, Colors.Black);
	private float _rotation, _transparency, _borderThickness = 2;
	private bool _enabled = true;
	private ApplyToEnum _applyTo = ApplyToEnum.Background;
	private TextureRect? _overlay;
	private UIField? _connectedField;
	private readonly Dictionary<CanvasItem, Material?> _previousTextMaterials = [];

	[Editable, ScriptProperty] public ColorSeries Color { get => _color; set { _color = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0f)] public float Rotation { get => _rotation; set { _rotation = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0f)] public float Transparency { get => _transparency; set { _transparency = Mathf.Clamp(value, 0, 1); Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(ApplyToEnum.Background)] public ApplyToEnum ApplyTo { get => _applyTo; set { _applyTo = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(2f)] public float BorderThickness { get => _borderThickness; set { _borderThickness = Mathf.Max(0, value); Apply(); OnPropertyChanged(); } }

	public override void EnterTree() { ConnectField(); Apply(); base.EnterTree(); }
	public override void ExitTree() { DisconnectField(); Cleanup(); base.ExitTree(); }
	public override void PreDelete() { DisconnectField(); Cleanup(); base.PreDelete(); }
	public override void HiddenChanged(bool to) { Apply(); base.HiddenChanged(to); }
	protected override void OnParentChanged(Instance? oldParent, Instance? newParent) { DisconnectField(); ConnectField(); Apply(); base.OnParentChanged(oldParent, newParent); }

	private void Apply()
	{
		Cleanup();
		if (IsHidden || !_enabled || ResolveField() is not UIField field || field.NodeControl == null) return;
		GradientTexture2D texture = CreateTexture();
		if (_applyTo == ApplyToEnum.Text)
		{
			ApplyToText(field, texture);
			return;
		}

		_overlay = new TextureRect { MouseFilter = Control.MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Texture = texture, Modulate = new Color(1, 1, 1, 1 - _transparency) };
		_overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		if (_applyTo is ApplyToEnum.Border or ApplyToEnum.UIStroke)
		{
			float thickness = ResolveBorderThickness(field);
			float minimumSize = Mathf.Max(1, Mathf.Min(field.AbsoluteSize.X, field.AbsoluteSize.Y));
			ShaderMaterial borderMaterial = new() { Shader = new Shader { Code = BorderShaderCode } };
			borderMaterial.SetShaderParameter("border_width", Mathf.Clamp(thickness / minimumSize, 0, 0.5f));
			_overlay.Material = borderMaterial;
		}
		else
		{
			_overlay.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mul };
		}
		field.NodeControl.AddChild(_overlay);
		if (_applyTo == ApplyToEnum.Background) field.NodeControl.MoveChild(_overlay, 0);
	}

	private UIField? ResolveField() => Parent switch { UIField field => field, UIStroke { Parent: UIField field } => field, _ => null };
	private void ConnectField() { _connectedField = ResolveField(); _connectedField?.TransformChanged.Connect(Apply); }
	private void DisconnectField() { _connectedField?.TransformChanged.Disconnect(Apply); _connectedField = null; }
	private GradientTexture2D CreateTexture()
	{
		float radians = Mathf.DegToRad(_rotation); Vector2 direction = new(Mathf.Cos(radians), Mathf.Sin(radians));
		return new() { Gradient = _color.ToGradient(), FillFrom = new Vector2(0.5f, 0.5f) - direction * 0.5f, FillTo = new Vector2(0.5f, 0.5f) + direction * 0.5f, Width = 256, Height = 256 };
	}
	private void ApplyToText(UIField field, GradientTexture2D texture)
	{
		Shader shader = new() { Code = TextShaderCode };
		foreach (Node node in field.NodeControl.FindChildren("*", "", true, false))
		{
			if (node is not Label and not RichTextLabel and not LineEdit and not TextEdit) continue;
			CanvasItem item = (CanvasItem)node; _previousTextMaterials.TryAdd(item, item.Material);
			ShaderMaterial material = new() { Shader = shader };
			material.SetShaderParameter("gradient_texture", new GradientTexture1D { Gradient = texture.Gradient });
			material.SetShaderParameter("gradient_rotation", Mathf.DegToRad(_rotation)); material.SetShaderParameter("gradient_alpha", 1 - _transparency);
			item.Material = material;
		}
	}
	private float ResolveBorderThickness(UIField field)
	{
		UIStroke? stroke = Parent as UIStroke ?? field.GetChildren().OfType<UIStroke>().FirstOrDefault();
		if (_applyTo == ApplyToEnum.UIStroke && stroke != null) return stroke.Thickness.Compute(Mathf.Min(field.AbsoluteSize.X, field.AbsoluteSize.Y));
		if (field is UIView view && view.BorderWidth > 0) return view.BorderWidth;
		return _borderThickness;
	}
	private void Cleanup()
	{
		_overlay?.QueueFree(); _overlay = null;
		foreach ((CanvasItem item, Material? material) in _previousTextMaterials)
			if (GodotObject.IsInstanceValid(item)) item.Material = material;
		_previousTextMaterials.Clear();
	}

	[ScriptEnum("UIGradientApplyTo")]
	public enum ApplyToEnum { Background, Text, Border, UIStroke, All }
}

using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;
[Instantiable]
public sealed partial class FogEffect : LightingModifier
{
	private bool _enabled = true; private Color _color = Colors.White; private float _density = 0.01f, _height, _heightFalloff = 0.2f;
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Color Color { get => _color; set { _color = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0.01f)] public float Density { get => _density; set { _density = Mathf.Clamp(value, 0, 1); Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float Height { get => _height; set { _height = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0.2f)] public float HeightFalloff { get => _heightFalloff; set { _heightFalloff = Mathf.Max(0, value); Apply(); OnPropertyChanged(); } }
	public override void Ready() { Apply(); base.Ready(); }
	public override void HiddenChanged(bool to) { Apply(); base.HiddenChanged(to); }
	public override void PreDelete() { Root.Lighting.environment.FogEnabled = false; base.PreDelete(); }
	private void Apply() { if (Root?.Lighting?.environment == null) return; bool active = _enabled && !IsHidden; Root.Lighting.environment.FogEnabled = active; if (!active) return; Root.Lighting.environment.FogLightColor = _color; Root.Lighting.environment.FogDensity = _density; Root.Lighting.environment.FogHeight = _height; Root.Lighting.environment.FogHeightDensity = _heightFalloff; }
}

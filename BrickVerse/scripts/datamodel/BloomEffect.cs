using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class BloomEffect : LightingModifier
{
	private bool _enabled = true; private float _intensity = 0.8f, _size = 1, _threshold = 1;
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0.8f)] public float Intensity { get => _intensity; set { _intensity = Mathf.Max(0, value); Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float Size { get => _size; set { _size = Mathf.Clamp(value, 0, 2); Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float Threshold { get => _threshold; set { _threshold = Mathf.Max(0, value); Apply(); OnPropertyChanged(); } }
	public override void Ready() { Apply(); base.Ready(); }
	public override void HiddenChanged(bool to) { Apply(); base.HiddenChanged(to); }
	public override void PreDelete() { Root.Lighting.environment.GlowEnabled = false; base.PreDelete(); }
	private void Apply() { if (Root?.Lighting?.environment == null) return; bool active = _enabled && !IsHidden; Root.Lighting.environment.GlowEnabled = active; if (!active) return; Root.Lighting.environment.GlowIntensity = _intensity; Root.Lighting.environment.GlowStrength = _size; Root.Lighting.environment.GlowBloom = _threshold; }
}

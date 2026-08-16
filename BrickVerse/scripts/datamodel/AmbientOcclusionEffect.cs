using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;
[Instantiable]
public sealed partial class AmbientOcclusionEffect : LightingModifier
{
	private bool _enabled = true; private float _intensity = 2, _radius = 1;
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(2f)] public float Intensity { get => _intensity; set { _intensity = Mathf.Max(0, value); Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float Radius { get => _radius; set { _radius = Mathf.Max(0, value); Apply(); OnPropertyChanged(); } }
	public override void Ready() { Apply(); base.Ready(); }
	public override void HiddenChanged(bool to) { Apply(); base.HiddenChanged(to); }
	public override void PreDelete() { Root.Lighting.environment.SsaoEnabled = false; base.PreDelete(); }
	private void Apply() { if (Root?.Lighting?.environment == null) return; bool active = _enabled && !IsHidden; Root.Lighting.environment.SsaoEnabled = active; if (!active) return; Root.Lighting.environment.SsaoIntensity = _intensity; Root.Lighting.environment.SsaoRadius = _radius; }
}

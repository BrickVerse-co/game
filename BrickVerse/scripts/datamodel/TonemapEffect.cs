using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class TonemapEffect : LightingModifier
{
	private ModeEnum _mode = ModeEnum.Filmic; private float _exposure = 1, _whitePoint = 1;
	[Editable, ScriptProperty, DefaultValue(ModeEnum.Filmic)] public ModeEnum Mode { get => _mode; set { _mode = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float Exposure { get => _exposure; set { _exposure = Mathf.Max(0, value); Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float WhitePoint { get => _whitePoint; set { _whitePoint = Mathf.Max(0.01f, value); Apply(); OnPropertyChanged(); } }
	public override void Ready() { Apply(); base.Ready(); }
	public override void HiddenChanged(bool to) { Apply(); base.HiddenChanged(to); }
	private void Apply() { if (IsHidden || Root?.Lighting?.environment == null) return; Root.Lighting.environment.TonemapMode = _mode switch { ModeEnum.Linear => Godot.Environment.ToneMapper.Linear, ModeEnum.Reinhard => Godot.Environment.ToneMapper.Reinhardt, ModeEnum.Aces => Godot.Environment.ToneMapper.Aces, _ => Godot.Environment.ToneMapper.Filmic }; Root.Lighting.environment.TonemapExposure = _exposure; Root.Lighting.environment.TonemapWhite = _whitePoint; }
	[ScriptEnum("TonemapMode")] public enum ModeEnum { Linear, Reinhard, Filmic, Aces }
}

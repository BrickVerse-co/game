using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class AudioEqualizer : AudioProcessor
{
	private float _low, _mid, _high;
	[Editable, ScriptProperty, DefaultValue(0f)] public float LowGain { get => _low; set { _low = Mathf.Clamp(value, -60, 24); ApplyEffect(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0f)] public float MidGain { get => _mid; set { _mid = Mathf.Clamp(value, -60, 24); ApplyEffect(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0f)] public float HighGain { get => _high; set { _high = Mathf.Clamp(value, -60, 24); ApplyEffect(); OnPropertyChanged(); } }
	protected override AudioEffect CreateEffect() => new AudioEffectEQ10();
	protected override void ConfigureEffect(AudioEffect effect)
	{
		AudioEffectEQ10 eq = (AudioEffectEQ10)effect;
		for (int band = 0; band < 10; band++) eq.SetBandGainDb(band, band <= 2 ? _low : band <= 6 ? _mid : _high);
	}
}

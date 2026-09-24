using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class AudioReverb : AudioProcessor
{
	private float _wet = 0.35f, _dry = 1f, _roomSize = 0.5f, _damping = 0.5f;
	[Editable, ScriptProperty, DefaultValue(0.35f)] public float Wet { get => _wet; set { _wet = Mathf.Clamp(value, 0, 1); ApplyEffect(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float Dry { get => _dry; set { _dry = Mathf.Clamp(value, 0, 1); ApplyEffect(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0.5f)] public float RoomSize { get => _roomSize; set { _roomSize = Mathf.Clamp(value, 0, 1); ApplyEffect(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0.5f)] public float Damping { get => _damping; set { _damping = Mathf.Clamp(value, 0, 1); ApplyEffect(); OnPropertyChanged(); } }
	protected override AudioEffect CreateEffect() => new AudioEffectReverb();
	protected override void ConfigureEffect(AudioEffect effect)
	{
		AudioEffectReverb reverb = (AudioEffectReverb)effect;
		reverb.Wet = _wet; reverb.Dry = _dry; reverb.RoomSize = _roomSize; reverb.Damping = _damping;
	}
}

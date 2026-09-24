using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class AudioFader : AudioProcessor
{
	private float _volume = 1f;
	private bool _bypass;
	[Editable, ScriptProperty, DefaultValue(1f)] public float Volume { get => _volume; set { _volume = Mathf.Clamp(value, 0, 4); ApplyEffect(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(false)] public bool Bypass { get => _bypass; set { _bypass = value; ApplyEffect(); OnPropertyChanged(); } }
	protected override AudioEffect CreateEffect() => new AudioEffectAmplify();
	protected override void ConfigureEffect(AudioEffect effect) => ((AudioEffectAmplify)effect).VolumeDb = _bypass ? 0 : Mathf.LinearToDb(Mathf.Max(_volume, 0.0001f));
}

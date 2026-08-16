using BrickVerse.Attributes;
using Godot;
using System.Linq;
namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class SoundGroup : Instance
{
	private float _volume = 1; private bool _muted, _solo;
	internal string BusName => $"BV_{NetworkedObjectID}";
	[Editable, ScriptProperty, DefaultValue(1f)] public float Volume { get => _volume; set { _volume = Mathf.Clamp(value, 0, 4); Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public bool Muted { get => _muted; set { _muted = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public bool Solo { get => _solo; set { _solo = value; Apply(); OnPropertyChanged(); } }
	public override void Ready() { EnsureBus(); Apply(); RefreshSounds(); base.Ready(); }
	public override void PreDelete() { foreach (Sound sound in Root.GetDescendants().OfType<Sound>().Where(s => s.SoundGroup == this)) sound.SoundGroup = null; int index = AudioServer.GetBusIndex(BusName); if (index >= 0) AudioServer.RemoveBus(index); base.PreDelete(); }
	private void EnsureBus() { if (AudioServer.GetBusIndex(BusName) >= 0) return; AudioServer.AddBus(); AudioServer.SetBusName(AudioServer.BusCount - 1, BusName); }
	private void Apply() { EnsureBus(); int index = AudioServer.GetBusIndex(BusName); if (index < 0) return; AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(_volume, 0.0001f))); AudioServer.SetBusMute(index, _muted); AudioServer.SetBusSolo(index, _solo); }
	private void RefreshSounds() { foreach (Sound sound in Root.GetDescendants().OfType<Sound>().Where(s => s.SoundGroup == this)) sound.UpdateSoundGroup(); }
}

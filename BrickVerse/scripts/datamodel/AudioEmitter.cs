using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;
using System.Linq;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class AudioEmitter : Dynamic, IAudioWireNode
{
	private float _maxDistance = 100f, _attenuation = 1f, _panning = 1f, _emissionAngle = 360f;
	private bool _doppler;
	private string _interactionGroup = "Default";
	internal string[] InputPins => ["Input"];
	internal string[] OutputPins => ["Output"];
	[ScriptProperty] public BVSignal<bool, string, AudioWire, Instance> WiringChanged { get; private set; } = new();
	[Editable, ScriptProperty, DefaultValue(100f)] public float MaxDistance { get => _maxDistance; set { _maxDistance = Mathf.Max(0.1f, value); AudioGraph.Rebuild(Root); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float AttenuationStrength { get => _attenuation; set { _attenuation = Mathf.Max(0.01f, value); AudioGraph.Rebuild(Root); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float PanningStrength { get => _panning; set { _panning = Mathf.Clamp(value, 0, 3); AudioGraph.Rebuild(Root); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(false)] public bool DopplerEnabled { get => _doppler; set { _doppler = value; AudioGraph.Rebuild(Root); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(360f)] public float EmissionAngle { get => _emissionAngle; set { _emissionAngle = Mathf.Clamp(value, 1, 360); AudioGraph.Rebuild(Root); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public string AudioInteractionGroup { get => _interactionGroup; set { _interactionGroup = value?.Trim() ?? ""; OnPropertyChanged(); } }
	[ScriptMethod] public string[] GetInputPins() => [.. InputPins];
	[ScriptMethod] public string[] GetOutputPins() => [.. OutputPins];
	[ScriptMethod] public AudioWire[] GetConnectedWires(string pin) => Root.GetDescendants().OfType<AudioWire>().Where(w => w.Connected && ((w.SourceInstance == this && w.SourceName == pin) || (w.TargetInstance == this && w.TargetName == pin))).ToArray();
	internal bool HasInputPin(string pin) => pin.Equals("Input", System.StringComparison.OrdinalIgnoreCase);
	internal bool HasOutputPin(string pin) => pin.Equals("Output", System.StringComparison.OrdinalIgnoreCase);
	internal void EmitWiringChanged(bool connected, string pin, AudioWire wire, Instance other) => WiringChanged.Invoke(connected, pin, wire, other);
	string[] IAudioWireNode.InputPins => InputPins;
	string[] IAudioWireNode.OutputPins => OutputPins;
	bool IAudioWireNode.HasInputPin(string pin) => HasInputPin(pin);
	bool IAudioWireNode.HasOutputPin(string pin) => HasOutputPin(pin);
	void IAudioWireNode.EmitWiringChanged(bool connected, string pin, AudioWire wire, Instance other) => EmitWiringChanged(connected, pin, wire, other);
	public override void PreDelete()
	{
		if (Root != null) foreach (AudioWire wire in Root.GetDescendants().OfType<AudioWire>().Where(w => w.SourceInstance == this || w.TargetInstance == this).ToArray())
		{
			if (wire.SourceInstance == this) wire.SourceInstance = null;
			if (wire.TargetInstance == this) wire.TargetInstance = null;
		}
		WiringChanged.DisconnectAll();
		base.PreDelete();
	}
}

using BrickVerse.Attributes;
using BrickVerse.Datamodel.Services;
using Godot;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class AudioDeviceOutput : AudioNode
{
	private float _volume = 1f;
	private bool _muted;
	internal override string[] InputPins => ["Input"];
	internal override string[] OutputPins => [];
	internal string BusName => $"BVAudioOutput_{NetworkedObjectID}";

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Volume
	{
		get => _volume;
		set
		{
			_volume = Mathf.Clamp(value, 0, 4);
			Apply();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool Muted
	{
		get => _muted;
		set
		{
			_muted = value;
			Apply();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Player? Player { get; set; }

	[ScriptProperty, SaveIgnore, CloneIgnore]
	public string DeviceName => AudioServer.OutputDevice;

	public override void Ready()
	{
		Apply();
		base.Ready();
	}

	internal void Apply()
	{
		if (AudioServer.GetBusIndex(BusName) < 0)
		{
			AudioServer.AddBus();
			AudioServer.SetBusName(AudioServer.BusCount - 1, BusName);
		}
		int index = AudioServer.GetBusIndex(BusName);
		if (index < 0)
			return;
		AudioServer.SetBusSend(index, Root?.FindChild<SoundService>("SoundService")?.BusName ?? "Master");
		AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(_volume, 0.0001f)));
		AudioServer.SetBusMute(index, _muted);
	}

	public override void PreDelete()
	{
		int index = AudioServer.GetBusIndex(BusName);
		if (index >= 0)
			AudioServer.RemoveBus(index);
		base.PreDelete();
	}
}

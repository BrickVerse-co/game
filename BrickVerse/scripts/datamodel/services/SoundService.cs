// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Datamodel.Services;

/// <summary>Controls world-wide audio acoustics, spatialization, and the default output/listener.</summary>
[Static("SoundService")]
public sealed partial class SoundService : Instance
{
	private float _dopplerScale = 1f;
	private float _distanceFactor = 1f;
	private AmbientReverbEnum _ambientReverb;
	private VolumetricAudioEnum _volumetricAudio = VolumetricAudioEnum.Automatic;
	private DefaultListenerLocationEnum _defaultListenerLocation = DefaultListenerLocationEnum.Default;
	private bool _autoCreateDeviceOutput = true;
	private AudioDeviceOutput? _automaticOutput;
	private AudioListener3D? _automaticListener;

	internal string BusName => $"BVSoundService_{NetworkedObjectID}";

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float DopplerScale { get => _dopplerScale; set { _dopplerScale = Mathf.Clamp(value, 0, 10); RefreshGraph(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float DistanceFactor { get => _distanceFactor; set { _distanceFactor = Mathf.Clamp(value, 0.001f, 1000); RefreshGraph(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(AmbientReverbEnum.Off)]
	public AmbientReverbEnum AmbientReverb { get => _ambientReverb; set { _ambientReverb = value; ApplyAmbientReverb(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(VolumetricAudioEnum.Automatic)]
	public VolumetricAudioEnum VolumetricAudio { get => _volumetricAudio; set { _volumetricAudio = value; RefreshGraph(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(DefaultListenerLocationEnum.Default)]
	public DefaultListenerLocationEnum DefaultListenerLocation { get => _defaultListenerLocation; set { _defaultListenerLocation = value; UpdateListener(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool AutoCreateDeviceOutput { get => _autoCreateDeviceOutput; set { _autoCreateDeviceOutput = value; UpdateAutomaticOutput(); OnPropertyChanged(); } }

	[ScriptProperty, SaveIgnore, CloneIgnore]
	public AudioDeviceOutput? DefaultDeviceOutput => _automaticOutput;

	public override void Init()
	{
		SetProcess(true);
		base.Init();
	}

	public override void Ready()
	{
		EnsureBus();
		ApplyAmbientReverb();
		UpdateAutomaticOutput();
		foreach (AudioDeviceOutput output in Root.GetDescendants().OfType<AudioDeviceOutput>()) output.Apply();
		UpdateListener();
		base.Ready();
	}

	public override void Process(double delta) => UpdateListener();

	internal bool ShouldSpatialize(AudioEmitter? emitter) => _volumetricAudio switch
	{
		VolumetricAudioEnum.Disabled => false,
		VolumetricAudioEnum.Enabled => emitter != null,
		_ => emitter != null,
	};

	private void UpdateAutomaticOutput()
	{
		if (Root == null) return;
		_automaticOutput = Root.GetDescendants().OfType<AudioDeviceOutput>()
			.FirstOrDefault(output => output.Name == "DefaultAudioDeviceOutput");
		if (_autoCreateDeviceOutput)
		{
			if (Root.GetDescendants().OfType<AudioDeviceOutput>().Any()) return;
			_automaticOutput = Globals.LoadInstance<AudioDeviceOutput>(Root);
			_automaticOutput.NameOverride = "DefaultAudioDeviceOutput";
			_automaticOutput.NetworkParent = this;
			AudioGraph.Rebuild(Root);
		}
		else if (_automaticOutput != null && !_automaticOutput.IsDeleted)
		{
			AudioDeviceOutput output = _automaticOutput;
			_automaticOutput = null;
			output.Delete();
			AudioGraph.Rebuild(Root);
		}
	}

	private void UpdateListener()
	{
		if (Root == null || Root.Network.IsServer) return;
		if (_defaultListenerLocation == DefaultListenerLocationEnum.Disabled)
		{
			_automaticListener?.ClearCurrent();
			return;
		}

		Node3D? target = _defaultListenerLocation == DefaultListenerLocationEnum.Character
			? Root.Players.LocalPlayer?.Character?.GDNode3D
			: Root.Environment.CurrentCamera?.GDNode3D;
		if (target == null) return;
		if (_automaticListener == null)
		{
			_automaticListener = new AudioListener3D { Name = "DefaultAudioListener" };
			target.GetTree().Root.AddChild(_automaticListener);
		}
		_automaticListener.GlobalTransform = target.GlobalTransform;
		_automaticListener.MakeCurrent();
	}

	private void EnsureBus()
	{
		if (AudioServer.GetBusIndex(BusName) >= 0) return;
		AudioServer.AddBus();
		int index = AudioServer.BusCount - 1;
		AudioServer.SetBusName(index, BusName);
		AudioServer.SetBusSend(index, "Master");
	}

	private void ApplyAmbientReverb()
	{
		if (Root == null || Root.Network.IsServer) return;
		EnsureBus();
		int index = AudioServer.GetBusIndex(BusName);
		if (index < 0) return;
		while (AudioServer.GetBusEffectCount(index) > 0) AudioServer.RemoveBusEffect(index, 0);
		if (_ambientReverb == AmbientReverbEnum.Off) return;
		(float room, float damping, float wet) = _ambientReverb switch
		{
			AmbientReverbEnum.SmallRoom => (0.25f, 0.7f, 0.18f),
			AmbientReverbEnum.MediumRoom => (0.45f, 0.6f, 0.24f),
			AmbientReverbEnum.LargeRoom => (0.7f, 0.5f, 0.32f),
			AmbientReverbEnum.Hall => (0.85f, 0.38f, 0.4f),
			AmbientReverbEnum.Cave => (0.95f, 0.22f, 0.48f),
			AmbientReverbEnum.Arena => (1f, 0.32f, 0.36f),
			AmbientReverbEnum.Underwater => (0.6f, 0.88f, 0.42f),
			_ => (0.5f, 0.5f, 0.25f),
		};
		AudioServer.AddBusEffect(index, new AudioEffectReverb { RoomSize = room, Damping = damping, Wet = wet, Dry = 1f });
	}

	private void RefreshGraph()
	{
		if (Root != null) AudioGraph.Rebuild(Root);
	}

	public override void PreDelete()
	{
		if (_automaticListener != null) { _automaticListener.QueueFree(); _automaticListener = null; }
		int index = AudioServer.GetBusIndex(BusName);
		if (index >= 0) AudioServer.RemoveBus(index);
		base.PreDelete();
	}

	[ScriptEnum("AmbientReverb")]
	public enum AmbientReverbEnum { Off, SmallRoom, MediumRoom, LargeRoom, Hall, Cave, Arena, Underwater }

	[ScriptEnum("VolumetricAudio")]
	public enum VolumetricAudioEnum { Automatic, Enabled, Disabled }

	[ScriptEnum("DefaultAudioListenerLocation")]
	public enum DefaultListenerLocationEnum { Default, Camera, Character, Disabled }
}

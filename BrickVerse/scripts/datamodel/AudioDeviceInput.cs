using System.Collections.Generic;
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Services;
using Godot;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class AudioDeviceInput : AudioNode, IAudioSource
{
	private readonly List<Node> _players = [];
	private bool _active = true, _muted;
	private float _volume = 1f;
	internal override string[] InputPins => [];
	internal override string[] OutputPins => ["Output"];
	[Editable, ScriptProperty, DefaultValue(true)] public bool Active { get => _active; set { _active = value; RebuildAudioRoutes(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(false)] public bool Muted { get => _muted; set { _muted = value; ApplyVolume(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float Volume { get => _volume; set { _volume = Mathf.Clamp(value, 0, 4); ApplyVolume(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Player? Player { get; set; }
	[ScriptProperty, SaveIgnore, CloneIgnore] public bool IsReady => AudioServer.GetInputDeviceList().Length > 0;
	public override void Ready() { RebuildAudioRoutes(); base.Ready(); }
	void IAudioSource.RebuildAudioRoutes() => RebuildAudioRoutes();
	internal void RebuildAudioRoutes()
	{
		foreach (Node node in _players) node.QueueFree(); _players.Clear();
		if (!_active || Root == null || Root.Network.IsServer) return;
		AudioServer.SetInputDeviceActive(true);
		foreach (AudioGraph.Route route in AudioGraph.ResolveRoutes(this))
		{
			Node player;
			SoundService? soundService = Root.FindChild<SoundService>("SoundService");
			if (route.Emitter != null && (soundService?.ShouldSpatialize(route.Emitter) ?? true))
			{
				AudioEmitter emitter = route.Emitter;
				AudioStreamPlayer3D spatial = new() { Stream = new AudioStreamMicrophone(), Bus = route.Bus, MaxDistance = emitter.MaxDistance * (soundService?.DistanceFactor ?? 1f), UnitSize = emitter.AttenuationStrength * (soundService?.DistanceFactor ?? 1f), PanningStrength = emitter.PanningStrength, DopplerTracking = emitter.DopplerEnabled && (soundService?.DopplerScale ?? 1f) > 0 ? AudioStreamPlayer3D.DopplerTrackingEnum.IdleStep : AudioStreamPlayer3D.DopplerTrackingEnum.Disabled };
				emitter.GDNode3D.AddChild(spatial, @internal: Node.InternalMode.Back); spatial.Play(); player = spatial;
			}
			else { AudioStreamPlayer flat = new() { Stream = new AudioStreamMicrophone(), Bus = route.Bus }; GDNode.AddChild(flat, @internal: Node.InternalMode.Back); flat.Play(); player = flat; }
			_players.Add(player);
		}
		ApplyVolume();
	}
	private void ApplyVolume() { float volume = _muted ? 0 : _volume; foreach (Node node in _players) { if (node is AudioStreamPlayer p) p.VolumeLinear = volume; else if (node is AudioStreamPlayer3D p3) p3.VolumeLinear = volume; } }
	public override void PreDelete() { foreach (Node node in _players) node.QueueFree(); _players.Clear(); base.PreDelete(); }
}

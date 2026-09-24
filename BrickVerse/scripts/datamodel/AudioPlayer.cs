// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Data;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Datamodel.Services;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>Asset-backed audio source whose output is routed exclusively through Wires.</summary>
[Instantiable]
public sealed partial class AudioPlayer : AudioNode, IAudioSource
{
	private AudioAsset? _asset;
	private AudioStream? _stream;
	private readonly List<Node> _players = [];
	private bool _autoLoad = true, _autoPlay, _looping;
	private bool _autoPlayConsumed;
	private float _volume = 1f, _playbackSpeed = 1f, _pendingTime;
	private NumberRange _loopRegion, _playbackRegion;
	internal override string[] InputPins => [];
	internal override string[] OutputPins => ["Output"];

	[ScriptProperty] public BVSignal Ended { get; private set; } = new();
	[ScriptProperty] public BVSignal Looped { get; private set; } = new();

	[Editable, ScriptProperty]
	public AudioAsset? Audio
	{
		get => _asset;
		set
		{
			if (_asset == value) return;
			if (_asset != null) { _asset.ResourceLoaded -= OnResourceLoaded; _asset.UnlinkFrom(this); }
			_asset = value; _stream = null; _autoPlayConsumed = false;
			if (_asset != null)
			{
				_asset.LinkTo(this); _asset.ResourceLoaded += OnResourceLoaded;
				if (_asset.IsResourceLoaded && _asset.Resource is AudioStream stream) OnResourceLoaded(stream);
				else if (_autoLoad) _asset.QueueLoadResource();
			}
			RebuildAudioRoutes(); OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)] public bool AutoLoad { get => _autoLoad; set { _autoLoad = value; if (value && _asset?.IsResourceLoaded == false) _asset.QueueLoadResource(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(false)] public bool AutoPlay { get => _autoPlay; set { _autoPlay = value; if (value && !_autoPlayConsumed) RebuildAudioRoutes(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(false)] public bool Looping { get => _looping; set { _looping = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float Volume { get => _volume; set { _volume = Mathf.Clamp(value, 0, 10); ApplyPlaybackSettings(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float PlaybackSpeed { get => _playbackSpeed; set { _playbackSpeed = Mathf.Clamp(value, 0.01f, 20); ApplyPlaybackSettings(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public NumberRange LoopRegion { get => _loopRegion; set { _loopRegion = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public NumberRange PlaybackRegion { get => _playbackRegion; set { _playbackRegion = value; OnPropertyChanged(); } }
	[ScriptProperty, SaveIgnore, CloneIgnore] public bool IsReady => _stream != null;
	[ScriptProperty, SaveIgnore, CloneIgnore] public bool IsPlaying => _players.Any(IsNodePlaying);
	[ScriptProperty, SaveIgnore, CloneIgnore] public float TimeLength => (float)(_stream?.GetLength() ?? 0);
	[ScriptProperty]
	public float TimePosition
	{
		get => _players.Select(GetPlaybackPosition).FirstOrDefault(value => value >= 0);
		set { _pendingTime = Mathf.Clamp(value, 0, Mathf.Max(TimeLength, 0)); foreach (Node node in _players) Seek(node, _pendingTime); OnPropertyChanged(); }
	}

	public override void Ready()
	{
		SetProcess(true);
		RebuildAudioRoutes();
		base.Ready();
	}

	public override void Process(double delta)
	{
		if (!IsPlaying || _playbackRegion.Max <= _playbackRegion.Min) return;
		if (TimePosition < _playbackRegion.Max) return;
		if (_looping) { TimePosition = EffectiveLoopStart(); Looped.Invoke(); }
		else Stop();
	}

	[ScriptMethod]
	public void Play(float atTime = -1)
	{
		if (_stream == null) { _asset?.QueueLoadResource(); return; }
		if (_players.Count == 0) RebuildAudioRoutes();
		float start = atTime >= 0 ? atTime : (_pendingTime > 0 ? _pendingTime : Mathf.Max(0, _playbackRegion.Min));
		foreach (Node node in _players) PlayNode(node, start);
		if (_players.Count > 0) _autoPlayConsumed = true;
	}

	[ScriptMethod] public void Pause() { foreach (Node node in _players) SetPaused(node, true); }
	[ScriptMethod] public void Resume() { foreach (Node node in _players) SetPaused(node, false); }
	[ScriptMethod] public void Stop() { foreach (Node node in _players) StopNode(node); _pendingTime = 0; }

	void IAudioSource.RebuildAudioRoutes() => RebuildAudioRoutes();
	internal void RebuildAudioRoutes()
	{
		bool resume = IsPlaying;
		float position = TimePosition;
		ClearPlayers();
		if (_stream == null || Root == null || Root.Network.IsServer) return;
		foreach (AudioGraph.Route route in AudioGraph.ResolveRoutes(this))
		{
			Node player;
			SoundService? soundService = Root.FindChild<SoundService>("SoundService");
			if (route.Emitter != null && (soundService?.ShouldSpatialize(route.Emitter) ?? true))
			{
				AudioEmitter emitter = route.Emitter;
				AudioStreamPlayer3D spatial = new()
				{
					Stream = _stream, Bus = route.Bus, MaxDistance = emitter.MaxDistance * (soundService?.DistanceFactor ?? 1f),
					UnitSize = emitter.AttenuationStrength * (soundService?.DistanceFactor ?? 1f), PanningStrength = emitter.PanningStrength,
					DopplerTracking = emitter.DopplerEnabled && (soundService?.DopplerScale ?? 1f) > 0 ? AudioStreamPlayer3D.DopplerTrackingEnum.IdleStep : AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
					EmissionAngleEnabled = emitter.EmissionAngle < 360, EmissionAngleDegrees = emitter.EmissionAngle,
				};
				emitter.GDNode3D.AddChild(spatial, @internal: Node.InternalMode.Back); player = spatial;
			}
			else
			{
				AudioStreamPlayer flat = new() { Stream = _stream, Bus = route.Bus };
				GDNode.AddChild(flat, @internal: Node.InternalMode.Back); player = flat;
			}
			_players.Add(player); ConnectFinished(player);
		}
		ApplyPlaybackSettings();
		if (resume) foreach (Node node in _players) PlayNode(node, position);
		else if (_autoPlay && !_autoPlayConsumed && _players.Count > 0) Play();
	}

	private void OnResourceLoaded(Resource resource)
	{
		if (resource is not AudioStream stream) return;
		_stream = stream; RebuildAudioRoutes();
	}

	private void OnFinished()
	{
		if (_looping)
		{
			float start = EffectiveLoopStart(); foreach (Node node in _players) PlayNode(node, start); Looped.Invoke();
		}
		else if (!IsPlaying) Ended.Invoke();
	}

	private float EffectiveLoopStart() => _loopRegion.Max > _loopRegion.Min ? Mathf.Max(_loopRegion.Min, _playbackRegion.Min) : Mathf.Max(0, _playbackRegion.Min);
	private void ApplyPlaybackSettings() { foreach (Node node in _players) { if (node is AudioStreamPlayer p) { p.VolumeLinear = _volume; p.PitchScale = _playbackSpeed; } else if (node is AudioStreamPlayer3D p3) { p3.VolumeLinear = _volume; p3.PitchScale = _playbackSpeed; } } }
	private void ConnectFinished(Node node) { if (node is AudioStreamPlayer p) p.Finished += OnFinished; else if (node is AudioStreamPlayer3D p3) p3.Finished += OnFinished; }
	private static bool IsNodePlaying(Node node) => node is AudioStreamPlayer p ? p.Playing : node is AudioStreamPlayer3D p3 && p3.Playing;
	private static float GetPlaybackPosition(Node node) => node is AudioStreamPlayer p ? p.GetPlaybackPosition() : node is AudioStreamPlayer3D p3 ? p3.GetPlaybackPosition() : -1;
	private static void PlayNode(Node node, float from) { if (node is AudioStreamPlayer p) p.Play(from); else if (node is AudioStreamPlayer3D p3) p3.Play(from); }
	private static void StopNode(Node node) { if (node is AudioStreamPlayer p) p.Stop(); else if (node is AudioStreamPlayer3D p3) p3.Stop(); }
	private static void Seek(Node node, float to) { if (node is AudioStreamPlayer p) p.Seek(to); else if (node is AudioStreamPlayer3D p3) p3.Seek(to); }
	private static void SetPaused(Node node, bool paused) { if (node is AudioStreamPlayer p) p.StreamPaused = paused; else if (node is AudioStreamPlayer3D p3) p3.StreamPaused = paused; }
	private void ClearPlayers() { foreach (Node node in _players) { if (node is AudioStreamPlayer p) p.Finished -= OnFinished; else if (node is AudioStreamPlayer3D p3) p3.Finished -= OnFinished; node.QueueFree(); } _players.Clear(); }
	public override void PreDelete() { if (_asset != null) { _asset.ResourceLoaded -= OnResourceLoaded; _asset.UnlinkFrom(this); } ClearPlayers(); Ended.DisconnectAll(); Looped.DisconnectAll(); base.PreDelete(); }
}

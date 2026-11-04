// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using BrickVerse.Attributes;
using BrickVerse.Networking;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>A reusable server-authoritative cooldown with progress and completion signals.</summary>
[Instantiable]
public sealed partial class Cooldown : Instance
{
	private double _duration = 1, _remaining;
	[Editable, ScriptProperty, DefaultValue(1d)] public double Duration { get => _duration; set { _duration = Math.Max(0, double.IsFinite(value) ? value : 1); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(false)] public bool StartReady { get; set; }
	[ScriptProperty, SyncVar] public double Remaining { get => _remaining; private set { _remaining = Math.Max(0, value); OnPropertyChanged(); OnPropertyChanged(nameof(IsReady)); OnPropertyChanged(nameof(Progress)); } }
	[ScriptProperty] public bool IsReady => _remaining <= 0;
	[ScriptProperty] public double Progress => _duration <= 0 ? 1 : 1 - Math.Clamp(_remaining / _duration, 0, 1);
	[ScriptProperty] public BVSignal Started { get; private set; } = new();
	[ScriptProperty] public BVSignal BecameReady { get; private set; } = new();
	public override void Init() { base.Init(); Remaining = StartReady ? 0 : _duration; SetProcess(true); }
	public override void Process(double delta) { base.Process(delta); if (!HasAuthority || Remaining <= 0) return; double before = Remaining; Remaining -= delta; if (before > 0 && IsReady) BecameReady.Invoke(); }
	[ScriptMethod] public bool TryUse() { if (!HasAuthority || !IsReady) return false; Restart(); return true; }
	[ScriptMethod] public void Restart(double duration = -1) { if (!HasAuthority) return; if (duration >= 0) Duration = duration; Remaining = _duration; Started.Invoke(); if (IsReady) BecameReady.Invoke(); }
	[ScriptMethod] public void Cancel() { if (!HasAuthority) return; bool active = !IsReady; Remaining = 0; if (active) BecameReady.Invoke(); }
	[ScriptMethod] public void Reduce(double seconds) { if (HasAuthority) Remaining -= Math.Max(0, seconds); }
}

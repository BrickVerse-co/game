// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Networking;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>A child-driven finite state machine suitable for AI, UI, rounds, and interactions.</summary>
[Instantiable]
public sealed partial class StateMachine : Instance
{
	private State? _current;
	[Editable, ScriptProperty, DefaultValue(true)] public bool AutoStart { get; set; } = true;
	[ScriptProperty, SyncVar] public State? CurrentState { get => _current; private set { _current = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentStateName)); } }
	[ScriptProperty] public string CurrentStateName => _current?.Name ?? "";
	[ScriptProperty] public BVSignal<State?, State?> StateChanged { get; private set; } = new();
	public override void Ready() { base.Ready(); if (HasAuthority && AutoStart && _current == null) { State? initial = GetStates().FirstOrDefault(s => s.Initial && s.Enabled) ?? GetStates().FirstOrDefault(s => s.Enabled); if (initial != null) TransitionTo(initial); } }
	[ScriptMethod] public State[] GetStates() => [.. GetChildren().OfType<State>()];
	[ScriptMethod] public State? FindState(string name) => GetStates().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
	[ScriptMethod] public bool Transition(string name) { State? state = FindState(name); return state != null && TransitionTo(state); }
	[ScriptMethod] public bool TransitionTo(State state) { if (!HasAuthority || state == null || state.Parent != this || !state.Enabled || state == _current) return false; State? previous = _current; previous?.SetActive(false); CurrentState = state; state.SetActive(true); StateChanged.Invoke(previous, state); return true; }
	[ScriptMethod] public void Stop() { if (!HasAuthority) return; State? previous = _current; previous?.SetActive(false); CurrentState = null; if (previous != null) StateChanged.Invoke(previous, null); }
#if CREATOR
	public override void CreatorInserted()
	{
		State initial = Root.New<State>(); initial.Name = "Idle"; initial.Initial = true; initial.Parent = this;
		base.CreatorInserted();
	}
#endif
}

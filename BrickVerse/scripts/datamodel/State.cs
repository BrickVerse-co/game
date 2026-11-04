// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>A named state that can be enabled, disabled, and entered by a StateMachine.</summary>
[Instantiable]
public sealed partial class State : Instance
{
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get; set; } = true;
	[Editable, ScriptProperty, DefaultValue(false)] public bool Initial { get; set; }
	[Editable, ScriptProperty, DefaultValue("")] public string Tag { get; set; } = "";
	[ScriptProperty, SaveIgnore, CloneIgnore] public bool IsActive { get; internal set; }
	[ScriptProperty] public BVSignal Entered { get; private set; } = new();
	[ScriptProperty] public BVSignal Exited { get; private set; } = new();
	internal void SetActive(bool active) { if (IsActive == active) return; IsActive = active; OnPropertyChanged(nameof(IsActive)); if (active) Entered.Invoke(); else Exited.Invoke(); }
}

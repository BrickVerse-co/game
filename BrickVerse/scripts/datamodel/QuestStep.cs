// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Linq;
using BrickVerse.Attributes;

namespace BrickVerse.Datamodel;

/// <summary>An ordered quest step containing Objective children.</summary>
[Instantiable]
public sealed partial class QuestStep : Instance
{
	[Editable, ScriptProperty] public string Description { get; set; } = "";
	[Editable, ScriptProperty, DefaultValue(false)] public bool Optional { get; set; }
	[ScriptProperty] public bool Completed => GetObjectives().All(o => o.Optional || o.Completed);
	[ScriptProperty] public bool Failed => GetObjectives().Any(o => !o.Optional && o.Failed);
	[ScriptMethod] public Objective[] GetObjectives() => [.. GetChildren().OfType<Objective>()];
	[ScriptMethod] public void Reset() { if (!HasAuthority) return; foreach (Objective objective in GetObjectives()) objective.Reset(); OnPropertyChanged(nameof(Completed)); OnPropertyChanged(nameof(Failed)); }
}

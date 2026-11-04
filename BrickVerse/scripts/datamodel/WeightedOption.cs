// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;

namespace BrickVerse.Datamodel;

/// <summary>A weighted string or object result used by RandomSelector.</summary>
[Instantiable]
public sealed partial class WeightedOption : Instance
{
	[Editable, ScriptProperty, DefaultValue(1d)] public double Weight { get; set; } = 1;
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get; set; } = true;
	[Editable, ScriptProperty] public string Value { get; set; } = "";
	[Editable, ScriptProperty] public Instance? ObjectValue { get; set; }
}

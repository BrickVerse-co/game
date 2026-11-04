// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using BrickVerse.Attributes;

namespace BrickVerse.Datamodel;

/// <summary>A weighted loot result with quantity range, chance, and an optional referenced object.</summary>
[Instantiable]
public sealed partial class LootEntry : Instance
{
	[Editable, ScriptProperty] public string ItemId { get; set; } = "";
	[Editable, ScriptProperty] public Instance? Item { get; set; }
	[Editable, ScriptProperty, DefaultValue(1)] public int MinimumQuantity { get; set; } = 1;
	[Editable, ScriptProperty, DefaultValue(1)] public int MaximumQuantity { get; set; } = 1;
	[Editable, ScriptProperty, DefaultValue(1d)] public double Weight { get; set; } = 1;
	[Editable, ScriptProperty, DefaultValue(1d)] public double Chance { get; set; } = 1;
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get; set; } = true;
	[ScriptMethod] public int RollQuantity() { int min = Math.Max(0, MinimumQuantity), max = Math.Max(min, MaximumQuantity); return max == int.MaxValue ? Random.Shared.Next(min, max) : Random.Shared.Next(min, max + 1); }
}

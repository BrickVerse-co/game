// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>A weighted loot table supporting multiple rolls, chance, quantity, and duplicate control.</summary>
[Instantiable]
public sealed partial class LootTable : Instance
{
	[Editable, ScriptProperty, DefaultValue(1)] public int Rolls { get; set; } = 1;
	[Editable, ScriptProperty, DefaultValue(true)] public bool AllowDuplicates { get; set; } = true;
	[ScriptProperty] public BVSignal<LootEntry, int> Dropped { get; private set; } = new();
	[ScriptMethod] public LootEntry[] GetEntries() => [.. GetChildren().OfType<LootEntry>().Where(e => e.Enabled && double.IsFinite(e.Weight) && e.Weight > 0)];
	[ScriptMethod]
	public LootEntry[] Roll()
	{
		if (!HasAuthority) return [];
		List<LootEntry> available = [.. GetEntries()]; List<LootEntry> results = [];
		for (int i = 0; i < Math.Max(0, Rolls) && available.Count > 0; i++)
		{
			double cursor = Random.Shared.NextDouble() * available.Sum(e => e.Weight); LootEntry selected = available[^1];
			foreach (LootEntry entry in available) { cursor -= entry.Weight; if (cursor < 0) { selected = entry; break; } }
			if (Random.Shared.NextDouble() <= (double.IsFinite(selected.Chance) ? Math.Clamp(selected.Chance, 0, 1) : 0)) { results.Add(selected); Dropped.Invoke(selected, selected.RollQuantity()); }
			if (!AllowDuplicates) available.Remove(selected);
		}
		return [.. results];
	}
#if CREATOR
	public override void CreatorInserted()
	{
		LootEntry entry = Root.New<LootEntry>(); entry.Name = "LootEntry"; entry.Parent = this;
		base.CreatorInserted();
	}
#endif
}

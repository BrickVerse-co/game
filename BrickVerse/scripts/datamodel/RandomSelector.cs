// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>Selects enabled WeightedOption children using deterministic or random weighted rolls.</summary>
[Instantiable]
public sealed partial class RandomSelector : Instance
{
	[Editable, ScriptProperty, DefaultValue(false)] public bool PreventImmediateRepeat { get; set; }
	[ScriptProperty] public WeightedOption? LastSelected { get; private set; }
	[ScriptProperty] public BVSignal<WeightedOption> Selected { get; private set; } = new();
	[ScriptMethod] public WeightedOption[] GetOptions() => [.. GetChildren().OfType<WeightedOption>().Where(o => o.Enabled && double.IsFinite(o.Weight) && o.Weight > 0)];
	[ScriptMethod]
	public WeightedOption? Select(double normalizedRoll = -1)
	{
		WeightedOption[] options = GetOptions();
		if (PreventImmediateRepeat && options.Length > 1) options = [.. options.Where(o => o != LastSelected)];
		if (options.Length == 0) return null;
		double total = options.Sum(o => o.Weight);
		double roll = normalizedRoll >= 0 ? Math.Clamp(normalizedRoll, 0, 0.999999999) * total : Random.Shared.NextDouble() * total;
		WeightedOption selected = options[^1];
		foreach (WeightedOption option in options) { roll -= option.Weight; if (roll < 0) { selected = option; break; } }
		LastSelected = selected; OnPropertyChanged(nameof(LastSelected)); Selected.Invoke(selected); return selected;
	}
#if CREATOR
	public override void CreatorInserted()
	{
		WeightedOption option = Root.New<WeightedOption>(); option.Name = "Option"; option.Parent = this;
		base.CreatorInserted();
	}
#endif
}

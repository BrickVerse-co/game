// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using BrickVerse.Attributes;
using BrickVerse.Networking;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>A bounded regenerating resource such as stamina, mana, fuel, or shields.</summary>
[Instantiable]
public sealed partial class ResourceMeter : Instance
{
	private double _maximum = 100, _value = 100, _regenerationRate;
	private bool _regenerationEnabled = true;
	[Editable, ScriptProperty, DefaultValue(100d)] public double Maximum { get => _maximum; set { _maximum = Math.Max(0, double.IsFinite(value) ? value : 100); Value = _value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, SyncVar, DefaultValue(100d)] public double Value { get => _value; set { double old = _value; _value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, _maximum); OnPropertyChanged(); OnPropertyChanged(nameof(Fraction)); if (old != _value) ResourceChanged.Invoke(old, _value); } }
	[Editable, ScriptProperty, DefaultValue(0d)] public double RegenerationRate { get => _regenerationRate; set { _regenerationRate = double.IsFinite(value) ? value : 0; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool RegenerationEnabled { get => _regenerationEnabled; set { _regenerationEnabled = value; OnPropertyChanged(); } }
	[ScriptProperty] public double Fraction => _maximum <= 0 ? 0 : _value / _maximum;
	[ScriptProperty] public bool IsEmpty => _value <= 0;
	[ScriptProperty] public bool IsFull => _value >= _maximum;
	[ScriptProperty] public BVSignal<double, double> ResourceChanged { get; private set; } = new();
	[ScriptProperty] public BVSignal Depleted { get; private set; } = new();
	[ScriptProperty] public BVSignal Refilled { get; private set; } = new();
	public override void Init() { base.Init(); SetProcess(true); }
	public override void Process(double delta) { base.Process(delta); if (HasAuthority && _regenerationEnabled && _regenerationRate != 0) Add(_regenerationRate * delta); }
	[ScriptMethod] public bool Spend(double amount) { if (!HasAuthority || amount < 0 || _value < amount) return false; double before = _value; Value -= amount; if (before > 0 && IsEmpty) Depleted.Invoke(); return true; }
	[ScriptMethod] public double Add(double amount) { if (!HasAuthority) return 0; double before = _value; Value += amount; if (before < _maximum && IsFull) Refilled.Invoke(); return _value - before; }
	[ScriptMethod] public void Fill() { if (HasAuthority) Value = _maximum; }
	[ScriptMethod] public void Empty() { if (!HasAuthority) return; bool wasEmpty = IsEmpty; Value = 0; if (!wasEmpty) Depleted.Invoke(); }
}

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using BrickVerse.Attributes;
using BrickVerse.Networking;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>A reusable health pool for destructible objects, NPC systems, and scripted gameplay.</summary>
[Instantiable]
public sealed partial class HealthComponent : Instance
{
	private double _maximum = 100, _current = 100;
	private bool _invulnerable;

	[Editable, ScriptProperty, DefaultValue(100d)]
	public double Maximum { get => _maximum; set { _maximum = Math.Max(0, double.IsFinite(value) ? value : 100); SetCurrent(Math.Min(_current, _maximum)); OnPropertyChanged(); } }
	[Editable, ScriptProperty, SyncVar, DefaultValue(100d)] public double Current { get => _current; set => SetCurrent(value); }
	[Editable, ScriptProperty, SyncVar, DefaultValue(false)] public bool Invulnerable { get => _invulnerable; set { _invulnerable = value; OnPropertyChanged(); } }
	[ScriptProperty] public bool IsDead => _current <= 0;
	[ScriptProperty] public double Fraction => _maximum <= 0 ? 0 : _current / _maximum;
	[ScriptProperty] public BVSignal<double, Instance?> Damaged { get; private set; } = new();
	[ScriptProperty] public BVSignal<double, Instance?> Healed { get; private set; } = new();
	[ScriptProperty] public BVSignal<Instance?> Died { get; private set; } = new();
	[ScriptProperty] public BVSignal Revived { get; private set; } = new();
	[ScriptProperty] public BVSignal<double, double> HealthChanged { get; private set; } = new();

	[ScriptMethod]
	public double Damage(double amount, Instance? source = null)
	{
		if (!HasAuthority || _invulnerable || amount <= 0 || IsDead) return 0;
		double before = _current;
		SetCurrent(_current - amount);
		double applied = before - _current;
		if (applied > 0) Damaged.Invoke(applied, source);
		if (before > 0 && IsDead) Died.Invoke(source);
		return applied;
	}

	[ScriptMethod]
	public double Heal(double amount, Instance? source = null)
	{
		if (!HasAuthority || amount <= 0 || _maximum <= 0) return 0;
		double before = _current;
		SetCurrent(_current + amount);
		double applied = _current - before;
		if (applied > 0) Healed.Invoke(applied, source);
		if (before <= 0 && _current > 0) Revived.Invoke();
		return applied;
	}

	[ScriptMethod] public void Kill(Instance? source = null) { if (!_invulnerable) Damage(_current, source); }
	[ScriptMethod] public void Reset() { if (!HasAuthority) return; bool dead = IsDead; SetCurrent(_maximum); if (dead && _current > 0) Revived.Invoke(); }

	private void SetCurrent(double value)
	{
		double next = Math.Clamp(double.IsFinite(value) ? value : 0, 0, _maximum);
		if (next == _current) return;
		double previous = _current;
		_current = next;
		OnPropertyChanged(nameof(Current)); OnPropertyChanged(nameof(IsDead)); OnPropertyChanged(nameof(Fraction));
		HealthChanged.Invoke(previous, next);
	}
}

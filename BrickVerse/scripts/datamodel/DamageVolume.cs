// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A trigger volume that continuously damages players and NPCs on the server.</summary>
[Instantiable]
public sealed partial class DamageVolume : TriggerVolume
{
	private float _damagePerSecond = 25;

	[Editable, ScriptProperty, DefaultValue(25f)]
	public float DamagePerSecond
	{
		get => _damagePerSecond;
		set { float next = Mathf.Max(0, value); if (Mathf.IsEqualApprox(_damagePerSecond, next)) return; _damagePerSecond = next; OnPropertyChanged(); }
	}

	[ScriptProperty] public BVSignal<NPC, float> Damaged { get; private set; } = new();

	public override void Init()
	{
		base.Init();
		DetectEntities = false;
		SetPhysicsProcess(true);
	}

	public override void PhysicsProcess(double delta)
	{
		base.PhysicsProcess(delta);
		if (!Enabled || !Root.Network.IsServer || _damagePerSecond <= 0) return;
		float damage = _damagePerSecond * (float)delta;
		foreach (Physical physical in GetOccupants())
		{
			if (physical is not NPC npc || npc.IsDead) continue;
			npc.TakeDamage(damage);
			Damaged.Invoke(npc, damage);
		}
	}
}

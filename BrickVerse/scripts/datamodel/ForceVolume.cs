// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A trigger volume that applies continuous acceleration to physical instances.</summary>
[Instantiable]
public sealed partial class ForceVolume : TriggerVolume
{
	private Vector3 _acceleration = new(0, 20, 0);

	[Editable, ScriptProperty]
	public Vector3 Acceleration
	{
		get => _acceleration;
		set { if (_acceleration.IsEqualApprox(value)) return; _acceleration = value; OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(false)] public bool LocalSpace { get; set; }
	[Editable, ScriptProperty, DefaultValue(100f)] public float MaximumSpeed { get; set; } = 100;

	public override void Init()
	{
		base.Init();
		SetPhysicsProcess(true);
	}

	public override void PhysicsProcess(double delta)
	{
		base.PhysicsProcess(delta);
		if (!Enabled || !Root.Network.IsServer) return;
		Vector3 acceleration = LocalSpace ? GetGlobalTransform().Basis * _acceleration : _acceleration;
		foreach (Physical physical in GetOccupants())
		{
			if (physical.IsDeleted || physical.Anchored) continue;
			Vector3 velocity = physical.Velocity + acceleration * (float)delta;
			float limit = Mathf.Max(0, MaximumSpeed);
			physical.Velocity = limit > 0 && velocity.Length() > limit ? velocity.Normalized() * limit : velocity;
		}
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>Applies configurable flotation and water drag to a parent RigidBody.</summary>
[Instantiable]
public partial class Buoyancy : Instance
{
	[Editable, ScriptProperty] public bool Enabled { get; set; } = true;
	[Editable, ScriptProperty] public float WaterLevel { get; set; }
	[Editable, ScriptProperty, DefaultValue(2f)] public float FloatHeight { get; set; } = 2f;
	[Editable, ScriptProperty, DefaultValue(20f)] public float BuoyancyAcceleration { get; set; } = 20f;
	[Editable, ScriptProperty, DefaultValue(2f)] public float LinearDamping { get; set; } = 2f;
	[Editable, ScriptProperty, DefaultValue(1f)] public float AngularDamping { get; set; } = 1f;
	[ScriptProperty] public float Submersion { get; private set; }

	public override void Init() { SetPhysicsProcess(true); base.Init(); }

	public override void PhysicsProcess(double delta)
	{
		base.PhysicsProcess(delta);
		if (!Enabled || Parent is not RigidBody body || body.Anchored || !Root.Network.IsServer) { Submersion = 0; return; }
		float height = Mathf.Max(0.01f, FloatHeight);
		Submersion = Mathf.Clamp((WaterLevel - body.Position.Y + height * 0.5f) / height, 0, 1);
		if (Submersion <= 0) return;
		float mass = Mathf.Max(body.Mass, Physical.MinMass);
		body.AddForce((Vector3.Up * BuoyancyAcceleration - body.Velocity * Mathf.Max(0, LinearDamping)) * mass * Submersion);
		body.AddTorque(-body.AngularVelocity * Mathf.Max(0, AngularDamping) * mass * Submersion);
	}
}

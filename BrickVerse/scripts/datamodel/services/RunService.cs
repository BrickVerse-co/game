// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel.Services;

/// <summary>Exposes render and fixed-physics frame lifecycle events to scripts.</summary>
[Static("RunService"), ExplorerExclude, SaveIgnore]
public sealed partial class RunService : Instance
{
	private double _elapsedTime;
	private double _physicsElapsedTime;

	[ScriptProperty] public BVSignal<double> RenderStepped { get; private set; } = new();
	[ScriptProperty] public BVSignal<double> Heartbeat { get; private set; } = new();
	[ScriptProperty] public BVSignal<double> PhysicsStepped { get; private set; } = new();
	[ScriptProperty] public double ElapsedTime => _elapsedTime;
	[ScriptProperty] public double PhysicsElapsedTime => _physicsElapsedTime;
	[ScriptProperty] public bool IsCreator => Root.SessionType == World.SessionTypeEnum.Creator;
	[ScriptProperty] public bool IsClient => Root.SessionType == World.SessionTypeEnum.Client;
	[ScriptProperty] public bool IsServer => Root.Network.IsServer;

	public override void Init()
	{
		base.Init();
		SetProcess(true);
		SetPhysicsProcess(true);
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		_elapsedTime += delta;
		RenderStepped.Invoke(delta);
		Heartbeat.Invoke(delta);
	}

	public override void PhysicsProcess(double delta)
	{
		base.PhysicsProcess(delta);
		_physicsElapsedTime += delta;
		PhysicsStepped.Invoke(delta);
	}
}

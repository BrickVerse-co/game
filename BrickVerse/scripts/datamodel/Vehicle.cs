// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A native raycast-wheel vehicle chassis. Parent VehicleWheel instances to this object.</summary>
[Instantiable]
public partial class Vehicle : RigidBody
{
	private float _maxEngineForce = 1200f;
	private float _maxSteeringAngle = 30f;
	private float _brakeForce = 40f;
	private VehicleSeat? _driverSeat;

	private VehicleBody3D Body => (VehicleBody3D)GDRigidBody;

	[Editable, ScriptProperty, DefaultValue(1200f)] public float MaxEngineForce { get => _maxEngineForce; set { _maxEngineForce = Mathf.Max(0, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(30f)] public float MaxSteeringAngle { get => _maxSteeringAngle; set { _maxSteeringAngle = Mathf.Clamp(value, 0, 89); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(40f)] public float BrakeForce { get => _brakeForce; set { _brakeForce = Mathf.Max(0, value); OnPropertyChanged(); } }
	[ScriptProperty] public float Throttle { get; private set; }
	[ScriptProperty] public float Steer { get; private set; }
	[ScriptProperty] public float BrakeInput { get; private set; }
	[ScriptProperty] public BVSignal<float, float, float> InputChanged { get; private set; } = new();
	[Editable, ScriptProperty]
	public VehicleSeat? DriverSeat
	{
		get => _driverSeat is { IsDeleted: false } ? _driverSeat : null;
		set
		{
			if (_driverSeat == value) return;
			if (_driverSeat != null) _driverSeat.InputChanged.Disconnect(OnSeatInputChanged);
			_driverSeat = value;
			if (_driverSeat != null) _driverSeat.InputChanged.Connect(OnSeatInputChanged);
			OnPropertyChanged();
		}
	}

	public override Node CreateGDNode() => new VehicleBody3D();
	public override void Ready()
	{
		DriverSeat ??= FindFirstChildWhichIsA(nameof(VehicleSeat), true) as VehicleSeat;
		base.Ready();
	}
	public override void PreDelete()
	{
		if (_driverSeat != null) _driverSeat.InputChanged.Disconnect(OnSeatInputChanged);
		base.PreDelete();
	}
	private void OnSeatInputChanged(float throttle, float steer) => SetInput(throttle, steer, throttle == 0 ? 1 : 0);

	[ScriptMethod]
	public void SetInput(float throttle, float steer, float brake = 0)
	{
		Throttle = Mathf.Clamp(throttle, -1, 1);
		Steer = Mathf.Clamp(steer, -1, 1);
		BrakeInput = Mathf.Clamp(brake, 0, 1);
		Body.EngineForce = Throttle * MaxEngineForce;
		Body.Steering = Mathf.DegToRad(Steer * MaxSteeringAngle);
		Body.Brake = BrakeInput * BrakeForce;
		InputChanged.Invoke(Throttle, Steer, BrakeInput);
	}
}

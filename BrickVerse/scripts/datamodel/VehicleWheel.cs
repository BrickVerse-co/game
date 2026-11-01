// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A raycast vehicle wheel. It becomes active when parented beneath a Vehicle.</summary>
[Instantiable]
public partial class VehicleWheel : Dynamic
{
	private VehicleWheel3D Wheel => (VehicleWheel3D)GDNode3D;
	private bool _steering, _traction = true;
	private float _radius = 0.5f, _restLength = 0.3f, _stiffness = 5.88f, _friction = 10.5f;

	[Editable, ScriptProperty] public bool SteeringEnabled { get => _steering; set { _steering = value; Wheel.UseAsSteering = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool TractionEnabled { get => _traction; set { _traction = value; Wheel.UseAsTraction = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0.5f)] public float Radius { get => _radius; set { _radius = Mathf.Max(0.01f, value); Wheel.WheelRadius = _radius; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0.3f)] public float SuspensionLength { get => _restLength; set { _restLength = Mathf.Max(0, value); Wheel.WheelRestLength = _restLength; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(5.88f)] public float SuspensionStiffness { get => _stiffness; set { _stiffness = Mathf.Max(0, value); Wheel.SuspensionStiffness = _stiffness; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(10.5f)] public float FrictionSlip { get => _friction; set { _friction = Mathf.Max(0, value); Wheel.WheelFrictionSlip = _friction; OnPropertyChanged(); } }
	[ScriptProperty] public bool IsInContact => Wheel.IsInContact();
	[ScriptProperty] public float RevolutionsPerMinute => Wheel.GetRpm();

	public override Node CreateGDNode() => new VehicleWheel3D();
	public override void InitGDNode()
	{
		base.InitGDNode();
		Wheel.UseAsSteering = _steering; Wheel.UseAsTraction = _traction;
		Wheel.WheelRadius = _radius; Wheel.WheelRestLength = _restLength;
		Wheel.SuspensionStiffness = _stiffness; Wheel.WheelFrictionSlip = _friction;
	}
}

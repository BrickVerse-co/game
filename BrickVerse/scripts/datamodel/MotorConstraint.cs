using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class MotorConstraint : Constraint
{
	private float _angularVelocity, _maxTorque = 1000;
	[Editable, ScriptProperty] public float AngularVelocity { get => _angularVelocity; set { _angularVelocity = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1000f)] public float MaxTorque { get => _maxTorque; set { _maxTorque = Mathf.Max(0, value); Rebuild(); OnPropertyChanged(); } }
	protected override Joint3D CreateJoint() { HingeJoint3D joint = new(); joint.Set("motor/enable", true); joint.Set("motor/target_velocity", Mathf.DegToRad(_angularVelocity)); joint.Set("motor/max_impulse", _maxTorque); return joint; }
}

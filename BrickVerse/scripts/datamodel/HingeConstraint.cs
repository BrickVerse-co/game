using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;
[Instantiable]
public sealed partial class HingeConstraint : Constraint
{
	private bool _limitsEnabled, _motorEnabled; private float _lowerAngle = -45, _upperAngle = 45, _angularVelocity, _motorMaxTorque = 1000;
	[Editable, ScriptProperty] public bool LimitsEnabled { get => _limitsEnabled; set { _limitsEnabled = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(-45f)] public float LowerAngle { get => _lowerAngle; set { _lowerAngle = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(45f)] public float UpperAngle { get => _upperAngle; set { _upperAngle = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public bool MotorEnabled { get => _motorEnabled; set { _motorEnabled = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float AngularVelocity { get => _angularVelocity; set { _angularVelocity = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1000f)] public float MotorMaxTorque { get => _motorMaxTorque; set { _motorMaxTorque = Mathf.Max(0, value); Rebuild(); OnPropertyChanged(); } }
	protected override Joint3D CreateJoint() { HingeJoint3D joint = new(); joint.Set("angular_limit/enable", _limitsEnabled); joint.Set("angular_limit/lower", Mathf.DegToRad(_lowerAngle)); joint.Set("angular_limit/upper", Mathf.DegToRad(_upperAngle)); joint.Set("motor/enable", _motorEnabled); joint.Set("motor/target_velocity", Mathf.DegToRad(_angularVelocity)); joint.Set("motor/max_impulse", _motorMaxTorque); return joint; }
}

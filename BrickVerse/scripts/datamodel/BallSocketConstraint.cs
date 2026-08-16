using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class BallSocketConstraint : Constraint
{
	private float _maxFrictionTorque;
	[Editable, ScriptProperty] public float MaxFrictionTorque { get => _maxFrictionTorque; set { _maxFrictionTorque = Mathf.Max(0, value); Rebuild(); OnPropertyChanged(); } }
	protected override Joint3D CreateJoint() { PinJoint3D joint = new(); joint.Set("params/impulse_clamp", _maxFrictionTorque); return joint; }
}

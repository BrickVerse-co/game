using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;
[Instantiable]
public sealed partial class AlignRotation : Instance
{
	private Attachment? _attachment0, _attachment1; private Vector3 _rotation; private bool _enabled = true; private float _maxTorque = 10000, _responsiveness = 10;
	[Editable, ScriptProperty] public Attachment? Attachment0 { get => _attachment0; set { _attachment0 = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Attachment? Attachment1 { get => _attachment1; set { _attachment1 = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Vector3 Rotation { get => _rotation; set { _rotation = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(10000f)] public float MaxTorque { get => _maxTorque; set { _maxTorque = Mathf.Max(0, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(10f)] public float Responsiveness { get => _responsiveness; set { _responsiveness = Mathf.Clamp(value, 0, 200); OnPropertyChanged(); } }
	public override void Init() { SetPhysicsProcess(true); base.Init(); }
	public override void PhysicsProcess(double delta)
	{
		RigidBody3D? body = (_attachment0?.Parent as Physical)?.GDNode3D as RigidBody3D;
		if (_enabled && _attachment0 != null && body != null)
		{
			Quaternion current = _attachment0.GDNode3D.GlobalBasis.GetRotationQuaternion(); Quaternion target = _attachment1?.GDNode3D.GlobalBasis.GetRotationQuaternion() ?? Quaternion.FromEuler(new Vector3(Mathf.DegToRad(_rotation.X), Mathf.DegToRad(_rotation.Y), Mathf.DegToRad(_rotation.Z))); Quaternion error = (target * current.Inverse()).Normalized(); if (error.W < 0) error = -error;
			Vector3 torque = error.GetAxis() * error.GetAngle() * (_responsiveness * _responsiveness) - body.AngularVelocity * (2 * _responsiveness); if (torque.Length() > _maxTorque) torque = torque.Normalized() * _maxTorque; body.ApplyTorque(torque);
		}
		base.PhysicsProcess(delta);
	}
}

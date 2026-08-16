using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class RopeConstraint : Instance
{
	private Attachment? _attachment0, _attachment1; private bool _enabled = true; private float _length = 10, _stiffness = 500, _damping = 20;
	[Editable, ScriptProperty] public Attachment? Attachment0 { get => _attachment0; set { _attachment0 = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Attachment? Attachment1 { get => _attachment1; set { _attachment1 = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(10f)] public float Length { get => _length; set { _length = Mathf.Max(0, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(500f)] public float Stiffness { get => _stiffness; set { _stiffness = Mathf.Max(0, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(20f)] public float Damping { get => _damping; set { _damping = Mathf.Max(0, value); OnPropertyChanged(); } }
	public override void Init() { SetPhysicsProcess(true); base.Init(); }
	public override void PhysicsProcess(double delta)
	{
		if (!_enabled || _attachment0 == null || _attachment1 == null) { base.PhysicsProcess(delta); return; }
		Vector3 offset = _attachment1.WorldPosition - _attachment0.WorldPosition; float distance = offset.Length();
		if (distance > _length && distance > 0.0001f)
		{
			Vector3 direction = offset / distance; RigidBody3D? a = (_attachment0.Parent as Physical)?.GDNode3D as RigidBody3D; RigidBody3D? b = (_attachment1.Parent as Physical)?.GDNode3D as RigidBody3D;
			float relativeSpeed = (b?.LinearVelocity ?? Vector3.Zero).Dot(direction) - (a?.LinearVelocity ?? Vector3.Zero).Dot(direction);
			Vector3 force = direction * Mathf.Max(0, (distance - _length) * _stiffness + relativeSpeed * _damping);
			a?.ApplyCentralForce(force); b?.ApplyCentralForce(-force);
		}
		base.PhysicsProcess(delta);
	}
}

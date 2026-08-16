using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class AlignPosition : Instance
{
	private Attachment? _attachment0, _attachment1; private Vector3 _position; private bool _enabled = true; private float _maxForce = 10000, _responsiveness = 10;
	[Editable, ScriptProperty] public Attachment? Attachment0 { get => _attachment0; set { _attachment0 = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Attachment? Attachment1 { get => _attachment1; set { _attachment1 = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Vector3 Position { get => _position; set { _position = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(10000f)] public float MaxForce { get => _maxForce; set { _maxForce = Mathf.Max(0, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(10f)] public float Responsiveness { get => _responsiveness; set { _responsiveness = Mathf.Clamp(value, 0, 200); OnPropertyChanged(); } }
	public override void Init() { SetPhysicsProcess(true); base.Init(); }
	public override void PhysicsProcess(double delta)
	{
		RigidBody3D? body = (_attachment0?.Parent as Physical)?.GDNode3D as RigidBody3D;
		if (_enabled && _attachment0 != null && body != null) { Vector3 target = _attachment1?.WorldPosition ?? _position; Vector3 force = (target - _attachment0.WorldPosition) * (_responsiveness * _responsiveness) - body.LinearVelocity * (2 * _responsiveness); if (force.Length() > _maxForce) force = force.Normalized() * _maxForce; body.ApplyCentralForce(force); }
		base.PhysicsProcess(delta);
	}
}

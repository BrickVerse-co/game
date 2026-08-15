// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

[Abstract]
public abstract partial class Constraint : Instance
{
	private Attachment? _attachment0, _attachment1;
	private bool _enabled = true;
	protected Joint3D? Joint;
	[Editable, ScriptProperty] public Attachment? Attachment0 { get => _attachment0; set { _attachment0 = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Attachment? Attachment1 { get => _attachment1; set { _attachment1 = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; Rebuild(); OnPropertyChanged(); } }
	[ScriptMethod] public void Break() { Enabled = false; Attachment0 = null; Attachment1 = null; }
	public override void EnterTree() { Rebuild(); base.EnterTree(); }
	public override void ExitTree() { DestroyJoint(); base.ExitTree(); }
	public override void PreDelete() { DestroyJoint(); base.PreDelete(); }
	protected void Rebuild()
	{
		DestroyJoint();
		if (!_enabled || _attachment0?.Parent is not Physical p0 || _attachment1?.Parent is not Physical p1) return;
		if (p0.GDNode3D is not PhysicsBody3D body0 || p1.GDNode3D is not PhysicsBody3D body1 || !body0.IsInsideTree() || !body1.IsInsideTree()) return;
		Joint = CreateJoint(); GDNode.AddChild(Joint); Joint.GlobalTransform = _attachment0.GDNode3D.GlobalTransform;
		Joint.NodeA = body0.GetPath(); Joint.NodeB = body1.GetPath();
	}
	protected abstract Joint3D CreateJoint();
	private void DestroyJoint() { if (Joint != null && Node.IsInstanceValid(Joint)) Joint.QueueFree(); Joint = null; }
}

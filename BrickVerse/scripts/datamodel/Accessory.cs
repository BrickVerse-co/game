// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;

namespace BrickVerse.Datamodel;

[Instantiable]
public partial class Accessory : Dynamic
{
	private BrickversianModel.CharacterAttachmentEnum _targetAttachment;
	private Node3D? _attachmentNode;
	private Transform3D _attachmentReference;
	private Transform3D _accessoryReference;

	[Editable, ScriptProperty, SyncVar]
	public BrickversianModel.CharacterAttachmentEnum TargetAttachment
	{
		get => _targetAttachment;
		set
		{
			if (_targetAttachment == value)
				return;
			_targetAttachment = value;
			AttachToTarget();
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		base.Init();
		// Registration must happen during initialization. Attachment may occur later
		// after replication or parenting has completed.
		SetProcess(true);
	}

	public override void PostReparent()
	{
		base.PostReparent();

		if (Parent is CharacterModel)
		{
			// Keep the accessory outside the internally scaled rig hierarchy. Bone
			// movement is applied below without inheriting the rig's import scale.
			LocalPosition = Vector3.Zero;
			LocalRotation = Vector3.Zero;
			AttachToTarget();
		}
		else
		{
			_attachmentNode = null;
			SetProcess(false);
		}
	}

	private void AttachToTarget()
	{
		if (Parent is not BrickversianModel character)
			return;
		if (!GodotObject.IsInstanceValid(GDNode3D))
			return;

		Node3D attachment = character.GetNode3DAttachment(_targetAttachment);
		if (!GodotObject.IsInstanceValid(attachment))
			return;

		_attachmentNode = attachment;
		_attachmentReference = attachment.GlobalTransform;
		_accessoryReference = GDNode3D.GlobalTransform;
		SetProcess(true);
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		if (
			!GodotObject.IsInstanceValid(_attachmentNode)
			|| !GodotObject.IsInstanceValid(GDNode3D)
		)
			return;

		Transform3D currentAttachment = _attachmentNode!.GlobalTransform;
		Basis referenceRotation = _attachmentReference.Basis.Orthonormalized();
		Basis currentRotation = currentAttachment.Basis.Orthonormalized();
		Basis rotationDelta = currentRotation * referenceRotation.Inverse();
		Vector3 referenceOffset = _accessoryReference.Origin - _attachmentReference.Origin;

		GDNode3D.GlobalTransform = new Transform3D(
			(rotationDelta * _accessoryReference.Basis).Orthonormalized(),
			currentAttachment.Origin + rotationDelta * referenceOffset
		);
	}
}

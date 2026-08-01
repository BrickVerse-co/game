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

	// Retained for backwards-compatible loading of existing models. Accessories now
	// use character-local coordinates and no longer follow per-bone attachments.
	[ScriptProperty]
	public BrickversianModel.CharacterAttachmentEnum TargetAttachment
	{
		get => _targetAttachment;
		set
		{
			_targetAttachment = value;
			OnPropertyChanged();
		}
	}

	public override void PostReparent()
	{
		base.PostReparent();

		if (Parent is CharacterModel)
		{
			// Godot reparents Node3D instances while preserving their global transform.
			// Marketplace accessories instead need their root to be character-local.
			// Do not assign size here; imported and user-authored scale must be preserved.
			LocalPosition = Vector3.Zero;
			LocalRotation = Vector3.Zero;
		}
	}
}

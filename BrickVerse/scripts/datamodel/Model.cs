// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Datamodel.Interfaces;
using Godot;
using System;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class Model : Dynamic, IGroup
{
	private Dynamic? _primaryPart;
	[SyncVar, CloneInclude]
	public float ModelScale { get; private set; } = 1f;

	[Editable, ScriptProperty]
	public Dynamic? PrimaryPart
	{
		get => _primaryPart is { IsDeleted: false } ? _primaryPart : null;
		set
		{
			if (value != null && value != this && !value.IsDescendantOf(this))
				throw new InvalidOperationException("PrimaryPart must be a descendant of the Model.");
			_primaryPart = value;
			OnPropertyChanged();
		}
	}

	[ScriptMethod]
	public Transform3D GetPivot() => PrimaryPart?.CFrame ?? CFrame;

	[ScriptMethod]
	public void PivotTo(Transform3D target)
	{
		if (!target.IsFinite()) throw new ArgumentException("Pivot transform must contain finite values.", nameof(target));
		Transform3D current = GetPivot();
		Transform3D delta = target * current.AffineInverse();
		CFrame = delta * CFrame;
	}

	[ScriptMethod]
	public void MoveTo(Vector3 position)
	{
		if (!position.IsFinite()) throw new ArgumentException("Position must contain finite values.", nameof(position));
		Transform3D pivot = GetPivot();
		PivotTo(new Transform3D(pivot.Basis, position));
	}

	[ScriptMethod]
	public float GetScale() => ModelScale;

	[ScriptMethod]
	public void ScaleTo(float newScale)
	{
		if (!float.IsFinite(newScale) || newScale <= 0f || newScale > 10_000f)
			throw new ArgumentOutOfRangeException(nameof(newScale), "Model scale must be finite and between 0 and 10,000.");
		float ratio = newScale / ModelScale;
		Size *= ratio;
		ModelScale = newScale;
		OnPropertyChanged(nameof(ModelScale));
	}
}

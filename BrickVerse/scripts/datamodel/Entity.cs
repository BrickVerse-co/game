// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using System.Collections.Generic;

namespace BrickVerse.Datamodel;

[Abstract]
public abstract partial class Entity : RigidBody
{
	internal const uint CameraClipCollisionLayerMask = 1u << 5;

	private bool _isSpawn = false;
	private bool _isNegated = false;
#if CREATOR
	private Node3D? _negateHighlight;
#endif

	private Color _color = new(1, 1, 1);
	private bool _castShadows = true;

	[Editable, ScriptProperty]
	public virtual Color Color
	{
		get => _color;
		set
		{
			if (_color == value)
			{
				return;
			}

			_color = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public virtual bool CastShadows
	{
		get => _castShadows;
		set
		{
			if (_castShadows == value)
			{
				return;
			}

			_castShadows = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool IsNegated
	{
		get => _isNegated;
		set
		{
			if (_isNegated == value)
			{
				return;
			}

			_isNegated = value;
			OnNegatedChanged();
			UpdateNegateHighlight();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool IsSpawn
	{
		get => _isSpawn;
		set
		{
			if (_isSpawn == value)
			{
				return;
			}

			_isSpawn = value;

			if (_isSpawn)
			{
				Root.Environment.RegisterSpawnPoint(this);
			}
			else
			{
				Root.Environment.UnregisterSpawnPoint(this);
			}
		}
	}

	public override void Init()
	{
		base.Init();
		UpdateCamLayer();
		UpdateNegateHighlight();
	}

	internal virtual (Godot.Mesh Mesh, Transform3D Transform)[] GetBooleanGeometry() => [];
	internal virtual void OnNegatedChanged() { }
	protected Color GetVisualColor(Color color) => _isNegated ? new Color(color, Mathf.Min(color.A, 0.48f)) : color;

	internal void UpdateNegateHighlight()
	{
#if CREATOR
		if (_negateHighlight != null && GodotObject.IsInstanceValid(_negateHighlight))
		{
			_negateHighlight.QueueFree();
			_negateHighlight = null;
		}
		if (!_isNegated || GDNode3D == null || !GodotObject.IsInstanceValid(GDNode3D)) return;

		_negateHighlight = new Node3D { Name = "NegateHighlight" };
		GDNode3D.AddChild(_negateHighlight, false, Node.InternalMode.Back);
		StandardMaterial3D material = new()
		{
			AlbedoColor = new Color(1f, 0.04f, 0.04f, 0.38f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			NoDepthTest = true,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		Transform3D inverse = GDNode3D.GlobalTransform.AffineInverse();
		foreach ((Godot.Mesh mesh, Transform3D transform) in GetBooleanGeometry())
		{
			MeshInstance3D overlay = new() { Mesh = mesh, MaterialOverride = material, Transform = inverse * transform };
			_negateHighlight.AddChild(overlay);
		}
#endif
	}

	public override void PreDelete()
	{
		// Unregister spawnpoint on delete
		Root?.Environment?.UnregisterSpawnPoint(this);
		base.PreDelete();
	}

	internal void UpdateCamLayer()
	{
		ApplyCollisionObjectLayers();
	}

	protected override uint GetAppliedCollisionLayers()
	{
		uint layers = base.GetAppliedCollisionLayers();

		return Color.A > 0.5f
			? layers | CameraClipCollisionLayerMask
			: layers & ~CameraClipCollisionLayerMask;
	}
}

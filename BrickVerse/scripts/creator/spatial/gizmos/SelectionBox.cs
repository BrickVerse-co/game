// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Utils;

namespace BrickVerse.Creator.Spatial;

public partial class SelectionBox : Node
{
	private Dynamic? _target;
	public Gizmos? RootGizmos { get; set; }
	public World Root = null!;
	public Dynamic? Target
	{
		get => _target;
		set
		{
			GenerateBoxes();
			if (_target != value)
			{
				_target?.TransformChanged -= UpdateBox;
				_target = value;
				UpdateBox();
				_target?.TransformChanged += UpdateBox;
			}
		}
	}
	public Color SelectionColor = new(1f, 0.5f, 0f);

	private MeshInstance3D _selectionBoxMesh = null!;
	private MeshInstance3D _selectionBoxXrayMesh = null!;

	private ArrayMesh _selectionBox = null!;
	private ArrayMesh _selectionBoxXray = null!;

	private float _gizmoScale;
	private Camera3D _camera = null!;

	private StandardMaterial3D _mat = null!;
	private StandardMaterial3D _matXray = null!;

	private Aabb? _cachedGlobalBounds = null;
	private Transform3D _cachedTargetTransform = Transform3D.Identity;

	private bool _boxGenerated = false;

	public override void _EnterTree()
	{
		GenerateBoxes();
		UpdateBox();
		base._EnterTree();
	}

	public override void _ExitTree()
	{
		_selectionBoxMesh?.QueueFree();
		_selectionBoxXrayMesh?.QueueFree();
		base._ExitTree();
	}

	public override void _Ready()
	{
		_camera = GetViewport().GetCamera3D();
	}

	private void GenerateBoxes()
	{
		if (_boxGenerated) return;
		_boxGenerated = true;
		Aabb aabb = new(new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(1, 1, 1));

		SurfaceTool st = new();
		SurfaceTool stXray = new();

		st.Begin(Godot.Mesh.PrimitiveType.Lines);
		stXray.Begin(Godot.Mesh.PrimitiveType.Lines);

		for (int i = 0; i < 12; i++)
		{
			aabb.GetEdge(i, out Vector3 a, out Vector3 b);

			st.AddVertex(a);
			st.AddVertex(b);
			stXray.AddVertex(a);
			stXray.AddVertex(b);
		}

		_mat = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};
		st.SetMaterial(_mat);
		_selectionBox = st.Commit();

		_matXray = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			NoDepthTest = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};
		stXray.SetMaterial(_matXray);
		_selectionBoxXray = stXray.Commit();

		_selectionBoxMesh = new MeshInstance3D { Mesh = _selectionBox };
		Root.GDNode.AddChild(_selectionBoxMesh, @internal: Node.InternalMode.Back);

		_selectionBoxXrayMesh = new MeshInstance3D { Mesh = _selectionBoxXray };
		Root.GDNode.AddChild(_selectionBoxXrayMesh, @internal: Node.InternalMode.Back);
	}

	public void InvalidateBoundCache()
	{
		_cachedGlobalBounds = null;
		_cachedTargetTransform = Transform3D.Identity;
	}

	public void UpdateBox()
	{
		Dynamic? target = Target;
		if (target == null)
		{
			_selectionBoxMesh.Visible = false;
			_selectionBoxXrayMesh.Visible = false;
			return;
		}

		UpdateBox(target);
	}

	private void UpdateBox(Dynamic target)
	{
		bool isDragging = RootGizmos != null && (RootGizmos.IsDraggingDynamic || RootGizmos.IsTransformingSelected);
		bool shouldShow = !isDragging;

		_selectionBoxMesh.Visible = shouldShow;
		_selectionBoxXrayMesh.Visible = shouldShow;
		if (!shouldShow) return;

		Aabb globalBounds;
		if (_cachedGlobalBounds.HasValue && _cachedTargetTransform != Transform3D.Identity)
		{
			Transform3D currentTransform = target.GetGlobalTransform();
			Transform3D delta = currentTransform * _cachedTargetTransform.AffineInverse();

			globalBounds = delta * _cachedGlobalBounds.Value;

			_cachedGlobalBounds = globalBounds;
			_cachedTargetTransform = currentTransform;
		}
		else
		{
			globalBounds = GetDisplayBounds(target);
			_cachedGlobalBounds = globalBounds;
			_cachedTargetTransform = target.GetGlobalTransform();
		}
		Vector3 size = globalBounds.Size + Vector3.One * 0.005f;

		Transform3D boxXform = new(
			Basis.FromScale(size),
			globalBounds.GetCenter()
		);

		_mat.AlbedoColor = SelectionColor;
		_matXray.AlbedoColor = SelectionColor * new Color(1f, 1f, 1f, 0.2f);

		_selectionBoxMesh.GlobalTransform = boxXform;
		_selectionBoxXrayMesh.GlobalTransform = boxXform;
	}

	private static Aabb GetDisplayBounds(Dynamic target)
	{
		Aabb selfBounds = target.GetSelfBound();
		if (selfBounds.Size != Vector3.Zero)
		{
			return selfBounds;
		}

		return target.CalculateBounds();
	}
}

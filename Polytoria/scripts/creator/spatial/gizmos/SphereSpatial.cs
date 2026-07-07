// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
namespace Polytoria.Creator.Spatial;

public partial class SphereSpatial : Node3D
{
	private MeshInstance3D _meshInstance = null!;
	private float _radius = 3;

	public float Radius
	{
		get => _radius;
		set
		{
			_radius = value;
			RenderGizmo();
		}
	}

	public float Segments { get; set; } = 32;

	[Export]
	public Color GizmoColor { get; set; } = new(1f, 0.5f, 0f);

	public override void _Ready()
	{
		if (Globals.CurrentAppEntry != Globals.AppEntryEnum.Creator) { Visible = false; return; }
		_meshInstance = new MeshInstance3D();
		AddChild(_meshInstance);
		RenderGizmo();
	}

	private void RenderGizmo()
	{
		if (_meshInstance == null)
			return;

		SurfaceTool st = new();
		st.Begin(Mesh.PrimitiveType.Lines);

		// Z axis
		for (int i = 0; i < Segments; i++)
		{
			float angle1 = 2 * Mathf.Pi * i / Segments;
			float angle2 = 2 * Mathf.Pi * (i + 1) / Segments;

			Vector3 p1 = new(Radius * Mathf.Cos(angle1), Radius * Mathf.Sin(angle1), 0);
			Vector3 p2 = new(Radius * Mathf.Cos(angle2), Radius * Mathf.Sin(angle2), 0);

			st.AddVertex(p1);
			st.AddVertex(p2);
		}

		// Y axis
		for (int i = 0; i < Segments; i++)
		{
			float angle1 = 2 * Mathf.Pi * i / Segments;
			float angle2 = 2 * Mathf.Pi * (i + 1) / Segments;

			Vector3 p1 = new(Radius * Mathf.Cos(angle1), 0, Radius * Mathf.Sin(angle1));
			Vector3 p2 = new(Radius * Mathf.Cos(angle2), 0, Radius * Mathf.Sin(angle2));

			st.AddVertex(p1);
			st.AddVertex(p2);
		}

		// X axis
		for (int i = 0; i < Segments; i++)
		{
			float angle1 = 2 * Mathf.Pi * i / Segments;
			float angle2 = 2 * Mathf.Pi * (i + 1) / Segments;

			Vector3 p1 = new(0, Radius * Mathf.Cos(angle1), Radius * Mathf.Sin(angle1));
			Vector3 p2 = new(0, Radius * Mathf.Cos(angle2), Radius * Mathf.Sin(angle2));

			st.AddVertex(p1);
			st.AddVertex(p2);
		}

		StandardMaterial3D mat = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = GizmoColor,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};

		st.SetMaterial(mat);
		_meshInstance.Mesh = st.Commit();
		_meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
	}
}

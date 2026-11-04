// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using System;
using System.Linq;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>Vertex and face-level editing workspace for native editable meshes.</summary>
public sealed partial class MeshComponentEditorWindow : PopupWindowBase
{
	private readonly EditableMeshPart _target;
	private OptionButton _face = null!;
	private OptionButton _vertex = null!;
	private SpinBox _amount = null!;
	private SpinBox _x = null!;
	private SpinBox _y = null!;
	private SpinBox _z = null!;
	private Label _stats = null!;

	private MeshComponentEditorWindow(EditableMeshPart target)
	{
		_target = target;
		Title = $"Mesh Components — {target.Name}";
		Size = new Vector2I(720, 500);
		MinSize = new Vector2I(560, 400);
		InitialPosition = WindowInitialPosition.CenterMainWindowScreen;
		Transient = true;
	}

	public static void Open()
	{
		EditableMeshPart? target = World.Current?.CreatorContext.Selections.GetSelected()
			.OfType<EditableMeshPart>().FirstOrDefault();
		if (target == null)
		{
			CreatorService.Interface.StatusBar?.SetStatus("Select an EditableMeshPart to edit components");
			return;
		}
		CreatorService.Interface.PopupWindow(new MeshComponentEditorWindow(target));
	}

	public override void _Ready()
	{
		VBoxContainer page = new();
		page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 20);
		AddChild(page);
		_stats = new Label();
		_stats.AddThemeFontSizeOverride("font_size", 18);
		page.AddChild(_stats);
		page.AddChild(new HSeparator());
		HBoxContainer columns = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		page.AddChild(columns);

		VBoxContainer faces = Section("Face editing");
		columns.AddChild(faces);
		_face = new OptionButton { CustomMinimumSize = new Vector2(260, 36) };
		faces.AddChild(Labeled("Active triangle", _face));
		_amount = Spin(-1000, 1000, 0.05, 0.2);
		faces.AddChild(Labeled("Operation amount", _amount));
		AddButton(faces, "Extrude along normal", Extrude);
		AddButton(faces, "Inset", Inset);
		AddButton(faces, "Bevel", Bevel);
		AddButton(faces, "Flip winding", () => Apply("Flip mesh face", mesh => mesh.FlipFaces([FaceId()])));
		AddButton(faces, "Subdivide face", () => Apply("Subdivide mesh face", mesh => mesh.Subdivide([FaceId()])));

		VBoxContainer vertices = Section("Vertex editing");
		columns.AddChild(vertices);
		_vertex = new OptionButton { CustomMinimumSize = new Vector2(260, 36) };
		_vertex.ItemSelected += _ => LoadVertex();
		vertices.AddChild(Labeled("Active vertex", _vertex));
		_x = Spin(-100000, 100000, 0.01, 0);
		_y = Spin(-100000, 100000, 0.01, 0);
		_z = Spin(-100000, 100000, 0.01, 0);
		vertices.AddChild(Labeled("X", _x));
		vertices.AddChild(Labeled("Y", _y));
		vertices.AddChild(Labeled("Z", _z));
		AddButton(vertices, "Apply vertex position", SetVertex);
		AddButton(vertices, "Merge nearby vertices", () => Apply("Merge mesh vertices", mesh => mesh.MergeVertices(Mathf.Abs((float)_amount.Value))));
		RefreshLists();
		base._Ready();
	}

	private void Extrude()
	{
		Apply("Extrude mesh face", mesh =>
		{
			int faceId = FaceId();
			int[] ids = mesh.GetFaceVertices(faceId);
			Vector3 a = mesh.GetPosition(ids[0]);
			Vector3 normal = (mesh.GetPosition(ids[1]) - a).Cross(mesh.GetPosition(ids[2]) - a).Normalized();
			mesh.ExtrudeFaces([faceId], normal * (float)_amount.Value);
		});
	}

	private void Inset() => Apply("Inset mesh face", mesh =>
		mesh.InsetFaces([FaceId()], Mathf.Clamp(Mathf.Abs((float)_amount.Value), 0.001f, 0.999f)));

	private void Bevel() => Apply("Bevel mesh face", mesh =>
		mesh.BevelFaces([FaceId()], 0.2f, (float)_amount.Value));

	private void SetVertex() => Apply("Move mesh vertex", mesh =>
		mesh.SetPosition(VertexId(), new Vector3((float)_x.Value, (float)_y.Value, (float)_z.Value)));

	private void Apply(string title, Action<EditableMesh> operation)
	{
		EditableMesh before = Clone(_target.Geometry);
		EditableMesh after = Clone(_target.Geometry);
		after.AutoCommit = false;
		operation(after);
		after.Commit();
		after.AutoCommit = true;
		_target.Geometry = after;
		World? world = World.Current;
		if (world != null)
			world.CreatorContext.History.RecordAppliedAction(
				title,
				new BVCallback(_ => _target.Geometry = after),
				new BVCallback(_ => _target.Geometry = before)
			);
		RefreshLists();
	}

	private void RefreshLists()
	{
		int priorFace = _face.Selected >= 0 ? FaceId() : 0;
		int priorVertex = _vertex.Selected >= 0 ? VertexId() : 0;
		_face.Clear();
		foreach (int id in _target.Geometry.GetFaces()) _face.AddItem($"Face {id}", id);
		_vertex.Clear();
		foreach (int id in _target.Geometry.GetVertices()) _vertex.AddItem($"Vertex {id}", id);
		SelectId(_face, priorFace);
		SelectId(_vertex, priorVertex);
		_stats.Text = $"{_target.Geometry.VertexCount:N0} vertices  •  {_target.Geometry.FaceCount:N0} triangles";
		LoadVertex();
	}

	private void LoadVertex()
	{
		if (_vertex.ItemCount == 0) return;
		Vector3 value = _target.Geometry.GetPosition(VertexId());
		_x.Value = value.X; _y.Value = value.Y; _z.Value = value.Z;
	}

	private int FaceId() => _face.GetItemId(_face.Selected);
	private int VertexId() => _vertex.GetItemId(_vertex.Selected);
	private static EditableMesh Clone(EditableMesh mesh) => mesh.SeparateFaces(mesh.GetFaces());

	private static void SelectId(OptionButton options, int id)
	{
		for (int index = 0; index < options.ItemCount; index++)
			if (options.GetItemId(index) == id) { options.Select(index); return; }
		if (options.ItemCount > 0) options.Select(0);
	}

	private static VBoxContainer Section(string title)
	{
		VBoxContainer section = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		Label label = new() { Text = title };
		label.AddThemeFontSizeOverride("font_size", 17);
		section.AddChild(label);
		return section;
	}

	private static Control Labeled(string label, Control input)
	{
		VBoxContainer row = new(); row.AddChild(new Label { Text = label }); row.AddChild(input); return row;
	}

	private static SpinBox Spin(double min, double max, double step, double value) => new()
	{
		MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(260, 34),
	};

	private static void AddButton(Control parent, string label, Action action)
	{
		Button button = new() { Text = label, CustomMinimumSize = new Vector2(260, 34) };
		button.Pressed += action; parent.AddChild(button);
	}
}

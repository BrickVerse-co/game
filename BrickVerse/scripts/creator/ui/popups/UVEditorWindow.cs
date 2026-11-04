// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>Native UV workspace for EditableMeshPart geometry.</summary>
public sealed partial class UVEditorWindow : PopupWindowBase
{
	private static UVEditorWindow? _open;
	private readonly EditableMeshPart _target;
	private UVCanvas _canvas = null!;

	private UVEditorWindow(EditableMeshPart target)
	{
		_target = target;
		Title = $"UV Editor — {target.Name}";
		Size = new Vector2I(900, 700);
		MinSize = new Vector2I(600, 440);
		InitialPosition = WindowInitialPosition.CenterMainWindowScreen;
		Transient = true;
	}

	public static void Open()
	{
		EditableMeshPart? target = World.Current?.CreatorContext.Selections.GetSelected()
			.OfType<EditableMeshPart>().FirstOrDefault();
		if (target == null)
		{
			CreatorService.Interface.StatusBar?.SetStatus("Select an EditableMeshPart to edit UVs");
			return;
		}
		if (_open != null && IsInstanceValid(_open))
			_open.QueueFree();
		_open = new UVEditorWindow(target);
		CreatorService.Interface.PopupWindow(_open);
	}

	public override void _Ready()
	{
		VBoxContainer root = new();
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);
		HBoxContainer toolbar = new() { CustomMinimumSize = new Vector2(0, 44) };
		root.AddChild(toolbar);
		AddButton(toolbar, "Project XY", () => Project(EditableMesh.MeshProjectionPlane.XY));
		AddButton(toolbar, "Project XZ", () => Project(EditableMesh.MeshProjectionPlane.XZ));
		AddButton(toolbar, "Project YZ", () => Project(EditableMesh.MeshProjectionPlane.YZ));
		AddButton(toolbar, "Fit 0–1", Fit);
		AddButton(toolbar, "Rotate 90°", Rotate);
		AddButton(toolbar, "Mirror U", () => Mirror(true));
		AddButton(toolbar, "Mirror V", () => Mirror(false));
		toolbar.AddChild(new Label
		{
			Text = "Drag UV points • grid represents 0–1 texture space",
			VerticalAlignment = VerticalAlignment.Center,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Right,
		});
		_canvas = new UVCanvas(_target, RecordDrag)
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		root.AddChild(_canvas);
		base._Ready();
	}

	public override void _ExitTree()
	{
		if (_open == this)
			_open = null;
		base._ExitTree();
	}

	private void Project(EditableMesh.MeshProjectionPlane plane) =>
		Apply("Project editable mesh UVs", mesh => mesh.ProjectUVs(plane));

	private void Fit() => Apply("Fit editable mesh UVs", mesh =>
	{
		int[] ids = mesh.GetUVs();
		if (ids.Length == 0)
			return;
		Vector2 min = mesh.GetUV(ids[0]);
		Vector2 max = min;
		foreach (int id in ids)
		{
			Vector2 uv = mesh.GetUV(id);
			min = min.Min(uv);
			max = max.Max(uv);
		}
		Vector2 size = max - min;
		if (Mathf.IsZeroApprox(size.X)) size.X = 1;
		if (Mathf.IsZeroApprox(size.Y)) size.Y = 1;
		foreach (int id in ids)
			mesh.SetUV(id, (mesh.GetUV(id) - min) / size);
	});

	private void Rotate() => Apply("Rotate editable mesh UVs", mesh =>
	{
		foreach (int id in mesh.GetUVs())
		{
			Vector2 uv = mesh.GetUV(id) - new Vector2(0.5f, 0.5f);
			mesh.SetUV(id, new Vector2(-uv.Y, uv.X) + new Vector2(0.5f, 0.5f));
		}
	});

	private void Mirror(bool horizontal) => Apply("Mirror editable mesh UVs", mesh =>
	{
		foreach (int id in mesh.GetUVs())
		{
			Vector2 uv = mesh.GetUV(id);
			mesh.SetUV(id, horizontal ? new Vector2(1f - uv.X, uv.Y) : new Vector2(uv.X, 1f - uv.Y));
		}
	});

	private void Apply(string title, Action<EditableMesh> operation)
	{
		EditableMesh before = Clone(_target.Geometry);
		EditableMesh after = Clone(_target.Geometry);
		after.AutoCommit = false;
		operation(after);
		after.Commit();
		after.AutoCommit = true;
		_target.Geometry = after;
		Record(title, before, after);
		_canvas.QueueRedraw();
	}

	private void RecordDrag(EditableMesh before, EditableMesh after) =>
		Record("Move editable mesh UV", before, after);

	private void Record(string title, EditableMesh before, EditableMesh after)
	{
		World? world = World.Current;
		if (world == null)
			return;
		world.CreatorContext.History.RecordAppliedAction(
			title,
			new BVCallback(_ => { _target.Geometry = after; _canvas.QueueRedraw(); }),
			new BVCallback(_ => { _target.Geometry = before; _canvas.QueueRedraw(); })
		);
	}

	private static EditableMesh Clone(EditableMesh mesh) => mesh.SeparateFaces(mesh.GetFaces());

	private static void AddButton(Control parent, string text, Action action)
	{
		Button button = new() { Text = text, CustomMinimumSize = new Vector2(92, 34) };
		button.Pressed += action;
		parent.AddChild(button);
	}

	private sealed partial class UVCanvas : Control
	{
		private const float Margin = 36f;
		private readonly EditableMeshPart _target;
		private readonly Action<EditableMesh, EditableMesh> _record;
		private int _draggedUv;
		private EditableMesh? _dragBefore;

		public UVCanvas(EditableMeshPart target, Action<EditableMesh, EditableMesh> record)
		{
			_target = target;
			_record = record;
			MouseDefaultCursorShape = CursorShape.Cross;
		}

		public override void _Draw()
		{
			DrawRect(new Rect2(Vector2.Zero, Size), new Color("11141b"));
			Rect2 area = Area();
			DrawRect(area, new Color("202631"));
			for (int index = 0; index <= 10; index++)
			{
				float x = area.Position.X + area.Size.X * index / 10f;
				float y = area.Position.Y + area.Size.Y * index / 10f;
				DrawLine(new Vector2(x, area.Position.Y), new Vector2(x, area.End.Y), new Color("343c49"));
				DrawLine(new Vector2(area.Position.X, y), new Vector2(area.End.X, y), new Color("343c49"));
			}
			EditableMesh mesh = _target.Geometry;
			foreach (int faceId in mesh.GetFaces())
			{
				int[] uvIds = mesh.GetFaceUVs(faceId);
				Vector2[] points = uvIds.Select(id => ToScreen(mesh.GetUV(id))).ToArray();
				for (int corner = 0; corner < 3; corner++)
					DrawLine(points[corner], points[(corner + 1) % 3], new Color("58a6ff"), 1.5f);
			}
			foreach (int uvId in mesh.GetUVs())
				DrawCircle(ToScreen(mesh.GetUV(uvId)), uvId == _draggedUv ? 6 : 4, Colors.White);
		}

		public override void _GuiInput(InputEvent inputEvent)
		{
			if (inputEvent is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
			{
				if (button.Pressed)
				{
					_draggedUv = FindNearest(button.Position);
					if (_draggedUv != 0)
						_dragBefore = Clone(_target.Geometry);
				}
				else if (_draggedUv != 0 && _dragBefore != null)
				{
					_record(_dragBefore, Clone(_target.Geometry));
					_draggedUv = 0;
					_dragBefore = null;
				}
				QueueRedraw();
			}
			else if (inputEvent is InputEventMouseMotion motion && _draggedUv != 0)
			{
				_target.Geometry.SetUV(_draggedUv, ToUv(motion.Position));
				QueueRedraw();
			}
		}

		private int FindNearest(Vector2 position)
		{
			return _target.Geometry.GetUVs()
				.Select(id => (Id: id, Distance: ToScreen(_target.Geometry.GetUV(id)).DistanceTo(position)))
				.Where(item => item.Distance <= 12f)
				.OrderBy(item => item.Distance)
				.Select(item => item.Id)
				.FirstOrDefault();
		}

		private Rect2 Area() => new(new Vector2(Margin, Margin), Size - Vector2.One * Margin * 2);
		private Vector2 ToScreen(Vector2 uv) => Area().Position + uv * Area().Size;
		private Vector2 ToUv(Vector2 point) => (point - Area().Position) / Area().Size;
	}
}

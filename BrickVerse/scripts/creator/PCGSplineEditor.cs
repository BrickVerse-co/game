// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Scripting;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Creator;

/// <summary>Selection-driven viewport tooling for PCGSpline control points.</summary>
public sealed partial class PCGSplineEditor : Node
{
	private const float HandlePickRadius = 14f;
	private readonly Node3D _handles = new() { Name = "PCGSplineHandles" };
	private World _root = null!;
	private PCGSpline? _spline;
	private PanelContainer _panel = null!;
	private Label _summary = null!;
	private int _selectedPoint = -1;
	private bool _dragging;
	private string _dragBefore = "";
	private Vector3 _dragPlanePoint;
	private Vector3 _dragPlaneNormal;
	private Vector3 _dragGrabOffset;

	public void Attach(World root) => _root = root;

	public override void _Ready()
	{
		AddChild(_handles);
		CreatePanel();
		SetProcess(true);
		SetProcessUnhandledInput(true);
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(_panel)) _panel.QueueFree();
		base._ExitTree();
	}

	public override void _Process(double delta)
	{
		PCGSpline? selected = null;
		if (_root.CreatorContext.Selections.SelectedInstances.Count == 1)
			selected = _root.CreatorContext.Selections.SelectedInstances[0] as PCGSpline;
		if (selected != _spline) Activate(selected);
		if (_spline == null) return;
		if (_dragging && CreatorService.Interface.ToolMode is ToolModeEnum.Rotate or ToolModeEnum.Scale)
			FinishDrag();
		_handles.GlobalTransform = _spline.GDNode3D.GlobalTransform;
		UpdateHandleScales();
		string selectedText = _selectedPoint >= 0 ? $"  •  Point {_selectedPoint + 1}" : "";
		_summary.Text = $"{_spline.GetPointCount()} points  •  {_spline.GetLength():0.##} units  •  {_spline.GeneratedInstanceCount} instances{selectedText}\nShift-click surface: add  •  Drag: move  •  Del: remove";
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_spline == null || !_root.CreatorContext.IsViewportFocused) return;
		Camera3D? camera = _root.CreatorContext.Freelook.Camera3D;
		if (camera == null) return;

		if (@event is InputEventKey { Pressed: true, Echo: false } key)
		{
			if (key.Keycode is Key.Delete or Key.Backspace && _selectedPoint >= 0)
			{
				RemoveSelectedPoint(); GetViewport().SetInputAsHandled();
			}
			else if (key.Keycode == Key.Escape) { _selectedPoint = -1; RebuildHandles(); }
			return;
		}

		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
		{
			if (button.Pressed)
			{
				ToolModeEnum toolMode = CreatorService.Interface.ToolMode;
				if (toolMode is ToolModeEnum.Rotate or ToolModeEnum.Scale) return;
				int hit = FindHandle(camera, button.Position);
				if (hit >= 0)
				{
					_selectedPoint = hit; _dragging = true; _dragBefore = _spline.ControlPoints;
					_dragPlanePoint = _spline.GDNode3D.ToGlobal(_spline.GetPoint(hit));
					_dragPlaneNormal = -camera.GlobalBasis.Z.Normalized();
					Vector3? grab = RayPlaneIntersection(camera, button.Position, _dragPlanePoint, _dragPlaneNormal);
					_dragGrabOffset = grab == null ? Vector3.Zero : _dragPlanePoint - grab.Value;
					_root.CreatorContext.Gizmos.SuppressSelectionInput = true;
					RebuildHandles(); GetViewport().SetInputAsHandled();
				}
				else if (button.ShiftPressed)
				{
					Datamodel.Environment.RayResult? placement = _root.CreatorContext.Freelook.GetPlacementRay([_spline]);
					Vector3 worldPoint = placement?.Position ?? RayOnHorizontalPlane(camera, button.Position, _spline.GDNode3D.GlobalPosition.Y);
					ApplyPointAction("Add Spline Point", () => { _spline.AddPoint(_spline.GDNode3D.ToLocal(worldPoint)); _selectedPoint = _spline.GetPointCount() - 1; });
					GetViewport().SetInputAsHandled();
				}
			}
			else if (_dragging)
			{
				FinishDrag();
				GetViewport().SetInputAsHandled();
			}
		}
		else if (@event is InputEventMouseMotion motion && _dragging && _selectedPoint >= 0)
		{
			Vector3? intersection = RayPlaneIntersection(camera, motion.Position, _dragPlanePoint, _dragPlaneNormal);
			if (intersection != null)
			{
				Vector3 local = _spline.GDNode3D.ToLocal(intersection.Value + _dragGrabOffset);
				if (CreatorService.Interface.MoveSnapEnabled) local = local.Snap(CreatorService.Interface.MoveSnapping);
				_spline.SetPointDuringEdit(_selectedPoint, local);
				if (_selectedPoint < _handles.GetChildCount() && _handles.GetChild(_selectedPoint) is Node3D handle) handle.Position = local;
			}
			GetViewport().SetInputAsHandled();
		}
	}

	private void Activate(PCGSpline? spline)
	{
		_dragging = false; _root.CreatorContext.Gizmos.SuppressSelectionInput = false;
		_spline = spline; _selectedPoint = -1; _panel.Visible = spline != null; RebuildHandles();
		if (spline != null) CreatorService.Interface.StatusBar?.SetStatus("Spline editor active — Shift-click to add control points");
	}

	private void FinishDrag()
	{
		if (!_dragging || _spline == null) return;
		_dragging = false; _root.CreatorContext.Gizmos.SuppressSelectionInput = false;
		_spline.Regenerate(); RecordApplied("Move Spline Point", _dragBefore, _spline.ControlPoints);
	}

	private void CreatePanel()
	{
		_panel = new PanelContainer { Name = "PCGSplineTools", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
		_panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_panel.Position = new Vector2(-330, 74); _panel.Size = new Vector2(310, 0);
		VBoxContainer content = new(); _panel.AddChild(content);
		Label title = new() { Text = "PCG SPLINE EDITOR", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 16); content.AddChild(title);
		_summary = new() { Text = "Spline" }; content.AddChild(_summary);
		HBoxContainer row = new(); content.AddChild(row);
		AddButton(row, "Add After", AddAfterSelected);
		AddButton(row, "Delete", RemoveSelectedPoint);
		AddButton(row, "Reverse", ReversePoints);
		AddButton(row, "Clear", ClearPoints);
		GridContainer generation = new() { Columns = 2 }; content.AddChild(generation);
		AddButton(generation, "Regenerate", () => _spline?.Regenerate());
		AddButton(generation, "Bake", BakeInstances);
		AddButton(generation, "Toggle Loop", ToggleClosed);
		AddButton(generation, "Curve Mode", ToggleInterpolation);
		_root.Container!.AddChild(_panel);
	}

	private static void AddButton(Control parent, string text, Action pressed)
	{
		Button button = new() { Text = text }; button.Pressed += pressed; parent.AddChild(button);
	}

	private void AddAfterSelected()
	{
		if (_spline == null) return;
		ApplyPointAction("Add Spline Point", () =>
		{
			int count = _spline.GetPointCount();
			if (count == 0) { _spline.AddPoint(Vector3.Zero); _selectedPoint = 0; return; }
			int after = _selectedPoint >= 0 ? _selectedPoint : count - 1;
			int insertAt = after + 1;
			Vector3 point;
			if (insertAt < count) point = (_spline.GetPoint(after) + _spline.GetPoint(insertAt)) * 0.5f;
			else if (count > 1) point = _spline.GetPoint(after) + (_spline.GetPoint(after) - _spline.GetPoint(after - 1));
			else point = _spline.GetPoint(after) + Vector3.Forward * _spline.Spacing * 2;
			_spline.InsertPoint(insertAt, point); _selectedPoint = insertAt;
		});
	}

	private void RemoveSelectedPoint()
	{
		if (_spline == null || _selectedPoint < 0 || _selectedPoint >= _spline.GetPointCount()) return;
		ApplyPointAction("Delete Spline Point", () => { _spline.RemovePoint(_selectedPoint); _selectedPoint = Math.Min(_selectedPoint, _spline.GetPointCount() - 1); });
	}

	private void ReversePoints()
	{
		if (_spline == null) return;
		ApplyPointAction("Reverse Spline", () => { for (int left = 0, right = _spline.GetPointCount() - 1; left < right; left++, right--) { Vector3 value = _spline.GetPoint(left); _spline.SetPoint(left, _spline.GetPoint(right)); _spline.SetPoint(right, value); } });
	}

	private void ClearPoints() { if (_spline != null) ApplyPointAction("Clear Spline", _spline.ClearPoints); }
	private void BakeInstances()
	{
		if (_spline == null) return; Instance[] baked = _spline.Bake(); if (baked.Length == 0) { CreatorService.Interface.StatusBar?.SetStatus("Add one or more Dynamic children to use as spline templates"); return; }
		_root.CreatorContext.History.RecordAppliedAction("Bake Spline Instances", new BVCallback((_) => { foreach (Instance item in baked) item.Parent = _spline.Parent; }), new BVCallback((_) => { foreach (Instance item in baked) item.Parent = _root.TemporaryContainer; }));
		CreatorService.Interface.StatusBar?.SetStatus($"Baked {baked.Length} spline instances");
	}
	private void ToggleClosed() { if (_spline != null) { bool before = _spline.Closed; _root.CreatorContext.History.NewAction("Toggle Closed Spline"); _root.CreatorContext.History.AddDoCallback(new BVCallback((_) => _spline.Closed = !before)); _root.CreatorContext.History.AddUndoCallback(new BVCallback((_) => _spline.Closed = before)); _root.CreatorContext.History.CommitAction(); RebuildHandles(); } }
	private void ToggleInterpolation()
	{
		if (_spline == null) return; PCGSpline.InterpolationModeEnum before = _spline.Interpolation;
		PCGSpline.InterpolationModeEnum after = before == PCGSpline.InterpolationModeEnum.CatmullRom ? PCGSpline.InterpolationModeEnum.Linear : PCGSpline.InterpolationModeEnum.CatmullRom;
		_root.CreatorContext.History.NewAction("Change Spline Interpolation");
		_root.CreatorContext.History.AddDoCallback(new BVCallback((_) => _spline.Interpolation = after));
		_root.CreatorContext.History.AddUndoCallback(new BVCallback((_) => _spline.Interpolation = before));
		_root.CreatorContext.History.CommitAction(); CreatorService.Interface.StatusBar?.SetStatus($"Spline interpolation: {after}");
	}

	private void ApplyPointAction(string title, Action action)
	{
		if (_spline == null) return; string before = _spline.ControlPoints; action(); string after = _spline.ControlPoints;
		RecordApplied(title, before, after); RebuildHandles();
	}

	private void RecordApplied(string title, string before, string after)
	{
		if (_spline == null || before == after) return; PCGSpline target = _spline;
		_root.CreatorContext.History.RecordAppliedAction(title, new BVCallback((_) => { target.ControlPoints = after; RebuildHandles(); }), new BVCallback((_) => { target.ControlPoints = before; RebuildHandles(); }));
	}

	private void RebuildHandles()
	{
		foreach (Node child in _handles.GetChildren()) child.QueueFree();
		if (_spline == null) return;
		for (int i = 0; i < _spline.GetPointCount(); i++)
		{
			StandardMaterial3D material = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = i == _selectedPoint ? new Color("ffd34e") : new Color("46b9ff"), NoDepthTest = true };
			MeshInstance3D handle = new() { Position = _spline.GetPoint(i), Mesh = new SphereMesh { Radius = 0.28f, Height = 0.56f, Material = material }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			_handles.AddChild(handle);
		}
	}

	private void UpdateHandleScales()
	{
		Camera3D camera = _root.CreatorContext.Freelook.Camera3D;
		foreach (Node child in _handles.GetChildren()) if (child is Node3D handle) { float distance = camera.GlobalPosition.DistanceTo(handle.GlobalPosition); handle.Scale = Vector3.One * Mathf.Clamp(distance * 0.025f, 0.5f, 8f); }
	}

	private int FindHandle(Camera3D camera, Vector2 mouse)
	{
		if (_spline == null) return -1; int best = -1; float bestDistance = HandlePickRadius;
		for (int i = 0; i < _spline.GetPointCount(); i++) { Vector3 world = _spline.GDNode3D.ToGlobal(_spline.GetPoint(i)); if (camera.IsPositionBehind(world)) continue; float distance = camera.UnprojectPosition(world).DistanceTo(mouse); if (distance < bestDistance) { bestDistance = distance; best = i; } }
		return best;
	}

	private static Vector3? RayPlaneIntersection(Camera3D camera, Vector2 mouse, Vector3 planePoint, Vector3 planeNormal)
	{
		Vector3 origin = camera.ProjectRayOrigin(mouse), direction = camera.ProjectRayNormal(mouse).Normalized(); float denominator = direction.Dot(planeNormal);
		if (Mathf.Abs(denominator) < 0.0001f) return null; float distance = (planePoint - origin).Dot(planeNormal) / denominator; return distance >= 0 ? origin + direction * distance : null;
	}

	private static Vector3 RayOnHorizontalPlane(Camera3D camera, Vector2 mouse, float height) => RayPlaneIntersection(camera, mouse, new Vector3(0, height, 0), Vector3.Up) ?? camera.ProjectRayOrigin(mouse) + camera.ProjectRayNormal(mouse) * 20;
}

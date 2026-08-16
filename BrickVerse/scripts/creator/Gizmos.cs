// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.Spatial;
using BrickVerse.Creator.UI;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Datamodel.Interfaces;
using BrickVerse.Utils;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Creator;

public sealed partial class Gizmos : Node
{
	public const float GizmoCircleSize = 1.1f;
	public const float GizmoArrowSize = 0.35f;
	public const float MaxZ = 1000000f;

	public World Root = null!;
	private readonly Dictionary<Dynamic, SelectionBox> _selectionBoxes = [];

	private Camera3D _camera = null!;
	private bool _isDraggingDyn;
	private bool _isDragPending;
	private bool _duplicatedOnCurrentDrag;
	private Vector2 _dragStartPos;
	private Vector2 _rightClickStart;
	private bool _rightClickPending;
	private Instance? _rightClickTarget;
	private float _rightClickTravel;
	private const float DragThreshold = 6f;
	private const float MinimumScale = 0.01f;
	private const float MinimumSnap = 0.0001f;
	private SelectionBox _paintBox = null!;
	private SelectionBox _hoverBox = null!;

	public bool HoveringGizmos { get; set; }
	public bool HoveringUIGizmo { get; set; }
	public bool IsDraggingDynamic => _isDraggingDyn;
	public bool IsTransformingSelected { get; private set; }

	public static Color[] AxisColors { get; private set; } =
	[
			new(0.96f, 0.20f, 0.32f),
			new(0.53f, 0.84f, 0.01f),
			new(0.16f, 0.55f, 0.96f),
	];

	public MoveGizmo Move = new();
	public RotateGizmo Rotate = new();
	public ScaleGizmo Scale = new();
	public ResizeGizmo Resize = new();

	public List<Dynamic> Selected = [];
	public List<Dynamic> DragSelected = [];

	private readonly Dictionary<Dynamic, Vector3> _dragStartOffsets = [];
	private readonly Dictionary<Dynamic, Transform3D> _selectDragStartTransforms = [];
	private readonly Dictionary<Dynamic, Transform3D> _initialRelativeTransforms = [];
	private Transform3D _pivotStart;
	private CreatorHistory _history = null!;
	private Vector3 _lastMoveMotion = Vector3.Zero;

	public void Attach(World game)
	{
		Root = game;
		game.Loaded.Once(() =>
		{
			_history = Root.CreatorContext.History;
		});
	}

	public override void _Ready()
	{
		_camera = Root.CreatorContext.Freelook.Camera3D;
		Move.RootGizmos = this;
		Rotate.RootGizmos = this;
		Scale.RootGizmos = this;
		Resize.RootGizmos = this;
		Move.Name = "Move";
		Rotate.Name = "Rotate";
		Scale.Name = "Scale";
		Resize.Name = "Resize";

		Move.DragStarted += OnMoveDragStarted;
		Move.DragEnded += OnMoveDragEnded;
		Move.Dragged += OnMoveDragged;

		Rotate.DragStarted += OnRotateDragStarted;
		Rotate.DragEnded += OnRotateDragEnded;
		Rotate.Dragged += OnRotateDragged;

		Scale.DragStarted += OnScaleDragStarted;
		Scale.DragEnded += OnScaleDragEnded;
		Scale.Dragged += OnScaleDragged;

		Resize.DragStarted += OnResizeDragStarted;
		Resize.DragEnded += OnResizeDragEnded;
		Resize.Dragged += OnResizeDragged;

		AddChild(Move, true);
		AddChild(Rotate, true);
		AddChild(Scale, true);
		AddChild(Resize, true);
		AddChild(_paintBox = new() { Root = Root, Name = "PaintBox", RootGizmos = this });
		AddChild(_hoverBox = new() { Root = Root, Name = "HoverBox", RootGizmos = this });
	}

	private void OnResizeDragStarted()
	{
		IsTransformingSelected = true;
		_pivotStart = Selected[0].GetGlobalTransform();
		_history.NewAction("Resize Transform");
		RecordHistoryUndo();
	}

	private void OnResizeDragged(ResizeGizmo.ResizeGizmoAxis currentAxis, Vector3 rawMotion)
	{
		float moveSnap = GetSafeMoveSnap();
		bool isAltPressed = Input.IsActionPressed("gizmo_scale_uniform");
		bool isShiftPressed = Input.IsKeyPressed(Key.Shift);

		float scaleFactor = isAltPressed ? 2.0f : 1.0f;

		// 0 means x, 1 means y, 2 means z
		int column = (int)currentAxis >> 1;
		// -1 means negative direction, 1 means positive direction
		int globalDirection = ((int)currentAxis & 1) == 1 ? 1 : -1;

		Dynamic selectedItem = Selected[0];
		Vector3 oldOrigin = _pivotStart.Origin;
		Basis newBasis = _pivotStart.Basis;

		Vector3 oldScaleVector = _pivotStart.Basis[column];
		Vector3 currentAxisVector = oldScaleVector * globalDirection;
		Vector3 axisDir = currentAxisVector.Normalized();
		float snappedDelta = Mathf.Snapped(rawMotion.Dot(axisDir), moveSnap);
		Vector3 resizeDirection = oldScaleVector.Normalized();
		Vector3 newScaleVector = oldScaleVector + resizeDirection * snappedDelta * scaleFactor;

		// Apply minimum size & prevent negative scale
		float newLength = newScaleVector.Length();

		if (newLength < moveSnap || newScaleVector.Dot(resizeDirection) < 0)
		{
			newLength = moveSnap;
			newScaleVector = resizeDirection * moveSnap;
			snappedDelta = (moveSnap - oldScaleVector.Length()) / scaleFactor;
		}

		float ratio = newLength / oldScaleVector.Length();

		Vector3 totalOriginOffset = Vector3.Zero;

		for (int i = 0; i < 3; i++)
		{
			if (i == column)
			{
				// Primary Axis
				newBasis[i] = newScaleVector;
				if (!isAltPressed)
				{
					totalOriginOffset += (globalDirection * snappedDelta * _pivotStart.Basis[i].Normalized() / 2);
				}
			}
			else if (isShiftPressed)
			{
				// Uniform Scaling for other axes
				float oldColLength = _pivotStart.Basis[i].Length();
				float newColLength = oldColLength * ratio;
				newBasis[i] = _pivotStart.Basis[i].Normalized() * newColLength;
			}
		}

		Transform3D newTransform = new(newBasis, oldOrigin + totalOriginOffset);
		selectedItem.SetGlobalTransform(newTransform);
	}

	private void OnResizeDragEnded()
	{
		IsTransformingSelected = false;
		CommitHistorySelectedTransform();
	}

	private void OnScaleDragStarted()
	{
		IsTransformingSelected = true;
		_pivotStart = GetSelectionPivot();
		_initialRelativeTransforms.Clear();

		// Store each object's transform relative to pivot
		foreach (Dynamic item in Selected)
		{
			Transform3D itemTransform = item.GetGlobalTransform();
			Transform3D relative = _pivotStart.AffineInverse() * itemTransform;
			_initialRelativeTransforms[item] = relative;
		}
		_history.NewAction("Scale Transform");
		RecordHistoryUndo();
	}

	private void OnScaleDragged(Vector3 vector)
	{
		Vector3 scaleFactors;
		float snapValue = GetSafeMoveSnap() / 10.0f;
		if (Input.IsActionPressed("gizmo_scale_uniform"))
		{
			float maxChange = vector.X;
			if (Mathf.Abs(vector.Y) > Mathf.Abs(maxChange)) maxChange = vector.Y;
			if (Mathf.Abs(vector.Z) > Mathf.Abs(maxChange)) maxChange = vector.Z;

			float snappedChange = Mathf.Snapped(maxChange, snapValue);
			scaleFactors = Vector3.One * (1.0f + snappedChange);
		}
		else
		{
			scaleFactors = Vector3.One + vector.Snap(snapValue);
		}
		Basis scaledBasis = _pivotStart.Basis.Scaled(new(
			Mathf.Max(MinimumScale, scaleFactors.X),
			Mathf.Max(MinimumScale, scaleFactors.Y),
			Mathf.Max(MinimumScale, scaleFactors.Z)
			));

		Transform3D scaledPivot = new(
			scaledBasis,
			_pivotStart.Origin
		);

		foreach ((Dynamic item, Transform3D relative) in _initialRelativeTransforms)
		{
			item.SetGlobalTransform(scaledPivot * relative);
		}
	}

	private void OnScaleDragEnded()
	{
		IsTransformingSelected = false;
		CommitHistorySelectedTransform();
		_initialRelativeTransforms.Clear();
	}

	private void OnRotateDragStarted()
	{
		IsTransformingSelected = true;
		_pivotStart = GetSelectionPivot();
		_initialRelativeTransforms.Clear();

		// Store each object's transform relative to pivot
		foreach (Dynamic item in Selected)
		{
			Transform3D itemTransform = item.GetGlobalTransform();
			Transform3D relative = _pivotStart.AffineInverse() * itemTransform;
			_initialRelativeTransforms[item] = relative;
		}
		_history.NewAction("Rotate Transform");
		RecordHistoryUndo();
	}

	private void OnRotateDragged(Basis basis)
	{
		basis = SnapBasis(basis, _pivotStart.Basis, CreatorService.Interface.RotateSnapping);

		Transform3D rotatedPivot = new(basis, _pivotStart.Origin);

		foreach ((Dynamic item, Transform3D relative) in _initialRelativeTransforms)
		{
			item.SetGlobalTransform(rotatedPivot * relative);
		}
	}

	private void OnRotateDragEnded()
	{
		IsTransformingSelected = false;
		CommitHistorySelectedTransform();
		_initialRelativeTransforms.Clear();
	}

	private static Basis SnapBasis(Basis basis, Basis originalBasis, float deg)
	{
		if (deg <= MinimumSnap)
		{
			return basis;
		}

		float snapAngle = Mathf.DegToRad(deg);

		Basis deltaBasis = basis * originalBasis.Inverse();

		Quaternion quat = new(deltaBasis);
		Vector3 axis = quat.GetAxis();
		float angle = quat.GetAngle();

		float snappedAngle = Mathf.Round(angle / snapAngle) * snapAngle;

		Basis snappedDelta = new(axis, snappedAngle);
		return snappedDelta * originalBasis;
	}

	private void OnMoveDragged(Vector3 vector)
	{
		_lastMoveMotion = vector;
		ApplyMoveMotion(vector, snap: true);
	}

	private void OnMoveDragEnded()
	{
		if (Selected.Count > 0)
		{
			ApplyMoveMotion(_lastMoveMotion, snap: true);
		}
		IsTransformingSelected = false;
		CommitHistorySelectedTransform();
	}

	private void OnMoveDragStarted()
	{
		IsTransformingSelected = true;
		_lastMoveMotion = Vector3.Zero;
		_dragStartOffsets.Clear();

		foreach (Dynamic item in Selected)
		{
			_dragStartOffsets[item] = item.GetGlobalTransform().Origin;
		}

		_history.NewAction("Move Transform");
		RecordHistoryUndo();
	}

	private void ApplyMoveMotion(Vector3 motion, bool snap)
	{
		Vector3 appliedMotion = snap && CreatorService.Interface.MoveSnapEnabled
			? motion.Snap(GetSafeMoveSnap())
			: motion;

		foreach (Dynamic item in Selected)
		{
			if (_dragStartOffsets.TryGetValue(item, out Vector3 offset))
			{
				Transform3D current = item.GetGlobalTransform();
				current.Origin = appliedMotion + offset;
				item.SetGlobalTransform(current);
			}
		}
	}

	private void ApplySelectDragMotion(Vector3 motion)
	{
		foreach (Dynamic item in DragSelected)
		{
			if (_selectDragStartTransforms.TryGetValue(item, out Transform3D startTransform))
			{
				Transform3D updated = startTransform;
				updated.Origin += motion;
				item.SetGlobalTransform(updated);
			}
		}
	}

	private void RecordHistoryUndo()
	{
		RecordHistoryUndo(Selected);
	}

	private void RecordHistoryUndo(IEnumerable<Dynamic> targets)
	{
		foreach (Dynamic item in targets.Distinct())
		{
			Transform3D transform = item.GetGlobalTransform();
			_history.AddUndoCallback(new((_) =>
			{
				item.SetGlobalTransform(transform);
				item.PropagateUpdateCreatorBounds();
			}));
		}
	}

	private void CommitHistorySelectedTransform()
	{
		CommitHistoryTransform(Selected);
	}

	private void CommitHistoryTransform(IEnumerable<Dynamic> targets)
	{
		foreach (Dynamic item in targets.Distinct())
		{
			Transform3D transform = item.GetGlobalTransform();
			_history.AddDoCallback(new((_) =>
			{
				item.SetGlobalTransform(transform);
				item.PropagateUpdateCreatorBounds();
			}));

			item.PropagateUpdateCreatorBounds();
		}
		_history.CommitAction();
	}

	public override void _Process(double delta)
	{
		bool selectionValid = Selected.Count > 0;

		Move.Visible = CreatorService.Interface.ToolMode == ToolModeEnum.Move && selectionValid;
		Rotate.Visible = CreatorService.Interface.ToolMode == ToolModeEnum.Rotate && selectionValid;

		if (CreatorService.Interface.ToolMode == ToolModeEnum.Scale && selectionValid)
		{
			bool singlePartSelected = Selected.Count == 1 && Selected[0] is Part;
			Resize.Visible = singlePartSelected;
			Scale.Visible = !singlePartSelected;
		}
		else
		{
			Resize.Visible = false;
			Scale.Visible = false;
		}
	}

	public void Select(Dynamic dyn)
	{
		SelectionBox box = new()
		{
			Root = Root,
			Target = dyn,
			RootGizmos = this
		};
		AddChild(box);
		_selectionBoxes[dyn] = box;
		Selected.Add(dyn);
		Move.Targets.Add(dyn);
		Rotate.Targets.Add(dyn);
		Scale.Targets.Add(dyn);
		Resize.Targets.Add(dyn);
	}

	public void Deselect(Dynamic dyn)
	{
		if (_selectionBoxes.TryGetValue(dyn, out SelectionBox? box))
		{
			box.Target = null;
			_selectionBoxes.Remove(dyn);
			box.QueueFree();
		}
		if (Selected.Contains(dyn))
		{
			// Reset hover gizmo state when deselected
			HoveringGizmos = false;
		}
		Selected.Remove(dyn);
		Move.Targets.Remove(dyn);
		Rotate.Targets.Remove(dyn);
		Scale.Targets.Remove(dyn);
		Resize.Targets.Remove(dyn);
	}

	public static Instance? GetModelRoot(Instance instance)
	{
		Instance? current = instance;
		Instance? authoredRoot = null;

		while (current != null)
		{
			// Saved/imported prefabs explicitly point every member at their root.
			// Prefer that boundary even when the root is not a Model subclass.
			if (current.ModelRoot != null)
				authoredRoot = current.ModelRoot;

			// Models and folders are Creator grouping boundaries. Continue walking
			// so nested content selects the outer authored group by default.
			if (current is IGroup)
				authoredRoot = current;

			// A linked model is itself a prefab boundary.
			if (current.LinkedModel != null)
				authoredRoot = current;

			current = current.Parent;
		}

		return authoredRoot ?? instance;
	}

	public override void _Input(InputEvent @event)
	{
		if (!Root.CreatorContext.IsViewportFocused) { return; }
		ToolModeEnum toolMode = CreatorService.Interface.ToolMode;

		Vector2 mousePos = _camera.GetViewport().GetMousePosition();

		Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePos);
		Vector3 rayNormal = rayOrigin + _camera.ProjectRayNormal(mousePos) * 1000;

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayNormal);
		query.CollideWithAreas = true;
		query.CollideWithBodies = true;
		query.CollisionMask = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3);

		Godot.Collections.Dictionary? intersection = Root.World3D.DirectSpaceState.IntersectRay(query);

		Dynamic? hoveringOn = null;
		if (intersection.Count > 0)
		{
			Node collider = (Node)intersection["collider"];
			hoveringOn = Dynamic.GetDynFromCreatorBounds(collider);
			if (hoveringOn == null && collider is CollisionObject3D colObj)
			{
				hoveringOn = Physical.GetPhysicalFromBody(colObj);
				if (hoveringOn == null)
				{
					hoveringOn = Physical.GetPhysicalFromCollider(collider);
				}
			}
		}

		if (toolMode == ToolModeEnum.Paint)
		{
			if (hoveringOn != null && hoveringOn is Part && !hoveringOn.Locked)
			{
				_paintBox.SelectionColor = CreatorService.Interface.TargetPartColor;
				_paintBox.Target = hoveringOn;
			}
			else
			{
				_paintBox.Target = null;
			}
		}

		Instance? selectInstance = null;

		if (hoveringOn != null)
		{
			// A plain click selects the complete authored model/prefab. Holding a
			// selection modifier deliberately drills into the part beneath the cursor.
			bool drillIntoModel = Input.IsKeyPressed(Key.Alt)
				|| Input.IsKeyPressed(Key.Ctrl)
				|| Input.IsKeyPressed(Key.Shift);
			selectInstance = drillIntoModel
				? hoveringOn
				: GetModelRoot(hoveringOn) ?? hoveringOn;
		}

		if (selectInstance is Dynamic sdyn && !sdyn.Locked)
		{
			_hoverBox.Target = sdyn;
		}
		else
		{
			_hoverBox.Target = null;
		}

		if (Selected.Count > 0)
		{
			if (@event is InputEventKey { Pressed: true, Echo: false, CtrlPressed: true, ShiftPressed: false, Keycode: Key.L })
			{
				Dynamic[] targets = [.. Selected]; Transform3D[] before = targets.Select(target => target.GetGlobalTransform()).ToArray();
				_history.NewAction("Align selection to world axes");
				_history.AddDoCallback(new((_) => { foreach (Dynamic target in targets) target.Rotation = Vector3.Zero; }));
				_history.AddUndoCallback(new((_) => { for (int i = 0; i < targets.Length; i++) targets[i].SetGlobalTransform(before[i]); }));
				_history.CommitAction(); CreatorService.Interface.StatusBar?.SetStatus("Aligned selection to world axes");
			}
			else if (CreatorKeybindResolver.IsPressed(@event, CreatorSettingKeys.Keybinds.ToggleTransformOrientation, Key.L))
			{
				TransformOrientationEnum nextOrientation = CreatorService.Interface.TransformOrientation == TransformOrientationEnum.Global
					? TransformOrientationEnum.Local
					: TransformOrientationEnum.Global;
				CreatorSettingsService.Instance.Set(CreatorSettingKeys.Interface.TransformOrientation, nextOrientation);
				CreatorService.Interface.StatusBar?.SetStatus($"Transform Orientation: {nextOrientation}");
				if (_isDraggingDyn) RebaseActiveDirectDrag();
			}

			if (CreatorKeybindResolver.IsPressed(@event, CreatorSettingKeys.Keybinds.TogglePivotMode, Key.P))
			{
				SelectionPivotModeEnum nextPivot = CreatorService.Interface.SelectionPivotMode == SelectionPivotModeEnum.Center
					? SelectionPivotModeEnum.PrimarySelection
					: SelectionPivotModeEnum.Center;
				CreatorSettingsService.Instance.Set(CreatorSettingKeys.Interface.SelectionPivotMode, nextPivot);
				CreatorService.Interface.StatusBar?.SetStatus($"Selection Pivot: {nextPivot}");
				if (_isDraggingDyn) RebaseActiveDirectDrag();
			}

			// Selection orientation shortcuts
			if (CreatorKeybindResolver.IsPressed(@event, CreatorSettingKeys.Keybinds.RotateSelection, Key.R))
			{
				RotateSelectedAround(90);
				if (_isDraggingDyn)
				{
					RebaseActiveDirectDrag();
				}
			}
			if (CreatorKeybindResolver.IsPressed(@event, CreatorSettingKeys.Keybinds.TiltSelection, Key.T))
			{
				TiltSelectedAround(90);
				if (_isDraggingDyn)
				{
					RebaseActiveDirectDrag();
				}
			}
		}

		if (@event is InputEventMouseButton button)
		{
			if (HoveringGizmos || HoveringUIGizmo) { return; }
			if (button.ButtonIndex == MouseButton.Right)
			{
				if (button.Pressed)
				{
					_rightClickStart = button.Position;
					_rightClickTarget = selectInstance;
					_rightClickTravel = 0;
					_rightClickPending = true;
				}
				else if (_rightClickPending && _rightClickTravel < DragThreshold)
				{
					ShowViewportContextMenu(_rightClickTarget);
					_rightClickPending = false;
					_rightClickTarget = null;
				}
				return;
			}
			if (button.ButtonIndex != MouseButton.Left) { return; }
			if (button.Pressed)
			{
				_dragStartPos = button.Position;
				_duplicatedOnCurrentDrag = false;
			}
			else
			{
				_isDragPending = false;
				if (_isDraggingDyn)
				{
					_isDraggingDyn = false;
					IsTransformingSelected = false;
					CommitHistoryTransform(DragSelected);
				}
				_selectDragStartTransforms.Clear();
				DragSelected.Clear();
				return;
			}
			bool isMultiSelect = Input.IsActionPressed("gizmo_multi_select");

			if (hoveringOn != null)
			{
				// Force select NPC instead of CharacterModel
				if (selectInstance?.Parent is NPC)
				{
					selectInstance = selectInstance.Parent;
				}

				// Don't select creator freelook/current cam
				if (selectInstance == Root.Environment.CurrentCamera)
				{
					selectInstance = null;
				}

				if (selectInstance != null && selectInstance is Dynamic targetDyn)
				{
					if (targetDyn.Locked && !isMultiSelect)
					{
						Root.CreatorContext.Selections.DeselectAll();
						return;
					}

					if (isMultiSelect)
					{
						if (Root.CreatorContext.Selections.HasSelected(targetDyn))
						{
							Root.CreatorContext.Selections.Deselect(targetDyn);
						}
						else
						{
							ProcessPaint(hoveringOn);
							Root.CreatorContext.Selections.Select(targetDyn);
						}
					}
					else
					{
						ProcessPaint(hoveringOn);
						Root.CreatorContext.Selections.SelectOnly(targetDyn);
					}

					bool canDirectDrag = toolMode == ToolModeEnum.Select
						|| toolMode == ToolModeEnum.Move
						|| toolMode == ToolModeEnum.Rotate
						|| toolMode == ToolModeEnum.Scale;

					if (canDirectDrag)
					{
						DragSelected.Clear();

						// If the clicked instance is already selected, drag the full selection.
						if (Root.CreatorContext.Selections.HasSelected(targetDyn))
						{
							foreach (Dynamic selectedDyn in Selected)
							{
								if (!selectedDyn.Locked)
								{
									DragSelected.Add(selectedDyn);
								}
							}
						}
						else
						{
							DragSelected.Add(targetDyn);
						}

						_isDragPending = true;
					}
				}
			}
			else
			{
				if (!isMultiSelect)
				{
					Root.CreatorContext.Selections.DeselectAll();
				}
			}
		}
		else if (@event is InputEventMouseMotion motion)
		{
			if (_rightClickPending)
			{
				_rightClickTravel += motion.Relative.Length();
				if (_rightClickTravel >= DragThreshold)
				{
					_rightClickPending = false;
					_rightClickTarget = null;
				}
			}
			if (_isDragPending && !_isDraggingDyn)
			{
				float distance = motion.Position.DistanceTo(_dragStartPos);
				if (distance >= DragThreshold)
				{
					if (
						CreatorService.Interface.DuplicateOnDragEnabled
						&& Input.IsKeyPressed(Key.Ctrl)
						&& !_duplicatedOnCurrentDrag
					)
					{
						Instance[] source = [.. Root.CreatorContext.Selections.SelectedInstances];
						if (source.Length > 0)
						{
							Root.CreatorContext.History.DuplicateInstances(source);
							DragSelected.Clear();
							foreach (Instance selected in Root.CreatorContext.Selections.SelectedInstances)
							{
								if (selected is Dynamic selectedDyn && !selectedDyn.Locked)
								{
									DragSelected.Add(selectedDyn);
								}
							}
							_duplicatedOnCurrentDrag = true;
						}
					}

					_isDraggingDyn = true;
					_isDragPending = false;
					IsTransformingSelected = true;
					_pivotStart = GetSelectionPivot();
					_selectDragStartTransforms.Clear();
					foreach (Dynamic item in DragSelected)
					{
						_selectDragStartTransforms[item] = item.GetGlobalTransform();
					}
					_history.NewAction("Move Selection");
					RecordHistoryUndo(DragSelected);
				}
			}

			if (_isDraggingDyn)
			{
				DragSelectedDynamics();
			}
		}

	}

	private void ShowViewportContextMenu(Instance? clicked)
	{
		if (clicked != null && !Root.CreatorContext.Selections.HasSelected(clicked))
			Root.CreatorContext.Selections.SelectOnly(clicked);

		List<Instance> selectedInstances = [.. Root.CreatorContext.Selections.SelectedInstances];
		if (selectedInstances.Count > 0)
		{
			ExplorerItemContextMenu instanceMenu = new() { Targets = selectedInstances };
			CreatorService.Interface.AddChild(instanceMenu);
			instanceMenu.PopupAtCursor();
			return;
		}

		PopupMenu menu = new();
		menu.AddItem("Insert Instance...", 1);
		menu.AddItem("Paste", 2);
		menu.IdPressed += id =>
		{
			switch (id)
			{
				case 1:
					CreatorService.Interface.OpenInsertMenu(clicked);
					break;
				case 2:
					_ = CreatorService.Clipboard.PasteClipboard(true);
					break;
			}
			menu.QueueFree();
		};
		menu.PopupHide += menu.QueueFree;
		CreatorService.Interface.AddChild(menu);
		menu.Popup(new Rect2I((Vector2I)_camera.GetViewport().GetMousePosition(), Vector2I.Zero));
	}

	private void RebaseActiveDirectDrag()
	{
		if (!_isDraggingDyn)
			return;

		_dragStartPos = _camera.GetViewport().GetMousePosition();
		_pivotStart = GetSelectionPivot();
		_selectDragStartTransforms.Clear();

		foreach (Dynamic item in DragSelected)
		{
			_selectDragStartTransforms[item] = item.GetGlobalTransform();
		}
	}

	private void RotateSelectedAround(float angle)
	{
		if (Selected.Count == 0) return;

		_history.NewAction("Rotate Selection");
		RecordHistoryUndo();

		Transform3D t = GetCenterPivot([.. Selected]);
		Vector3 pivotPosition = t.Origin;
		float rotateAngle = Mathf.DegToRad(angle);

		foreach (Dynamic item in Selected)
		{
			Vector3 relativePos = item.GetGlobalPosition() - pivotPosition;
			Transform3D rotation = Transform3D.Identity.Rotated(Vector3.Up, rotateAngle);
			Vector3 rotatedPos = rotation * relativePos;

			item.SetGlobalPosition(pivotPosition + rotatedPos);
			item.GDNode3D.Rotation += new Vector3(0, rotateAngle, 0);

			if (_selectionBoxes.TryGetValue(item, out var box)) box.InvalidateBoundCache();
			item.UpdateCurrentTransformCache();
		}

		CommitHistorySelectedTransform();
	}

	private void TiltSelectedAround(float angle)
	{
		if (Selected.Count == 0) return;

		_history.NewAction("Tilt Selection");
		RecordHistoryUndo();

		Transform3D t = GetCenterPivot([.. Selected]);
		Vector3 pivotPosition = t.Origin;
		float tiltAngle = Mathf.DegToRad(angle);

		Vector3 cameraPosition = GetViewport().GetCamera3D().GlobalPosition;
		Vector3 directionToCamera = (cameraPosition - pivotPosition).Normalized();

		directionToCamera.Y = 0;
		directionToCamera = directionToCamera.Normalized();

		float angleToCamera = Mathf.Atan2(directionToCamera.X, directionToCamera.Z);
		float snappedAngle = Mathf.Round(angleToCamera / (Mathf.Pi / 2)) * (Mathf.Pi / 2);

		Vector3 tiltAxis = new(Mathf.Cos(snappedAngle), 0, Mathf.Sin(snappedAngle));

		foreach (Dynamic item in Selected)
		{
			Vector3 relativePos = item.GetGlobalPosition() - pivotPosition;
			Transform3D rotation = Transform3D.Identity.Rotated(tiltAxis, tiltAngle);
			Vector3 rotatedPos = rotation * relativePos;
			item.SetGlobalPosition(pivotPosition + rotatedPos);

			// Apply rotation to the object itself
			item.GDNode3D.Rotate(tiltAxis, tiltAngle);

			if (_selectionBoxes.TryGetValue(item, out var box)) box.InvalidateBoundCache();
			item.UpdateCurrentTransformCache();
		}

		CommitHistorySelectedTransform();
	}

	private void ProcessPaint(Dynamic dyn)
	{
		if (dyn is Part p)
		{
			CreatorHistory history = Root.CreatorContext.History;
			if (CreatorService.Interface.ToolMode == ToolModeEnum.Paint)
			{
				Color oldC = p.Color;
				Color newC = CreatorService.Interface.TargetPartColor;
				history.NewAction("Paint Part");
				history.AddDoCallback(new((_) =>
				{
					p.Color = newC;
				}));
				history.AddUndoCallback(new((_) =>
				{
					p.Color = oldC;
				}));
				history.CommitAction();
			}
			else if (CreatorService.Interface.ToolMode == ToolModeEnum.Brush)
			{
				Part.PartMaterialEnum oldC = p.Material;
				Part.PartMaterialEnum newC = CreatorService.Interface.TargetPartMaterial;
				history.NewAction("Brush Part");
				history.AddDoCallback(new((_) =>
				{
					p.Material = newC;
				}));
				history.AddUndoCallback(new((_) =>
				{
					p.Material = oldC;
				}));
				history.CommitAction();
			}
		}
	}

	private void DragSelectedDynamics()
	{
		if (DragSelected.Count == 0) return;

		Vector2 mousePos = _camera.GetViewport().GetMousePosition();
		Vector3 motion;
		Instance[] ignoreList = [.. DragSelected];
		Datamodel.Environment.RayResult? hit = Root.Environment.CurrentCamera?.ScreenPointToRay(mousePos, ignoreList);

		if (CreatorService.Interface.SnapToPartEnabled && hit.HasValue)
		{
			Vector3 halfExtents = GetCombinedHalfExtents([.. DragSelected]);
			Vector3 normal = hit.Value.Normal.Normalized();
			float pushOut =
				Mathf.Abs(normal.X) * halfExtents.X
				+ Mathf.Abs(normal.Y) * halfExtents.Y
				+ Mathf.Abs(normal.Z) * halfExtents.Z;

			Vector3 targetPivot = hit.Value.Position + normal * pushOut;
			motion = targetPivot - _pivotStart.Origin;
		}
		else
		{
			Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePos);
			Vector3 rayDirection = _camera.ProjectRayNormal(mousePos);
			Vector3 planeNormal = (_camera.GlobalPosition - _pivotStart.Origin).Normalized();
			if (planeNormal.IsZeroApprox())
			{
				planeNormal = -_camera.GlobalBasis.Z;
			}

			Plane dragPlane = new(planeNormal, _pivotStart.Origin);
			Vector3? currentIntersection = dragPlane.IntersectsRay(rayOrigin, rayDirection);
			Vector3? startIntersection = dragPlane.IntersectsRay(
				_camera.ProjectRayOrigin(_dragStartPos),
				_camera.ProjectRayNormal(_dragStartPos)
			);

			if (currentIntersection == null || startIntersection == null)
			{
				return;
			}

			motion = currentIntersection.Value - startIntersection.Value;
		}

		if (CreatorService.Interface.MoveSnapEnabled)
		{
			motion = motion.Snap(GetSafeMoveSnap());
		}

		ApplySelectDragMotion(motion);
	}

	private static float GetSafeMoveSnap()
	{
		return Mathf.Max(MinimumSnap, CreatorService.Interface.MoveSnapping);
	}

	private static Vector3 GetCombinedHalfExtents(Dynamic[] targets)
	{
		if (targets.Length == 0)
			return Vector3.Zero;

		Aabb merged = targets[0].CalculateBounds();
		for (int i = 1; i < targets.Length; i++)
		{
			merged = merged.Merge(targets[i].CalculateBounds());
		}

		return merged.Size * 0.5f;
	}

	public static Transform3D GetCenterPivot(Instance[] instances)
	{
		Vector3 center = Vector3.Zero;
		int count = 0;
		Dynamic? firstDynamic = null;
		TransformOrientationEnum orientationMode = CreatorService.Interface?.TransformOrientation ?? TransformOrientationEnum.Global;
		SelectionPivotModeEnum pivotMode = CreatorService.Interface?.SelectionPivotMode ?? SelectionPivotModeEnum.Center;

		foreach (Instance sel in instances)
		{
			if (sel is Dynamic dyn)
			{
				firstDynamic ??= dyn;
				Transform3D xform = dyn.GetGlobalTransform();
				center += xform.Origin;
				count++;
			}
		}
		if (count == 0) return Transform3D.Identity;
		center /= count;

		Vector3 origin = center;
		if (pivotMode == SelectionPivotModeEnum.PrimarySelection && firstDynamic != null)
		{
			origin = firstDynamic.GetGlobalTransform().Origin;
		}

		Basis basis = Basis.Identity;
		if (orientationMode == TransformOrientationEnum.Local && firstDynamic != null)
		{
			basis = firstDynamic.GetGlobalTransform().Basis.Orthonormalized();
		}

		return new Transform3D(basis, origin);
	}

	private Transform3D GetSelectionPivot()
	{
		if (Selected.Count == 0) return Transform3D.Identity;

		return GetCenterPivot([.. Selected]);
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using System;
using System.Collections.Generic;

namespace BrickVerse.Creator.UI;

public partial class TerrainEditor : Control
{
	private enum TerrainTool
	{
		Add,
		Remove,
		Paint,
		Smooth,
		Grow,
		Erode
	}

	private enum BrushShape
	{
		Sphere,
		Block,
		Cylinder
	}

	[Export] private Button _addButton = null!;
	[Export] private Button _removeButton = null!;
	[Export] private Button _paintButton = null!;
	[Export] private Button _smoothButton = null!;
	[Export] private Button _growButton = null!;
	[Export] private Button _erodeButton = null!;

	[Export] private OptionButton _shapeOption = null!;
	[Export] private OptionButton _materialOption = null!;
	[Export] private HSlider _sizeSlider = null!;
	[Export] private SpinBox _sizeSpinBox = null!;
	[Export] private HSlider _strengthSlider = null!;
	[Export] private SpinBox _strengthSpinBox = null!;
	[Export] private HSlider _spacingSlider = null!;
	[Export] private SpinBox _spacingSpinBox = null!;
	[Export] private CheckButton _surfaceSnapCheck = null!;
	[Export] private CheckButton _continuousCheck = null!;
	[Export] private Button _clearButton = null!;
	[Export] private Label _statusLabel = null!;

	private readonly Dictionary<Button, TerrainTool> _toolButtons = [];

	private TerrainTool _tool = TerrainTool.Add;
	private BrushShape _shape = BrushShape.Sphere;
	private MeshInstance3D? _brushPreview;
	private StandardMaterial3D? _previewMaterial;
	private bool _drawing;
	private bool _hasBrushHit;
	private Vector3 _brushPosition;
	private Vector3 _brushNormal = Vector3.Up;
	private Vector3 _lastAppliedPosition;
	private bool _hasLastAppliedPosition;
	private bool _savedAutoSerialise;

	private float BrushSize => (float)_sizeSpinBox.Value;
	private float BrushStrength => (float)_strengthSpinBox.Value;
	private float BrushSpacing => (float)_spacingSpinBox.Value;
	private int MaterialIndex => _materialOption.GetSelectedId();
	private Terrain? CurrentTerrain => CreatorService.CurrentGame?.Terrain;

	public override void _Ready()
	{
		_toolButtons[_addButton] = TerrainTool.Add;
		_toolButtons[_removeButton] = TerrainTool.Remove;
		_toolButtons[_paintButton] = TerrainTool.Paint;
		_toolButtons[_smoothButton] = TerrainTool.Smooth;
		_toolButtons[_growButton] = TerrainTool.Grow;
		_toolButtons[_erodeButton] = TerrainTool.Erode;

		foreach ((Button button, TerrainTool tool) in _toolButtons)
		{
			button.Pressed += () => SelectTool(tool);
		}

		_shapeOption.ItemSelected += OnShapeSelected;
		_materialOption.ItemSelected += _ => UpdateStatus();
		_clearButton.Pressed += OnClearPressed;

		BindRangeControls(_sizeSlider, _sizeSpinBox, OnBrushSettingsChanged);
		BindRangeControls(_strengthSlider, _strengthSpinBox, OnBrushSettingsChanged);
		BindRangeControls(_spacingSlider, _spacingSpinBox, OnBrushSettingsChanged);

		_surfaceSnapCheck.Toggled += _ => UpdateStatus();
		_continuousCheck.Toggled += _ => UpdateStatus();

		CreateBrushPreview();
		SelectTool(TerrainTool.Add);
		OnShapeSelected(_shapeOption.Selected);
		UpdateStatus();
	}

	public override void _ExitTree()
	{
		FinishStroke();

		if (_brushPreview != null && GodotObject.IsInstanceValid(_brushPreview))
		{
			_brushPreview.QueueFree();
		}

		_brushPreview = null;
		_previewMaterial = null;
	}

	public override void _Process(double delta)
	{
		UpdateBrushHit();
		UpdatePreview();

		if (_drawing && _continuousCheck.ButtonPressed && _hasBrushHit)
		{
			TryApplyBrush();
		}
	}

	public override void _UnhandledInput(InputEvent inputEvent)
	{
		if (!IsVisibleInTree() || CurrentTerrain == null)
		{
			return;
		}

		if (inputEvent is InputEventMouseButton mouseButton &&
			mouseButton.ButtonIndex == MouseButton.Left)
		{
			if (mouseButton.Pressed)
			{
				if (IsMouseOverEditorUi())
				{
					return;
				}

				StartStroke();
				TryApplyBrush(force: true);
				GetViewport().SetInputAsHandled();
			}
			else if (_drawing)
			{
				FinishStroke();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void SelectTool(TerrainTool tool)
	{
		_tool = tool;

		foreach ((Button button, TerrainTool mappedTool) in _toolButtons)
		{
			button.SetPressedNoSignal(mappedTool == tool);
		}

		bool materialEnabled = tool is TerrainTool.Add or TerrainTool.Paint;
		_materialOption.Disabled = !materialEnabled;

		bool strengthEnabled = tool is TerrainTool.Paint or TerrainTool.Smooth or TerrainTool.Grow or TerrainTool.Erode;
		_strengthSlider.Editable = strengthEnabled;
		_strengthSpinBox.Editable = strengthEnabled;

		UpdatePreviewColor();
		UpdateStatus();
	}

	private void OnShapeSelected(long index)
	{
		_shape = index switch
		{
			1 => BrushShape.Block,
			2 => BrushShape.Cylinder,
			_ => BrushShape.Sphere
		};

		RebuildPreviewMesh();
		UpdateStatus();
	}

	private void OnBrushSettingsChanged(double value)
	{
		UpdatePreviewTransform();
		UpdateStatus();
	}

	private void OnClearPressed()
	{
		Terrain? terrain = CurrentTerrain;

		if (terrain == null)
		{
			UpdateStatus("No terrain is loaded.");
			return;
		}

		terrain.Clear();
		UpdateStatus("Terrain cleared.");
	}

	private static void BindRangeControls(
		Slider slider,
		SpinBox spinBox,
		Action<double> changed)
	{
		bool syncing = false;

		slider.ValueChanged += value =>
		{
			if (syncing)
			{
				return;
			}

			syncing = true;
			spinBox.Value = value;
			syncing = false;
			changed(value);
		};

		spinBox.ValueChanged += value =>
		{
			if (syncing)
			{
				return;
			}

			syncing = true;
			slider.Value = value;
			syncing = false;
			changed(value);
		};
	}

	private void StartStroke()
	{
		if (_drawing)
		{
			return;
		}

		Terrain? terrain = CurrentTerrain;

		if (terrain == null)
		{
			return;
		}

		_drawing = true;
		_hasLastAppliedPosition = false;
		_savedAutoSerialise = terrain.AutoSerialise;
		terrain.AutoSerialise = false;
	}

	private void FinishStroke()
	{
		if (!_drawing)
		{
			return;
		}

		_drawing = false;
		_hasLastAppliedPosition = false;

		Terrain? terrain = CurrentTerrain;

		if (terrain != null)
		{
			terrain.SaveTerrain();
			terrain.AutoSerialise = _savedAutoSerialise;
		}
	}

	private void TryApplyBrush(bool force = false)
	{
		Terrain? terrain = CurrentTerrain;

		if (terrain == null || !_hasBrushHit)
		{
			return;
		}

		float minimumDistance = Math.Max(0.05f, BrushSize * BrushSpacing);

		if (!force &&
			_hasLastAppliedPosition &&
			_lastAppliedPosition.DistanceTo(_brushPosition) < minimumDistance)
		{
			return;
		}

		ApplyBrush(terrain, _brushPosition);
		_lastAppliedPosition = _brushPosition;
		_hasLastAppliedPosition = true;
	}

	private void ApplyBrush(Terrain terrain, Vector3 position)
	{
		float size = BrushSize;
		float radius = size * 0.5f;
		int material = MaterialIndex;

		switch (_tool)
		{
			case TerrainTool.Add:
				ApplyAdd(terrain, position, size, radius, material);
				break;

			case TerrainTool.Remove:
				ApplyRemove(terrain, position, size, radius);
				break;

			case TerrainTool.Paint:
				terrain.PaintBall(position, radius, material, BrushStrength);
				break;

			case TerrainTool.Smooth:
				terrain.SmoothBall(
					position,
					radius,
					Math.Max(1, Mathf.RoundToInt(BrushStrength * 6.0f)));
				break;

			case TerrainTool.Grow:
				terrain.GrowBall(position, radius, BrushStrength);
				break;

			case TerrainTool.Erode:
				terrain.ErodeBall(position, radius, BrushStrength);
				break;
		}
	}

	private void ApplyAdd(
		Terrain terrain,
		Vector3 position,
		float size,
		float radius,
		int material)
	{
		switch (_shape)
		{
			case BrushShape.Block:
				terrain.FillBlock(position, Vector3.One * size, material);
				break;

			case BrushShape.Cylinder:
				terrain.FillCylinder(position, size, radius, material);
				break;

			default:
				terrain.FillBall(position, radius, material);
				break;
		}
	}

	private void ApplyRemove(
		Terrain terrain,
		Vector3 position,
		float size,
		float radius)
	{
		switch (_shape)
		{
			case BrushShape.Block:
				terrain.DigBlock(position, Vector3.One * size);
				break;

			case BrushShape.Cylinder:
				terrain.DigCylinder(position, size, radius);
				break;

			default:
				terrain.DigBall(position, radius);
				break;
		}
	}

	private void UpdateBrushHit()
	{
		Camera3D? camera = GetViewport().GetCamera3D();

		if (camera == null)
		{
			_hasBrushHit = false;
			return;
		}

		Vector2 mousePosition = GetViewport().GetMousePosition();
		Vector3 rayOrigin = camera.ProjectRayOrigin(mousePosition);
		Vector3 rayDirection = camera.ProjectRayNormal(mousePosition).Normalized();
		Vector3 rayEnd = rayOrigin + rayDirection * 10000.0f;

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
			rayOrigin,
			rayEnd);
		query.CollideWithAreas = true;
		query.CollideWithBodies = true;

		Godot.Collections.Dictionary result =
			camera.GetWorld3D().DirectSpaceState.IntersectRay(query);

		if (result.Count > 0)
		{
			Vector3 position = (Vector3)result["position"];
			Vector3 normal = (Vector3)result["normal"];

			_brushNormal = normal.IsZeroApprox() ? Vector3.Up : normal.Normalized();
			_brushPosition = _surfaceSnapCheck.ButtonPressed
				? position + _brushNormal * (BrushSize * 0.25f)
				: position;
			_hasBrushHit = true;
			return;
		}

		// Keep the editor usable before terrain exists by falling back to the
		// horizontal world plane.
		if (Mathf.Abs(rayDirection.Y) > 0.0001f)
		{
			float distance = -rayOrigin.Y / rayDirection.Y;

			if (distance >= 0.0f)
			{
				_brushPosition = rayOrigin + rayDirection * distance;
				_brushNormal = Vector3.Up;
				_hasBrushHit = true;
				return;
			}
		}

		_hasBrushHit = false;
	}

	private void CreateBrushPreview()
	{
		_brushPreview = new MeshInstance3D
		{
			Name = "TerrainBrushPreview",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		};

		_previewMaterial = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			NoDepthTest = true,
			AlbedoColor = new Color(0.25f, 0.7f, 1.0f, 0.22f)
		};

		GetTree().Root.AddChild(_brushPreview);
		RebuildPreviewMesh();
	}

	private void RebuildPreviewMesh()
	{
		if (_brushPreview == null)
		{
			return;
		}

		PrimitiveMesh mesh = _shape switch
		{
			BrushShape.Block => new BoxMesh(),
			BrushShape.Cylinder => new CylinderMesh
			{
				TopRadius = 0.5f,
				BottomRadius = 0.5f,
				Height = 1.0f,
				RadialSegments = 32
			},
			_ => new SphereMesh
			{
				Radius = 0.5f,
				Height = 1.0f,
				RadialSegments = 32,
				Rings = 16
			}
		};

		mesh.Material = _previewMaterial;
		_brushPreview.Mesh = mesh;
		UpdatePreviewTransform();
	}

	private void UpdatePreview()
	{
		if (_brushPreview == null)
		{
			return;
		}

		_brushPreview.Visible = IsVisibleInTree() && _hasBrushHit;

		if (!_brushPreview.Visible)
		{
			return;
		}

		_brushPreview.GlobalPosition = _brushPosition;
		UpdatePreviewTransform();
	}

	private void UpdatePreviewTransform()
	{
		if (_brushPreview == null)
		{
			return;
		}

		_brushPreview.Scale = Vector3.One * BrushSize;
	}

	private void UpdatePreviewColor()
	{
		if (_previewMaterial == null)
		{
			return;
		}

		_previewMaterial.AlbedoColor = _tool switch
		{
			TerrainTool.Remove => new Color(1.0f, 0.25f, 0.25f, 0.25f),
			TerrainTool.Paint => new Color(1.0f, 0.72f, 0.2f, 0.25f),
			TerrainTool.Smooth => new Color(0.7f, 0.5f, 1.0f, 0.25f),
			TerrainTool.Grow => new Color(0.3f, 0.9f, 0.5f, 0.25f),
			TerrainTool.Erode => new Color(1.0f, 0.45f, 0.35f, 0.25f),
			_ => new Color(0.25f, 0.7f, 1.0f, 0.25f)
		};
	}

	private bool IsMouseOverEditorUi()
	{
		Control? hoveredControl = GetViewport().GuiGetHoveredControl();
		return hoveredControl != null &&
			(hoveredControl == this || IsAncestorOf(hoveredControl));
	}

	private void UpdateStatus(string? message = null)
	{
		if (!string.IsNullOrWhiteSpace(message))
		{
			_statusLabel.Text = message;
			return;
		}

		string materialText = _materialOption.Disabled
			? string.Empty
			: $" · Material {_materialOption.GetItemText(_materialOption.Selected)}";

		_statusLabel.Text =
			$"{_tool} · {_shape} · Size {BrushSize:0.#}{materialText}";
	}
}

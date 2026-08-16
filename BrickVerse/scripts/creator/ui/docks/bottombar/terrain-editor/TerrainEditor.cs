// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using BrickVerse.Creator.UI.Popups;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BrickVerse.Creator.UI;

public partial class TerrainEditor : Control
{
	private enum TerrainTool
	{
		Add,
		Remove,
		Paint,
		Smooth,
		Flatten,
		Grow,
		Erode,
		Grass
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
	[Export] private Button _flattenButton = null!;
	[Export] private Button _growButton = null!;
	[Export] private Button _erodeButton = null!;
	[Export] private Button _importButton = null!;
	[Export] private Button _generateButton = null!;

	[Export] private OptionButton _shapeOption = null!;
	[Export] private OptionButton _materialOption = null!;
	[Export] private HSlider _sizeSlider = null!;
	[Export] private SpinBox _sizeSpinBox = null!;
	[Export] private HSlider _strengthSlider = null!;
	[Export] private SpinBox _strengthSpinBox = null!;
	[Export] private HSlider _spacingSlider = null!;
	[Export] private SpinBox _spacingSpinBox = null!;
	[Export] private HSlider _intervalSlider = null!;
	[Export] private SpinBox _intervalSpinBox = null!;
	[Export] private HSlider _spacingFloorSlider = null!;
	[Export] private SpinBox _spacingFloorSpinBox = null!;
	[Export] private HSlider _snapOffsetSlider = null!;
	[Export] private SpinBox _snapOffsetSpinBox = null!;
	[Export] private CheckButton _enabledCheck = null!;
	[Export] private CheckButton _surfaceSnapCheck = null!;
	[Export] private CheckButton _continuousCheck = null!;
	[Export] private Button _clearButton = null!;
	[Export] private Label _statusLabel = null!;
	[Export] private bool _enabledByDefault = true;
	[Export(PropertyHint.Range, "0,2,1")] private int _defaultShapeId;
	[Export(PropertyHint.Range, "0,255,1")] private int _defaultMaterialId = 1;

	private readonly Dictionary<Button, TerrainTool> _toolButtons = [];
	private Button? _grassButton;
	private VBoxContainer? _grassSettings;

	private TerrainTool _tool = TerrainTool.Add;
	private BrushShape _shape = BrushShape.Sphere;
	private MeshInstance3D? _brushPreview;
	private MeshInstance3D? _brushPreviewOutline;
	private StandardMaterial3D? _previewMaterial;
	private StandardMaterial3D? _previewOutlineMaterial;
	private bool _drawing;
	private bool _hasBrushHit;
	private Vector3 _brushPosition;
	private Vector3 _brushNormal = Vector3.Up;
	private Vector3 _lastAppliedPosition;
	private bool _hasLastAppliedPosition;
	private bool _savedAutoSerialise;
	private Control? _viewportClickRegion;
	private double _lastApplyAtMs;
	private string _materialSignature = string.Empty;

	private float BrushSize => (float)_sizeSpinBox.Value;
	private float BrushStrength => (float)_strengthSpinBox.Value;
	private float BrushSpacing => (float)_spacingSpinBox.Value;
	private float BrushIntervalMs => (float)_intervalSpinBox.Value;
	private float BrushSpacingFloor => (float)_spacingFloorSpinBox.Value;
	private float BrushSnapOffset => (float)_snapOffsetSpinBox.Value;
	private int MaterialIndex =>
		_materialOption.Selected >= 0
			? _materialOption.GetItemId(_materialOption.Selected)
			: 0;
	private Terrain? CurrentTerrain => CreatorService.CurrentGame?.Terrain;

	public override void _Ready()
	{
		_toolButtons[_addButton] = TerrainTool.Add;
		_toolButtons[_removeButton] = TerrainTool.Remove;
		_toolButtons[_paintButton] = TerrainTool.Paint;
		_toolButtons[_smoothButton] = TerrainTool.Smooth;
		_toolButtons[_flattenButton] = TerrainTool.Flatten;
		_toolButtons[_growButton] = TerrainTool.Grow;
		_toolButtons[_erodeButton] = TerrainTool.Erode;
		foreach ((Button button, TerrainTool tool) in _toolButtons)
		{
			button.Pressed += () => SelectTool(tool);
		}
		CreatorBetaFeatures.FeatureChanged += OnBetaFeatureChanged;
		SetGrassBetaEnabled(CreatorBetaFeatures.IsEnabled(CreatorBetaFeatures.SkinnedGrass));

		_shapeOption.ItemSelected += OnShapeSelected;
		_materialOption.ItemSelected += _ => UpdateStatus();
		_enabledCheck.Toggled += OnEnabledToggled;
		_clearButton.Pressed += OnClearPressed;
		_importButton.Pressed += OpenHeightmapDialog;
		_generateButton.Pressed += OpenGenerateDialog;

		BindRangeControls(_sizeSlider, _sizeSpinBox, OnBrushSettingsChanged);
		BindRangeControls(_strengthSlider, _strengthSpinBox, OnBrushSettingsChanged);
		BindRangeControls(_spacingSlider, _spacingSpinBox, OnBrushSettingsChanged);
		BindRangeControls(_intervalSlider, _intervalSpinBox, OnBrushSettingsChanged);
		BindRangeControls(_spacingFloorSlider, _spacingFloorSpinBox, OnBrushSettingsChanged);
		BindRangeControls(_snapOffsetSlider, _snapOffsetSpinBox, OnBrushSettingsChanged);

		_surfaceSnapCheck.Toggled += _ => UpdateStatus();
		_continuousCheck.Toggled += _ => UpdateStatus();

		_viewportClickRegion = GetNodeOrNull<Control>("../../../Tabs/Container");

		PopulateMaterialOptions();
		CreateBrushPreview();
		PopulateMaterialIcons();
		SelectTool(TerrainTool.Add);
		ApplyEditorDefaults();
		UpdateStatus();
	}

	public override void _ExitTree()
	{
		CreatorBetaFeatures.FeatureChanged -= OnBetaFeatureChanged;
		FinishStroke();

		if (_brushPreview != null && GodotObject.IsInstanceValid(_brushPreview))
		{
			_brushPreview.QueueFree();
		}

		_brushPreview = null;
		_brushPreviewOutline = null;
		_previewMaterial = null;
		_previewOutlineMaterial = null;
	}

	private void OnBetaFeatureChanged(string flag, bool enabled)
	{
		if (flag == CreatorBetaFeatures.SkinnedGrass) SetGrassBetaEnabled(enabled);
	}

	private void SetGrassBetaEnabled(bool enabled)
	{
		if (enabled && _grassButton == null)
		{
			_grassButton = new Button { Text = "Grass", ToggleMode = true, TooltipText = "Paint skinned grass; hold Shift to erase" };
			_grassButton.Pressed += () => SelectTool(TerrainTool.Grass);
			_erodeButton.GetParent().AddChild(_grassButton);
			_toolButtons[_grassButton] = TerrainTool.Grass;
			CreateGrassSettingsPanel();
		}
		else if (!enabled && _grassButton != null)
		{
			if (_tool == TerrainTool.Grass) SelectTool(TerrainTool.Add);
			_toolButtons.Remove(_grassButton);
			_grassButton.QueueFree();
			_grassButton = null;
			_grassSettings?.QueueFree();
			_grassSettings = null;
		}
	}

	private void CreateGrassSettingsPanel()
	{
		if (_grassSettings != null) return;
		HBoxContainer top = GetNode<HBoxContainer>("Layout/Scroll/Top");
		_grassSettings = new VBoxContainer { Name = "GrassSettings", CustomMinimumSize = new Vector2(520, 0), Visible = _tool == TerrainTool.Grass };
		top.AddChild(_grassSettings);
		_grassSettings.AddChild(new Label { Text = "SKINNED GRASS (BETA)", Modulate = new Color("a1a8b8") });
		PanelContainer card = new(); _grassSettings.AddChild(card);
		MarginContainer margin = new(); margin.AddThemeConstantOverride("margin_left", 10); margin.AddThemeConstantOverride("margin_top", 8); margin.AddThemeConstantOverride("margin_right", 10); margin.AddThemeConstantOverride("margin_bottom", 8); card.AddChild(margin);
		HBoxContainer columns = new(); columns.AddThemeConstantOverride("separation", 16); margin.AddChild(columns);
		VBoxContainer global = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; global.AddThemeConstantOverride("separation", 5); columns.AddChild(global);
		VBoxContainer local = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; local.AddThemeConstantOverride("separation", 5); columns.AddChild(local);
		TerrainGrass? existing = GetGrassLayer(false);
		global.AddChild(new Label { Text = "Global layer", Modulate = new Color("72c96b") });
		AddGrassNumber(global, "Density", 0.05, 8, 0.05, existing?.Density ?? 1.2, (grass, value) => grass.Density = value);
		AddGrassNumber(global, "Height", 0.05, 12, 0.05, existing?.BladeHeight ?? 1.4, (grass, value) => grass.BladeHeight = value);
		AddGrassNumber(global, "Width", 0.01, 3, 0.01, existing?.BladeWidth ?? 0.13, (grass, value) => grass.BladeWidth = value);
		AddGrassNumber(global, "Surface inset", -2, 2, 0.01, existing?.SurfaceOffset ?? -0.1, (grass, value) => grass.SurfaceOffset = value);
		AddGrassNumber(global, "Wind strength", 0, 10, 0.05, CurrentTerrain?.Root.Environment.WindStrength ?? 0.28, (grass, value) => grass.Root.Environment.WindStrength = value);
		AddGrassNumber(global, "Wind speed", 0, 20, 0.1, CurrentTerrain?.Root.Environment.WindSpeed ?? 1.5, (grass, value) => grass.Root.Environment.WindSpeed = value);
		AddGrassColor(global, "Base color", existing?.BaseColor ?? new Color("327a32"), (grass, color) => grass.BaseColor = color);
		AddGrassColor(global, "Tip color", existing?.TipColor ?? new Color("83c95b"), (grass, color) => grass.TipColor = color);
		CheckButton conform = new() { Text = "Conform blades to surface", ButtonPressed = existing?.DeformToSurface ?? true }; conform.Toggled += value => GetGrassLayer(true)!.DeformToSurface = value; global.AddChild(conform);
		local.AddChild(new Label { Text = "Paint-at-location", Modulate = new Color("6ac2ff") });
		AddGrassNumber(local, "Density", 0.05, 8, 0.05, existing?.PaintDensityScale ?? 1, (grass, value) => grass.PaintDensityScale = value);
		AddGrassNumber(local, "Height", 0.05, 8, 0.05, existing?.PaintHeightScale ?? 1, (grass, value) => grass.PaintHeightScale = value);
		AddGrassNumber(local, "Width", 0.05, 8, 0.05, existing?.PaintWidthScale ?? 1, (grass, value) => grass.PaintWidthScale = value);
		AddGrassColor(local, "Tint", existing?.PaintColor ?? Colors.White, (grass, color) => grass.PaintColor = color);
		local.AddChild(new Label { Text = "Local values are stored on newly painted blades. Hold Shift while painting to erase.", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color("9aa8ba") });
	}

	private void AddGrassNumber(VBoxContainer parent, string label, double min, double max, double step, double initial, Action<TerrainGrass, float> apply)
	{
		HBoxContainer row = new(); row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(105, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		SpinBox input = new() { MinValue = min, MaxValue = max, Step = step, Value = initial, CustomMinimumSize = new Vector2(90, 0) };
		input.ValueChanged += value => apply(GetGrassLayer(true)!, (float)value); row.AddChild(input); parent.AddChild(row);
	}

	private void AddGrassColor(VBoxContainer parent, string label, Color initial, Action<TerrainGrass, Color> apply)
	{
		HBoxContainer row = new(); row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(105, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		ColorPickerButton input = new() { Color = initial, CustomMinimumSize = new Vector2(90, 28) };
		input.ColorChanged += color => apply(GetGrassLayer(true)!, color); row.AddChild(input); parent.AddChild(row);
	}

	private TerrainGrass? GetGrassLayer(bool create)
	{
		Terrain? terrain = CurrentTerrain; if (terrain == null) return null;
		TerrainGrass? grass = terrain.GetChildrenOfClass<TerrainGrass>().FirstOrDefault();
		if (grass == null && create) grass = terrain.New<TerrainGrass>(terrain);
		return grass;
	}

	public override void _Process(double delta)
	{
		RefreshMaterialOptionsIfNeeded();
		if (!IsVisibleInTree() || !IsEditingEnabled() || CurrentTerrain == null)
		{
			FinishStroke();
			_hasBrushHit = false;
			UpdatePreview();
			return;
		}

		if (_drawing && !Input.IsMouseButtonPressed(MouseButton.Left))
		{
			FinishStroke();
		}

		EnsurePreviewParent();
		UpdateBrushHit();
		UpdatePreview();

		if (_drawing && _continuousCheck.ButtonPressed && _hasBrushHit)
		{
			TryApplyBrush();
		}
	}

	public override void _Input(InputEvent inputEvent)
	{
		if (!IsVisibleInTree() || !IsEditingEnabled() || CurrentTerrain == null)
		{
			return;
		}

		if (inputEvent is InputEventMouseButton mouseButton &&
			mouseButton.ButtonIndex == MouseButton.Left)
		{
			if (mouseButton.Pressed)
			{
				if (IsMouseBlockedByUi())
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

		bool materialEnabled = tool is TerrainTool.Add or TerrainTool.Paint or TerrainTool.Flatten;
		_materialOption.Disabled = !materialEnabled;

		bool strengthEnabled = tool is TerrainTool.Paint or TerrainTool.Smooth or TerrainTool.Flatten or TerrainTool.Grow or TerrainTool.Erode or TerrainTool.Grass;
		_strengthSlider.Editable = strengthEnabled;
		_strengthSpinBox.Editable = strengthEnabled;

		UpdatePreviewColor();
		if (_grassSettings != null) _grassSettings.Visible = tool == TerrainTool.Grass;
		UpdateStatus();
	}

	private void OnEnabledToggled(bool enabled)
	{
		if (!enabled)
		{
			FinishStroke();
			_hasBrushHit = false;
		}

		UpdatePreview();
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

		FinishStroke();
		if (_tool == TerrainTool.Grass)
		{
			terrain.GetChildrenOfClass<TerrainGrass>().FirstOrDefault()?.Clear();
			UpdateStatus("Grass coverage cleared.");
			return;
		}
		terrain.Clear();

		Camera3D? camera =
			CreatorService.CurrentGame?.Environment?.CurrentGDCamera ??
			GetViewport().GetCamera3D();

		if (camera != null)
		{
			terrain.SetEditorViewerPosition(camera.GlobalPosition);
		}

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
		_lastApplyAtMs = 0.0;
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

		float minimumDistance = Math.Max(BrushSpacingFloor, BrushSize * Math.Max(BrushSpacing, 0.2f));
		double nowMs = Time.GetTicksMsec();

		if (!force &&
			_lastApplyAtMs > 0.0 &&
			nowMs - _lastApplyAtMs < BrushIntervalMs)
		{
			return;
		}

		if (!force &&
			_hasLastAppliedPosition &&
			_lastAppliedPosition.DistanceTo(_brushPosition) < minimumDistance)
		{
			return;
		}

		float halfSize = BrushSize * 0.5f;
		Vector3 padding = Vector3.One * (halfSize + 2.0f);

		if (!terrain.IsAreaEditable(
			_brushPosition - padding,
			_brushPosition + padding))
		{
			UpdateStatus("Loading terrain around the brush…");
			return;
		}

		ApplyBrush(terrain, _brushPosition);
		_lastAppliedPosition = _brushPosition;
		_hasLastAppliedPosition = true;
		_lastApplyAtMs = nowMs;
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

			case TerrainTool.Flatten:
				ApplyFlatten(terrain, position, size, radius, material);
				break;

			case TerrainTool.Grow:
				terrain.GrowBall(position, radius, BrushStrength);
				break;

			case TerrainTool.Erode:
				terrain.ErodeBall(position, radius, BrushStrength);
				break;

			case TerrainTool.Grass:
				TerrainGrass? grass = terrain.GetChildrenOfClass<TerrainGrass>().FirstOrDefault();
				if (grass == null)
				{
					grass = terrain.New<TerrainGrass>(terrain);
					CreatorService.CurrentGame?.CreatorContext.Selections.SelectOnly(grass);
				}
				if (Input.IsKeyPressed(Key.Shift)) grass.Erase(position, radius);
				else grass.Paint(position, _brushNormal, radius, BrushStrength);
				break;
		}
	}

	private void ApplyFlatten(
		Terrain terrain,
		Vector3 position,
		float size,
		float radius,
		int material)
	{
		float strength = Mathf.Clamp(BrushStrength, 0.05f, 1.0f);
		float verticalRange = Math.Max(2.0f, size * strength);
		Vector3 footprint = _shape == BrushShape.Block
			? new Vector3(size, verticalRange, size)
			: new Vector3(radius * 1.7f, verticalRange, radius * 1.7f);

		terrain.FillBlock(
			new Vector3(position.X, position.Y - verticalRange * 0.5f, position.Z),
			footprint,
			material);
		terrain.DigBlock(
			new Vector3(position.X, position.Y + verticalRange * 0.5f, position.Z),
			footprint);
	}

	private void OpenHeightmapDialog()
	{
		FileDialog dialog = new()
		{
			Title = "Import Terrain Heightmap",
			Access = FileDialog.AccessEnum.Filesystem,
			FileMode = FileDialog.FileModeEnum.OpenFile,
			UseNativeDialog = true,
			Filters =
			[
				"*.png, *.jpg, *.jpeg, *.webp, *.exr ; Heightmap Images",
				"*.r16, *.raw ; 16-bit RAW Heightmaps",
			],
		};
		dialog.FileSelected += path =>
		{
			try
			{
				ImportHeightmap(path);
			}
			catch (Exception error)
			{
				UpdateStatus("Heightmap import failed: " + error.Message);
				BV.PrintErr("Terrain heightmap import failed: ", error);
			}
			dialog.QueueFree();
		};
		dialog.Canceled += dialog.QueueFree;
		AddChild(dialog);
		dialog.PopupCenteredRatio(0.72f);
	}

	private void ImportHeightmap(string path)
	{
		Image image;
		string extension = Path.GetExtension(path).ToLowerInvariant();
		if (extension is ".r16" or ".raw")
		{
			byte[] bytes = System.IO.File.ReadAllBytes(path);
			int side = Mathf.RoundToInt(Mathf.Sqrt(bytes.Length / 2.0f));
			if (side * side * 2 != bytes.Length)
				throw new InvalidDataException("RAW heightmaps must be square, unsigned 16-bit little-endian data.");
			image = Image.CreateEmpty(side, side, false, Image.Format.Rf);
			for (int y = 0; y < side; y++)
				for (int x = 0; x < side; x++)
				{
					int offset = (y * side + x) * 2;
					ushort value = (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
					image.SetPixel(x, y, new Color(value / 65535.0f, 0, 0));
				}
		}
		else
		{
			image = Image.LoadFromFile(path);
			if (image.IsEmpty()) throw new InvalidDataException("The selected image could not be decoded.");
		}

		int largestSide = Math.Max(image.GetWidth(), image.GetHeight());
		if (largestSide > 128)
		{
			float scale = 128.0f / largestSide;
			image.Resize(
				Math.Max(2, Mathf.RoundToInt(image.GetWidth() * scale)),
				Math.Max(2, Mathf.RoundToInt(image.GetHeight() * scale)),
				Image.Interpolation.Lanczos);
		}
		BuildTerrainFromHeightSamples(
			image.GetWidth(),
			image.GetHeight(),
			(x, y) =>
			{
				Color pixel = image.GetPixel(x, y);
				return pixel.R * 0.2126f + pixel.G * 0.7152f + pixel.B * 0.0722f;
			},
			2.0f,
			128.0f,
			MaterialIndex,
			replaceExisting: true);
		UpdateStatus($"Imported {Path.GetFileName(path)} ({image.GetWidth()}×{image.GetHeight()}).");
	}

	private void OpenGenerateDialog()
	{
		ConfirmationDialog dialog = new()
		{
			Title = "Generate Terrain",
			OkButtonText = "Generate",
			MinSize = new Vector2I(430, 390),
		};
		VBoxContainer content = new();
		content.AddThemeConstantOverride("separation", 10);
		dialog.AddChild(content);

		OptionButton preset = AddDialogOption(content, "Terrain type", ["Rolling Hills", "Mountains", "Islands", "Canyons"]);
		SpinBox size = AddDialogSpin(content, "World size", 64, 512, 16, 256);
		SpinBox height = AddDialogSpin(content, "Maximum height", 8, 256, 4, 96);
		SpinBox detail = AddDialogSpin(content, "Feature size", 8, 128, 2, 48);
		SpinBox seed = AddDialogSpin(content, "Seed", 0, int.MaxValue, 1, Random.Shared.Next(1, int.MaxValue));
		CheckButton replace = new() { Text = "Replace existing terrain", ButtonPressed = true };
		content.AddChild(replace);
		Label hint = new()
		{
			Text = "Generation uses the selected terrain material and is saved with the world.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		content.AddChild(hint);

		dialog.Confirmed += () =>
		{
			GenerateTerrain(
				preset.Selected,
				(int)size.Value,
				(float)height.Value,
				(float)detail.Value,
				(int)seed.Value,
				replace.ButtonPressed);
			dialog.QueueFree();
		};
		dialog.Canceled += dialog.QueueFree;
		AddChild(dialog);
		dialog.PopupCentered();
	}

	private static OptionButton AddDialogOption(
		VBoxContainer parent,
		string label,
		string[] items)
	{
		Label title = new() { Text = label };
		parent.AddChild(title);
		OptionButton option = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		foreach (string item in items) option.AddItem(item);
		parent.AddChild(option);
		return option;
	}

	private static SpinBox AddDialogSpin(
		VBoxContainer parent,
		string label,
		double minimum,
		double maximum,
		double step,
		double value)
	{
		HBoxContainer row = new();
		row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(150, 0) });
		SpinBox spin = new()
		{
			MinValue = minimum,
			MaxValue = maximum,
			Step = step,
			Value = value,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		row.AddChild(spin);
		parent.AddChild(row);
		return spin;
	}

	private void GenerateTerrain(
		int preset,
		int worldSize,
		float maximumHeight,
		float featureSize,
		int seed,
		bool replaceExisting)
	{
		FastNoiseLite primary = new()
		{
			Seed = seed,
			Frequency = 1.0f / featureSize,
			FractalOctaves = preset == 1 ? 6 : 4,
			FractalGain = preset == 1 ? 0.58f : 0.5f,
		};
		FastNoiseLite detail = new()
		{
			Seed = seed ^ 0x5f3759df,
			Frequency = 2.5f / featureSize,
			FractalOctaves = 3,
		};
		int samples = Math.Clamp(worldSize / 4, 16, 128);
		BuildTerrainFromHeightSamples(
			samples,
			samples,
			(x, y) =>
			{
				float nx = (x / (samples - 1.0f)) * 2.0f - 1.0f;
				float ny = (y / (samples - 1.0f)) * 2.0f - 1.0f;
				float noise = (primary.GetNoise2D(x, y) + 1.0f) * 0.5f;
				float fine = detail.GetNoise2D(x, y) * 0.12f;
				return preset switch
				{
					1 => Mathf.Pow(Mathf.Clamp(noise + fine, 0, 1), 1.8f),
					2 => Mathf.Clamp((noise + fine) * 1.35f - Mathf.Sqrt(nx * nx + ny * ny) * 0.7f, 0, 1),
					3 => Mathf.Clamp(0.72f - Mathf.Abs(primary.GetNoise2D(x, y)) * 0.85f + fine, 0.05f, 1),
					_ => Mathf.Clamp(noise * 0.7f + fine + 0.12f, 0, 1),
				};
			},
			worldSize / (float)samples,
			maximumHeight,
			MaterialIndex,
			replaceExisting);
		UpdateStatus($"Generated {worldSize}×{worldSize} terrain with seed {seed}.");
	}

	private void BuildTerrainFromHeightSamples(
		int width,
		int height,
		Func<int, int, float> sample,
		float cellSize,
		float maximumHeight,
		int material,
		bool replaceExisting)
	{
		Terrain? terrain = CurrentTerrain;
		if (terrain == null) throw new InvalidOperationException("No terrain is loaded.");
		FinishStroke();
		bool oldAutoSerialise = terrain.AutoSerialise;
		terrain.AutoSerialise = false;
		try
		{
			if (replaceExisting) terrain.Clear();
			float originX = -width * cellSize * 0.5f;
			float originZ = -height * cellSize * 0.5f;
			const float baseDepth = 16.0f;
			for (int y = 0; y < height; y++)
				for (int x = 0; x < width; x++)
				{
					float columnHeight = Math.Max(1.0f, Mathf.Clamp(sample(x, y), 0, 1) * maximumHeight);
					terrain.FillBlock(
						new Vector3(
							originX + (x + 0.5f) * cellSize,
							(columnHeight - baseDepth) * 0.5f,
							originZ + (y + 0.5f) * cellSize),
						new Vector3(cellSize + 0.1f, columnHeight + baseDepth, cellSize + 0.1f),
						material);
				}
			terrain.SaveTerrain();
		}
		finally
		{
			terrain.AutoSerialise = oldAutoSerialise;
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
		Camera3D? camera =
			CreatorService.CurrentGame?.Environment?.CurrentGDCamera ??
			GetViewport().GetCamera3D();

		Terrain? terrain = CurrentTerrain;

		if (camera != null && terrain != null)
		{
			terrain.SetEditorViewerPosition(camera.GlobalPosition);
		}

		if (camera == null)
		{
			_hasBrushHit = false;
			return;
		}

		Vector2 mousePosition = camera.GetViewport().GetMousePosition();
		Vector3 rayOrigin = camera.ProjectRayOrigin(mousePosition);
		Vector3 rayDirection = camera.ProjectRayNormal(mousePosition).Normalized();
		Vector3 rayEnd = rayOrigin + rayDirection * 10000.0f;

		if (terrain != null &&
			terrain.TryRaycast(
				rayOrigin,
				rayDirection,
				10000.0f,
				out Vector3 terrainHitPosition,
				out Vector3 terrainHitNormal))
		{
			_brushNormal = terrainHitNormal.IsZeroApprox()
				? Vector3.Up
				: terrainHitNormal.Normalized();

			_brushPosition = _surfaceSnapCheck.ButtonPressed
				? terrainHitPosition + _brushNormal * GetSurfaceSnapOffset()
				: terrainHitPosition;

			_hasBrushHit = true;
			return;
		}

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
				? position + _brushNormal * GetSurfaceSnapOffset()
				: position;
			_hasBrushHit = true;
			return;
		}

		// Empty worlds need a stable plane for the first additive stroke.
		if (_tool == TerrainTool.Add &&
			Mathf.Abs(rayDirection.Y) > 0.0001f)
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

	private void ApplyEditorDefaults()
	{
		_enabledCheck.SetPressedNoSignal(_enabledByDefault);

		int shapeIndex = FindOptionIndexById(_shapeOption, _defaultShapeId);
		_shapeOption.Select(shapeIndex);
		OnShapeSelected(_shapeOption.GetItemId(shapeIndex));

		int materialIndex = FindOptionIndexById(_materialOption, _defaultMaterialId);
		if (_materialOption.ItemCount > 0)
			_materialOption.Select(materialIndex);
	}

	private void PopulateMaterialOptions()
	{
		int selectedSlot = MaterialIndex;
		_materialOption.Clear();
		TerrainMaterial[] materials = CurrentTerrain?.GetMaterials() ?? [];
		foreach (TerrainMaterial material in materials)
		{
			_materialOption.AddItem(material.Name, material.Slot);
		}
		int selectedIndex = FindOptionIndexById(_materialOption, selectedSlot);
		if (_materialOption.ItemCount > 0)
			_materialOption.Select(selectedIndex);
	}

	private void PopulateMaterialIcons()
	{
		PopupMenu popup = _materialOption.GetPopup();
		_materialOption.AddThemeConstantOverride("icon_max_width", 12);
		popup.AddThemeConstantOverride("icon_max_width", 12);

		for (int index = 0; index < _materialOption.ItemCount; index++)
		{
			int slot = _materialOption.GetItemId(index);
			TerrainMaterial? terrainMaterial = CurrentTerrain?.GetMaterial(slot);
			Texture2D icon = LoadMaterialIcon(terrainMaterial);
			_materialOption.SetItemIcon(index, icon);
			popup.SetItemIcon(index, icon);
			popup.SetItemIconMaxWidth(index, 12);
		}
	}

	private void RefreshMaterialOptionsIfNeeded()
	{
		string signature = string.Join(
			"|",
			CurrentTerrain?.GetMaterials().Select(
				material => $"{material.ObjectID}:{material.Slot}:{material.Name}:{material.Surface}:{material.Color}"
			) ?? []
		);
		if (_materialSignature == signature)
			return;
		_materialSignature = signature;
		PopulateMaterialOptions();
		PopulateMaterialIcons();
		UpdateStatus();
	}

	private static Texture2D LoadMaterialIcon(TerrainMaterial? terrainMaterial)
	{
		if (terrainMaterial != null)
		{
			if (terrainMaterial.SurfaceType == TerrainSurfaceType.Custom &&
				terrainMaterial.GetTexture(terrainMaterial.AlbedoTexture)
					is Texture2D customAlbedo)
			{
				return FitIconTexture(customAlbedo, 12);
			}

			Material material = Globals.LoadMaterial(terrainMaterial.Surface, 1.0f);

			if (material is ShaderMaterial shaderMaterial)
			{
				Variant albedoVariant = shaderMaterial.GetShaderParameter("albedo");

				if (albedoVariant.VariantType == Variant.Type.Object &&
					albedoVariant.AsGodotObject() is Texture2D albedoTexture)
				{
					return FitIconTexture(albedoTexture, 12);
				}
			}
		}

		Image image = Image.CreateEmpty(12, 12, false, Image.Format.Rgba8);
		image.Fill(terrainMaterial?.Color ?? Colors.Gray);
		return ImageTexture.CreateFromImage(image);
	}

	private static Texture2D FitIconTexture(Texture2D source, int size)
	{
		Image image = source.GetImage();

		if (image.IsEmpty())
		{
			return source;
		}
		if (image.IsCompressed() && image.Decompress() != Error.Ok)
		{
			return source;
		}

		image.Resize(size, size, Image.Interpolation.Lanczos);
		return ImageTexture.CreateFromImage(image);
	}

	private float GetSurfaceSnapOffset()
	{
		return BrushSnapOffset;
	}

	private static int FindOptionIndexById(OptionButton option, int itemId)
	{
		for (int index = 0; index < option.ItemCount; index++)
		{
			if (option.GetItemId(index) == itemId)
			{
				return index;
			}
		}

		return 0;
	}

	private void CreateBrushPreview()
	{
		_brushPreview = new MeshInstance3D
		{
			Name = "TerrainBrushPreview",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		};

		_brushPreviewOutline = new MeshInstance3D
		{
			Name = "TerrainBrushPreviewOutline",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			TopLevel = false
		};
		_brushPreview.AddChild(_brushPreviewOutline);

		_previewMaterial = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			NoDepthTest = false,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = new Color(0.25f, 0.7f, 1.0f, 0.32f)
		};

		_previewOutlineMaterial = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			NoDepthTest = true,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = new Color(0.95f, 0.98f, 1.0f, 0.9f)
		};
		_previewOutlineMaterial.Set("emission_enabled", true);
		_previewOutlineMaterial.Set("emission", new Color(0.95f, 0.98f, 1.0f, 1.0f));

		EnsurePreviewParent();
		RebuildPreviewMesh();
	}

	private void EnsurePreviewParent()
	{
		if (_brushPreview == null)
		{
			return;
		}

		Node3D? targetParent = CurrentTerrain?.VoxelTerrainNode;

		if (targetParent == null || !GodotObject.IsInstanceValid(targetParent))
		{
			return;
		}

		if (_brushPreview.GetParent() == targetParent)
		{
			return;
		}

		if (_brushPreview.GetParent() != null)
		{
			_brushPreview.Reparent(targetParent);
			return;
		}

		targetParent.AddChild(_brushPreview, false, Node.InternalMode.Front);
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

		if (_brushPreviewOutline != null)
		{
			PrimitiveMesh outlineMesh = _shape switch
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

			outlineMesh.Material = _previewOutlineMaterial;
			_brushPreviewOutline.Mesh = outlineMesh;
		}

		UpdatePreviewTransform();
	}

	private void UpdatePreview()
	{
		if (_brushPreview == null)
		{
			return;
		}

		_brushPreview.Visible =
			IsVisibleInTree() &&
			IsEditingEnabled() &&
			_hasBrushHit &&
			!IsMouseBlockedByUi();

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

		if (_brushPreviewOutline != null)
		{
			_brushPreviewOutline.Scale = Vector3.One * 1.035f;
		}
	}

	private void UpdatePreviewColor()
	{
		if (_previewMaterial == null)
		{
			return;
		}

		_previewMaterial.AlbedoColor = _tool switch
		{
			TerrainTool.Remove => new Color(1.0f, 0.25f, 0.25f, 0.32f),
			TerrainTool.Paint => new Color(1.0f, 0.72f, 0.2f, 0.32f),
			TerrainTool.Smooth => new Color(0.7f, 0.5f, 1.0f, 0.32f),
			TerrainTool.Flatten => new Color(0.35f, 0.85f, 0.95f, 0.32f),
			TerrainTool.Grow => new Color(0.3f, 0.9f, 0.5f, 0.32f),
			TerrainTool.Erode => new Color(1.0f, 0.45f, 0.35f, 0.32f),
			_ => new Color(0.25f, 0.7f, 1.0f, 0.32f)
		};
	}

	private bool IsMouseBlockedByUi()
	{
		Control? hoveredControl = GetViewport().GuiGetHoveredControl();

		if (hoveredControl == null)
		{
			return false;
		}

		if (_viewportClickRegion != null &&
			(hoveredControl == _viewportClickRegion || _viewportClickRegion.IsAncestorOf(hoveredControl)))
		{
			return false;
		}

		return true;
	}

	private bool IsEditingEnabled()
	{
		return _enabledCheck == null || _enabledCheck.ButtonPressed;
	}

	private void UpdateStatus(string? message = null)
	{
		if (!string.IsNullOrWhiteSpace(message))
		{
			_statusLabel.Text = message;
			return;
		}

		if (!IsEditingEnabled())
		{
			_statusLabel.Text = "Terrain editing is disabled.";
			return;
		}

		string materialText = _materialOption.Disabled || _materialOption.Selected < 0
			? string.Empty
			: $" · Material {_materialOption.GetItemText(_materialOption.Selected)}";

		_statusLabel.Text =
			$"{_tool} · {_shape} · Size {BrushSize:0.#}{materialText} · Interval {BrushIntervalMs:0}ms";
	}
}

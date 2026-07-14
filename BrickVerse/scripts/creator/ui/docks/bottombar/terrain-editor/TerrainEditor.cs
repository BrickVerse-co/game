// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
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
        _toolButtons[_growButton] = TerrainTool.Grow;
        _toolButtons[_erodeButton] = TerrainTool.Erode;

        foreach ((Button button, TerrainTool tool) in _toolButtons)
        {
            button.Pressed += () => SelectTool(tool);
        }

        _shapeOption.ItemSelected += OnShapeSelected;
        _materialOption.ItemSelected += _ => UpdateStatus();
        _enabledCheck.Toggled += OnEnabledToggled;
        _clearButton.Pressed += OnClearPressed;

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

    public override void _Process(double delta)
    {
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

        bool materialEnabled = tool is TerrainTool.Add or TerrainTool.Paint;
        _materialOption.Disabled = !materialEnabled;

        bool strengthEnabled = tool is TerrainTool.Paint or TerrainTool.Smooth or TerrainTool.Grow or TerrainTool.Erode;
        _strengthSlider.Editable = strengthEnabled;
        _strengthSpinBox.Editable = strengthEnabled;

        UpdatePreviewColor();
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
        _materialOption.Select(materialIndex);
    }

    private void PopulateMaterialOptions()
    {
        _materialOption.Clear();

        for (int index = 0; index < Terrain.MaterialPalette.Length; index++)
        {
            Part.PartMaterialEnum material = Terrain.MaterialPalette[index];
            _materialOption.AddItem(material.ToString(), index);
        }
    }

    private void PopulateMaterialIcons()
    {
        PopupMenu popup = _materialOption.GetPopup();
        _materialOption.AddThemeConstantOverride("icon_max_width", 12);
        popup.AddThemeConstantOverride("icon_max_width", 12);

        for (int index = 0; index < _materialOption.ItemCount; index++)
        {
            string materialName = _materialOption.GetItemText(index);
            Texture2D icon = LoadMaterialIcon(materialName);
            _materialOption.SetItemIcon(index, icon);
            popup.SetItemIcon(index, icon);
            popup.SetItemIconMaxWidth(index, 12);
        }
    }

    private static Texture2D LoadMaterialIcon(string materialName)
    {
        if (Enum.TryParse(materialName, out Part.PartMaterialEnum materialEnum))
        {
            Material material = Globals.LoadMaterial(materialEnum, 1.0f);

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

        uint hash = (uint)materialName.GetHashCode();
        float hue = (hash % 360u) / 360.0f;
        Image image = Image.Create(12, 12, false, Image.Format.Rgba8);
        image.Fill(Color.FromHsv(hue, 0.38f, 0.75f));
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D FitIconTexture(Texture2D source, int size)
    {
        Image image = source.GetImage();

        if (image.IsEmpty())
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

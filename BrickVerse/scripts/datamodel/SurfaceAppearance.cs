// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Resources;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel;

/// <summary>Engine-approved physically based material maps for a Part or Model.</summary>
[Instantiable]
public sealed partial class SurfaceAppearance : Instance
{
	private readonly HashSet<Part> _targets = [];
	private ImageAsset? _colorMap, _normalMap, _roughnessMap, _metalnessMap;
	private bool _enabled = true;
	private float _metalness, _roughness = 1;
	private Color _color = Colors.White;
	private AlphaModeEnum _alphaMode;
	internal StandardMaterial3D Material { get; } = new();

	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; Reconcile(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Color Color { get => _color; set { _color = value; RefreshMaterial(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(0f)] public float Metalness { get => _metalness; set { _metalness = Mathf.Clamp(value, 0, 1); RefreshMaterial(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float Roughness { get => _roughness; set { _roughness = Mathf.Clamp(value, 0, 1); RefreshMaterial(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public ImageAsset? ColorMap { get => _colorMap; set { SetAsset(ref _colorMap, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public ImageAsset? NormalMap { get => _normalMap; set { SetAsset(ref _normalMap, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public ImageAsset? RoughnessMap { get => _roughnessMap; set { SetAsset(ref _roughnessMap, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public ImageAsset? MetalnessMap { get => _metalnessMap; set { SetAsset(ref _metalnessMap, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(AlphaModeEnum.Overlay)] public AlphaModeEnum AlphaMode { get => _alphaMode; set { _alphaMode = value; RefreshMaterial(); OnPropertyChanged(); } }

	public override void Init() { SetProcess(true); Material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic; RefreshMaterial(); base.Init(); }
	public override void Ready() { Reconcile(); base.Ready(); }
	public override void Process(double delta) { Reconcile(); base.Process(delta); }
	public override void PreDelete()
	{
		foreach (ImageAsset? asset in Assets()) Unlink(asset);
		foreach (Part part in _targets) part.RefreshSurfaceAppearance(this);
		_targets.Clear();
		base.PreDelete();
	}

	private IEnumerable<ImageAsset?> Assets() => [_colorMap, _normalMap, _roughnessMap, _metalnessMap];
	private void SetAsset(ref ImageAsset? field, ImageAsset? value)
	{
		if (field == value) return;
		Unlink(field); field = value;
		if (field != null) { field.LinkTo(this); field.ResourceLoaded += OnMapLoaded; if (!field.IsResourceLoaded) field.QueueLoadResource(); }
		RefreshMaterial();
	}
	private void Unlink(ImageAsset? asset) { if (asset == null) return; asset.ResourceLoaded -= OnMapLoaded; asset.UnlinkFrom(this); }
	private void OnMapLoaded(Resource resource) => RefreshMaterial();
	private Texture2D? Texture(ImageAsset? asset) => asset?.Resource as Texture2D;
	private void RefreshMaterial()
	{
		Material.AlbedoColor = _color; Material.AlbedoTexture = Texture(_colorMap);
		Material.NormalEnabled = _normalMap != null; Material.NormalTexture = Texture(_normalMap);
		Material.Roughness = _roughness; Material.RoughnessTexture = Texture(_roughnessMap);
		Material.Metallic = _metalness; Material.MetallicTexture = Texture(_metalnessMap);
		Material.Transparency = _alphaMode == AlphaModeEnum.Transparency ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
		foreach (Part part in _targets) part.RefreshSurfaceAppearance();
	}
	private void Reconcile()
	{
		HashSet<Part> desired = !_enabled ? [] : Parent switch { Part p => [p], Model m => m.GetDescendants().OfType<Part>().ToHashSet(), _ => [] };
		foreach (Part part in _targets.Except(desired).ToArray()) { _targets.Remove(part); part.RefreshSurfaceAppearance(); }
		foreach (Part part in desired.Except(_targets).ToArray()) { _targets.Add(part); part.RefreshSurfaceAppearance(); }
	}

	[ScriptEnum("AlphaMode")]
	public enum AlphaModeEnum { Overlay, Transparency }
}

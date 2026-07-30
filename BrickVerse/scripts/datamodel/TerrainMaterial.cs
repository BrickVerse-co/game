// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Datamodel.Resources;
using Godot;
using System;

namespace BrickVerse.Datamodel;

/// <summary>
/// A creator-owned terrain palette entry. The slot is stored in voxel data,
/// while the name, surface type and tint remain editable and replicated.
/// </summary>
[Instantiable]
public sealed partial class TerrainMaterial : Instance
{
	public const int MaximumSlots = 16;

	private int _slot;
	private TerrainSurfaceType _surfaceType;
	private Part.PartMaterialEnum _surface = Part.PartMaterialEnum.SmoothPlastic;
	private Color _color = Colors.White;
	private ImageAsset? _albedoTexture;
	private ImageAsset? _normalTexture;
	private ImageAsset? _roughnessTexture;
	private ImageAsset? _metallicTexture;
	private float _textureScale = 0.1f;
	private float _roughness = 0.92f;
	private float _metallic;
	private float _normalStrength = 1.0f;

	[Editable, ScriptProperty, SyncVar, BrickVerse.Attributes.DefaultValueAttribute(0)]
	public int Slot
	{
		get => _slot;
		set
		{
			int validated = Math.Clamp(value, 0, MaximumSlots - 1);
			if (_slot == validated)
				return;
			if (Parent is Terrain terrain &&
				!terrain.IsMaterialSlotAvailable(validated, this))
			{
				throw new InvalidOperationException(
					$"Terrain material slot {validated} is already in use."
				);
			}
			_slot = validated;
			OnPropertyChanged();
			NotifyTerrain();
		}
	}

	[Editable, ScriptProperty, SyncVar, BrickVerse.Attributes.DefaultValueAttribute(TerrainSurfaceType.BuiltIn)]
	public TerrainSurfaceType SurfaceType
	{
		get => _surfaceType;
		set
		{
			if (_surfaceType == value)
				return;
			_surfaceType = value;
			OnPropertyChanged();
			NotifyTerrain();
		}
	}

	[Editable, ScriptProperty, SyncVar, BrickVerse.Attributes.DefaultValueAttribute(Part.PartMaterialEnum.SmoothPlastic)]
	public Part.PartMaterialEnum Surface
	{
		get => _surface;
		set
		{
			if (_surface == value)
				return;
			_surface = value;
			OnPropertyChanged();
			NotifyTerrain();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color Color
	{
		get => _color;
		set
		{
			if (_color == value)
				return;
			_color = value;
			OnPropertyChanged();
			NotifyTerrain();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public ImageAsset? AlbedoTexture
	{
		get => _albedoTexture;
		set => SetTexture(ref _albedoTexture, value, nameof(AlbedoTexture));
	}

	[Editable, ScriptProperty, SyncVar]
	public ImageAsset? NormalTexture
	{
		get => _normalTexture;
		set => SetTexture(ref _normalTexture, value, nameof(NormalTexture));
	}

	[Editable, ScriptProperty, SyncVar]
	public ImageAsset? RoughnessTexture
	{
		get => _roughnessTexture;
		set => SetTexture(ref _roughnessTexture, value, nameof(RoughnessTexture));
	}

	[Editable, ScriptProperty, SyncVar]
	public ImageAsset? MetallicTexture
	{
		get => _metallicTexture;
		set => SetTexture(ref _metallicTexture, value, nameof(MetallicTexture));
	}

	[Editable, ScriptProperty, SyncVar, BrickVerse.Attributes.DefaultValueAttribute(0.1f)]
	public float TextureScale
	{
		get => _textureScale;
		set
		{
			float validated = float.IsFinite(value) ? Math.Clamp(value, 0.001f, 100f) : 0.1f;
			if (Mathf.IsEqualApprox(_textureScale, validated))
				return;
			_textureScale = validated;
			OnPropertyChanged();
			NotifyTerrain();
		}
	}

	[Editable, ScriptProperty, SyncVar, BrickVerse.Attributes.DefaultValueAttribute(0.92f)]
	public float Roughness
	{
		get => _roughness;
		set => SetUnitValue(ref _roughness, value, nameof(Roughness));
	}

	[Editable, ScriptProperty, SyncVar, BrickVerse.Attributes.DefaultValueAttribute(0f)]
	public float Metallic
	{
		get => _metallic;
		set => SetUnitValue(ref _metallic, value, nameof(Metallic));
	}

	[Editable, ScriptProperty, SyncVar, BrickVerse.Attributes.DefaultValueAttribute(1f)]
	public float NormalStrength
	{
		get => _normalStrength;
		set => SetUnitValue(ref _normalStrength, value, nameof(NormalStrength));
	}

	public override void PostReparent()
	{
		base.PostReparent();
		if (Parent is Terrain terrain &&
			!terrain.IsMaterialSlotAvailable(_slot, this))
		{
			int availableSlot = terrain.FindAvailableMaterialSlot(this);
			if (availableSlot >= 0)
			{
				_slot = availableSlot;
				OnPropertyChanged(nameof(Slot));
			}
		}
		NotifyTerrain();
	}

	public override void PreDelete()
	{
		UnlinkTexture(_albedoTexture);
		UnlinkTexture(_normalTexture);
		UnlinkTexture(_roughnessTexture);
		UnlinkTexture(_metallicTexture);
		if (Parent is Terrain terrain)
			terrain.NotifyMaterialChanged();
		base.PreDelete();
	}

	private void NotifyTerrain()
	{
		if (Parent is Terrain terrain)
			terrain.NotifyMaterialChanged();
	}

	internal Texture2D? GetTexture(ImageAsset? asset) => asset?.Resource as Texture2D;

	private void SetTexture(
		ref ImageAsset? target,
		ImageAsset? value,
		string propertyName)
	{
		if (target == value)
			return;
		UnlinkTexture(target);
		target = value;
		if (target != null)
		{
			target.LinkTo(this);
			target.ResourceLoaded += OnTextureLoaded;
			if (!target.IsResourceLoaded)
				target.QueueLoadResource();
		}
		OnPropertyChanged(propertyName);
		NotifyTerrain();
	}

	private void UnlinkTexture(ImageAsset? asset)
	{
		if (asset == null)
			return;
		asset.ResourceLoaded -= OnTextureLoaded;
		asset.UnlinkFrom(this);
	}

	private void OnTextureLoaded(Resource resource) => NotifyTerrain();

	private void SetUnitValue(ref float target, float value, string propertyName)
	{
		float validated = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
		if (Mathf.IsEqualApprox(target, validated))
			return;
		target = validated;
		OnPropertyChanged(propertyName);
		NotifyTerrain();
	}
}

[ScriptEnum]
public enum TerrainSurfaceType
{
	BuiltIn,
	Custom,
}

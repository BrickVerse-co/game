// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace BrickVerse.Datamodel;

/// <summary>
/// Editable smooth voxel terrain backed by Zylann's Voxel Tools GDExtension.
///
/// Voxel Tools GDExtension classes are created and accessed through ClassDB,
/// GodotObject.Set and GodotObject.Call because they are not compiled into the
/// normal GodotSharp C# bindings.
/// </summary>
[Static("Terrain")]
public sealed partial class Terrain : Instance
{
	private const int SerialisationMagic = 0x42565452; // BVTR
	private const ushort SerialisationVersion = 1;
	private const int MaxSerialisedOperations = 10_000_000;

	private enum TerrainOperationType : byte
	{
		FillBall = 1,
		DigBall = 2,
		FillBlock = 3,
		DigBlock = 4,
		FillCylinder = 5,
		DigCylinder = 6,
		PaintBall = 7,
		SmoothBall = 8,
		GrowBall = 9,
		ErodeBall = 10,
		SetVoxelSdf = 11,
		SetVoxelMaterial = 12
	}

	private sealed class TerrainOperation
	{
		public TerrainOperationType Type;
		public Vector3 Position;
		public Vector3 Size;
		public Vector3 SecondaryPosition;
		public float Radius;
		public float Strength;
		public int Material;
		public int IntegerValue;
	}

	private Node3D? _voxelTerrainNode;
	private GodotObject? _voxelTerrain;
	private GodotObject? _voxelTool;
	private GodotObject? _mesher;
	private GodotObject? _generator;
	private GodotObject? _stream;
	private GodotObject? _voxelFormat;
	private ShaderMaterial? _terrainMaterial;
	private Node3D? _viewerNode;

	private readonly List<TerrainOperation> _operations = [];
	private TerrainOperation[]? _pendingReplayOperations;
	private int _pendingReplayIndex;

	private bool _initialized;
	private bool _isLoading;
	private bool _isUpdatingSerialisedTerrain;
	private bool _terrainDirty;

	private string _serialisedTerrain = string.Empty;
	private bool _autoSerialise = true;
	private bool _generateCollisions = true;
	private uint _collisionLayer = 1;
	private uint _collisionMask = uint.MaxValue;
	private int _collisionUpdateDelay;
	private float _collisionMargin = 0.04f;
	private float _defaultSdfStrength = 1.0f;
	private float _defaultSdfScale = 1.0f;
	private int _viewDistance = 512;
	private Color _baseColor = new(0.92f, 0.92f, 0.92f, 1.0f);
	private Color _grassColor = new(0.55f, 0.78f, 0.42f, 1.0f);
	private Color _stoneColor = new(0.70f, 0.72f, 0.74f, 1.0f);
	private Color _sandColor = new(0.90f, 0.82f, 0.60f, 1.0f);
	private Color _dirtColor = new(0.52f, 0.40f, 0.30f, 1.0f);
	private Color _snowColor = new(0.96f, 0.98f, 1.00f, 1.0f);
	private Color _concreteColor = new(0.74f, 0.74f, 0.72f, 1.0f);
	private Color _brickColor = new(0.70f, 0.32f, 0.27f, 1.0f);

	private static readonly (string Name, Part.PartMaterialEnum Surface, Color Color)[] DefaultMaterials =
	[
		("Base", Part.PartMaterialEnum.SmoothPlastic, new Color(0.92f, 0.92f, 0.92f, 1.0f)),
		("Grass", Part.PartMaterialEnum.Grass, new Color(0.55f, 0.78f, 0.42f, 1.0f)),
		("Stone", Part.PartMaterialEnum.Stone, new Color(0.70f, 0.72f, 0.74f, 1.0f)),
		("Sand", Part.PartMaterialEnum.Sand, new Color(0.90f, 0.82f, 0.60f, 1.0f)),
		("Dirt", Part.PartMaterialEnum.Dirt, new Color(0.52f, 0.40f, 0.30f, 1.0f)),
		("Snow", Part.PartMaterialEnum.Snow, new Color(0.96f, 0.98f, 1.00f, 1.0f)),
		("Concrete", Part.PartMaterialEnum.Concrete, new Color(0.74f, 0.74f, 0.72f, 1.0f)),
		("Brick", Part.PartMaterialEnum.Brick, new Color(0.70f, 0.32f, 0.27f, 1.0f)),
	];

	/// <summary>
	/// Compressed terrain edit data.
	///
	/// Assigning this value after initialization rebuilds the terrain
	/// immediately. Save this property with the rest of the world data.
	/// </summary>
	[Editable, ScriptProperty, SyncVar, DefaultValue("")]
	public string SerialisedTerrain
	{
		get
		{
			if (_terrainDirty && !_isLoading && !_isUpdatingSerialisedTerrain)
				SaveTerrain();
			return _serialisedTerrain;
		}
		set
		{
			value ??= string.Empty;

			if (_serialisedTerrain == value)
			{
				return;
			}

			_serialisedTerrain = value;
			_terrainDirty = false;
			OnPropertyChanged();

			if (_initialized && !_isUpdatingSerialisedTerrain)
			{
				LoadSerialisedTerrain();
			}
		}
	}

	/// <summary>
	/// Updates SerialisedTerrain after every terrain edit.
	///
	/// Disable this while performing large batches and call SaveTerrain()
	/// once after the batch is complete.
	/// </summary>
	[Editable, ScriptProperty, DefaultValue(true)]
	public bool AutoSerialise
	{
		get => _autoSerialise;
		set
		{
			if (_autoSerialise == value)
			{
				return;
			}

			_autoSerialise = value;
			OnPropertyChanged();

			if (_autoSerialise && _initialized && !_isLoading)
			{
				SaveTerrain();
			}
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool GenerateCollisions
	{
		get => _generateCollisions;
		set
		{
			if (_generateCollisions == value)
			{
				return;
			}

			_generateCollisions = value;

			if (_voxelTerrain != null)
			{
				TrySet(_voxelTerrain, "generate_collisions", value);
			}

			if (_viewerNode != null)
			{
				TrySet(_viewerNode, "requires_collisions", value);
			}

			OnPropertyChanged();
		}
	}

	[Editable(CustomPropertyControl = "Bitmap32"), ScriptProperty, SyncVar,
		DefaultValue(1u)]
	public uint CollisionLayer
	{
		get => _collisionLayer;
		set
		{
			if (_collisionLayer == value)
				return;
			_collisionLayer = value;
			if (_voxelTerrain != null)
				TrySet(_voxelTerrain, "collision_layer", (long)value);
			OnPropertyChanged();
		}
	}

	[Editable(CustomPropertyControl = "Bitmap32"), ScriptProperty, SyncVar,
		DefaultValue(uint.MaxValue)]
	public uint CollisionMask
	{
		get => _collisionMask;
		set
		{
			if (_collisionMask == value)
				return;
			_collisionMask = value;
			if (_voxelTerrain != null)
				TrySet(_voxelTerrain, "collision_mask", (long)value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar, DefaultValue(0)]
	public int CollisionUpdateDelay
	{
		get => _collisionUpdateDelay;
		set
		{
			int validated = Math.Clamp(value, 0, 10_000);
			if (_collisionUpdateDelay == validated)
				return;
			_collisionUpdateDelay = validated;
			if (_voxelTerrain != null)
				TrySet(_voxelTerrain, "collision_update_delay", validated);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar, DefaultValue(0.04f)]
	public float CollisionMargin
	{
		get => _collisionMargin;
		set
		{
			float validated = float.IsFinite(value)
				? Math.Clamp(value, 0.0f, 1.0f)
				: 0.04f;
			if (Mathf.IsEqualApprox(_collisionMargin, validated))
				return;
			_collisionMargin = validated;
			if (_voxelTerrain != null)
				TrySet(_voxelTerrain, "collision_margin", validated);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1.0f)]
	public float DefaultSdfStrength
	{
		get => _defaultSdfStrength;
		set
		{
			if (!float.IsFinite(value) || value <= 0.0f)
			{
				throw new ArgumentOutOfRangeException(
					nameof(value),
					"SDF strength must be finite and greater than zero.");
			}

			if (Mathf.IsEqualApprox(_defaultSdfStrength, value))
			{
				return;
			}

			_defaultSdfStrength = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1.0f)]
	public float DefaultSdfScale
	{
		get => _defaultSdfScale;
		set
		{
			if (!float.IsFinite(value) || value <= 0.0f)
			{
				throw new ArgumentOutOfRangeException(
					nameof(value),
					"SDF scale must be finite and greater than zero.");
			}

			if (Mathf.IsEqualApprox(_defaultSdfScale, value))
			{
				return;
			}

			_defaultSdfScale = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(512)]
	public int ViewDistance
	{
		get => _viewDistance;
		set
		{
			int newValue = Math.Max(value, 16);

			if (_viewDistance == newValue)
			{
				return;
			}

			_viewDistance = newValue;

			if (_viewerNode != null)
			{
				TrySet(_viewerNode, "view_distance", _viewDistance);
			}

			if (_voxelTerrain != null)
			{
				TrySet(_voxelTerrain, "view_distance", _viewDistance);
			}

			OnPropertyChanged();
		}
	}

	[ScriptProperty, NoSync, Attributes.Obsolete("Use TerrainMaterial children")]
	public Color BaseColor
	{
		get => _baseColor;
		set => SetMaterialTint(ref _baseColor, value, nameof(BaseColor));
	}

	[ScriptProperty, NoSync, Attributes.Obsolete("Use TerrainMaterial children")]
	public Color GrassColor
	{
		get => _grassColor;
		set => SetMaterialTint(ref _grassColor, value, nameof(GrassColor));
	}

	[ScriptProperty, NoSync, Attributes.Obsolete("Use TerrainMaterial children")]
	public Color StoneColor
	{
		get => _stoneColor;
		set => SetMaterialTint(ref _stoneColor, value, nameof(StoneColor));
	}

	[ScriptProperty, NoSync, Attributes.Obsolete("Use TerrainMaterial children")]
	public Color SandColor
	{
		get => _sandColor;
		set => SetMaterialTint(ref _sandColor, value, nameof(SandColor));
	}

	[ScriptProperty, NoSync, Attributes.Obsolete("Use TerrainMaterial children")]
	public Color DirtColor
	{
		get => _dirtColor;
		set => SetMaterialTint(ref _dirtColor, value, nameof(DirtColor));
	}

	[ScriptProperty, NoSync, Attributes.Obsolete("Use TerrainMaterial children")]
	public Color SnowColor
	{
		get => _snowColor;
		set => SetMaterialTint(ref _snowColor, value, nameof(SnowColor));
	}

	[ScriptProperty, NoSync, Attributes.Obsolete("Use TerrainMaterial children")]
	public Color ConcreteColor
	{
		get => _concreteColor;
		set => SetMaterialTint(ref _concreteColor, value, nameof(ConcreteColor));
	}

	[ScriptProperty, NoSync, Attributes.Obsolete("Use TerrainMaterial children")]
	public Color BrickColor
	{
		get => _brickColor;
		set => SetMaterialTint(ref _brickColor, value, nameof(BrickColor));
	}

	internal Node3D? VoxelTerrainNode => _voxelTerrainNode;

	public override void Init()
	{
		CreateVoxelTerrain();
		SetProcess(true);
		_initialized = true;

		if (!string.IsNullOrWhiteSpace(_serialisedTerrain))
		{
			LoadSerialisedTerrain();
		}

		base.Init();
	}

	public override void Ready()
	{
		base.Ready();
		ChildAdded.Connect(OnTerrainChildChanged);
		ChildRemoved.Connect(OnTerrainChildChanged);

		if (Root.Network == null || Root.Network.IsServer)
			EnsureDefaultMaterials();
		else
			NotifyMaterialChanged();
	}

	public override void Process(double delta)
	{
		base.Process(delta);

		if (_pendingReplayOperations != null)
		{
			TryReplayPendingOperations();
			return;
		}

		Camera3D? camera =
			Root?.Environment?.CurrentGDCamera
			?? Root?.RootViewport?.GetCamera3D()
			?? GDNode?.GetViewport()?.GetCamera3D();

		if (camera != null)
		{
			SetEditorViewerPosition(camera.GlobalPosition);
		}
	}

	public override void PreDelete()
	{
		_initialized = false;
		_pendingReplayOperations = null;
		_pendingReplayIndex = 0;
		ChildAdded.Disconnect(OnTerrainChildChanged);
		ChildRemoved.Disconnect(OnTerrainChildChanged);
		_operations.Clear();
		DestroyVoxelTerrain();
		base.PreDelete();
	}

	[ScriptMethod]
	public TerrainMaterial[] GetMaterials()
	{
		return GetChildrenOfClass<TerrainMaterial>()
			.OrderBy(material => material.Slot)
			.ThenBy(material => material.Name, StringComparer.Ordinal)
			.ToArray();
	}

	public TerrainMaterial? GetMaterial(int slot)
	{
		return GetChildrenOfClass<TerrainMaterial>()
			.FirstOrDefault(material => material.Slot == slot);
	}

	internal int FindAvailableMaterialSlot(TerrainMaterial? except = null)
	{
		HashSet<int> occupied = GetChildrenOfClass<TerrainMaterial>()
			.Where(material => material != except)
			.Select(material => material.Slot)
			.ToHashSet();
		for (int slot = 0; slot < TerrainMaterial.MaximumSlots; slot++)
		{
			if (!occupied.Contains(slot))
				return slot;
		}
		return -1;
	}

	internal bool IsMaterialSlotAvailable(int slot, TerrainMaterial except)
	{
		return !GetChildrenOfClass<TerrainMaterial>().Any(
			material => material != except && material.Slot == slot
		);
	}

	internal void NotifyMaterialChanged()
	{
		UpdateTerrainMaterialParameters();
	}

	private void OnTerrainChildChanged(Instance child)
	{
		if (child is TerrainMaterial)
			NotifyMaterialChanged();
	}

	private void EnsureDefaultMaterials()
	{
		if (GetChildrenOfClass<TerrainMaterial>().Length > 0)
		{
			NotifyMaterialChanged();
			return;
		}

		Color[] migratedColors =
		[
			_baseColor,
			_grassColor,
			_stoneColor,
			_sandColor,
			_dirtColor,
			_snowColor,
			_concreteColor,
			_brickColor,
		];
		for (int slot = 0; slot < DefaultMaterials.Length; slot++)
		{
			var definition = DefaultMaterials[slot];
			TerrainMaterial material = New<TerrainMaterial>(this);
			material.Name = definition.Name;
			material.Slot = slot;
			material.Surface = definition.Surface;
			material.Color = migratedColors[slot];
		}
		NotifyMaterialChanged();
	}

	#region Public shape API

	[ScriptMethod]
	public void FillBall(Vector3 center, float radius, int material = 0)
	{
		ValidateRadius(radius);
		ValidateMaterial(material);

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.FillBall,
			Position = center,
			Radius = radius,
			Material = material
		});
	}

	[ScriptMethod]
	public void DigBall(Vector3 center, float radius)
	{
		ValidateRadius(radius);

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.DigBall,
			Position = center,
			Radius = radius
		});
	}

	[ScriptMethod]
	public void FillBlock(Vector3 center, Vector3 size, int material = 0)
	{
		ValidateSize(size);
		ValidateMaterial(material);

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.FillBlock,
			Position = center,
			Size = size.Abs(),
			Material = material
		});
	}

	public void FillBlock(Transform3D transform, Vector3 size, int material = 0)
	{
		FillBlock(transform.Origin, size, material);
	}

	[ScriptMethod]
	public void DigBlock(Vector3 center, Vector3 size)
	{
		ValidateSize(size);

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.DigBlock,
			Position = center,
			Size = size.Abs()
		});
	}

	public void DigBlock(Transform3D transform, Vector3 size)
	{
		DigBlock(transform.Origin, size);
	}

	[ScriptMethod]
	public void FillCylinder(
		Vector3 center,
		float height,
		float radius,
		int material = 0)
	{
		FillCylinder(
			new Transform3D(Basis.Identity, center),
			height,
			radius,
			material);
	}

	public void FillCylinder(
		Transform3D transform,
		float height,
		float radius,
		int material = 0)
	{
		ValidateCylinder(height, radius);
		ValidateMaterial(material);

		Vector3 axis = transform.Basis.Y.Normalized();
		Vector3 halfAxis = axis * (height * 0.5f);

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.FillCylinder,
			Position = transform.Origin - halfAxis,
			SecondaryPosition = transform.Origin + halfAxis,
			Radius = radius,
			Material = material
		});
	}

	[ScriptMethod]
	public void DigCylinder(Vector3 center, float height, float radius)
	{
		DigCylinder(
			new Transform3D(Basis.Identity, center),
			height,
			radius);
	}

	public void DigCylinder(
		Transform3D transform,
		float height,
		float radius)
	{
		ValidateCylinder(height, radius);

		Vector3 axis = transform.Basis.Y.Normalized();
		Vector3 halfAxis = axis * (height * 0.5f);

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.DigCylinder,
			Position = transform.Origin - halfAxis,
			SecondaryPosition = transform.Origin + halfAxis,
			Radius = radius
		});
	}

	/// <summary>
	/// Paints a Mixel4 terrain texture without changing terrain geometry.
	/// Material indices are limited to the texture set configured by the
	/// terrain material and mesher.
	/// </summary>
	[ScriptMethod]
	public void PaintBall(
		Vector3 center,
		float radius,
		int material,
		float opacity = 1.0f)
	{
		ValidateRadius(radius);
		ValidateMaterial(material);

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.PaintBall,
			Position = center,
			Radius = radius,
			Material = material,
			Strength = Mathf.Clamp(opacity, 0.0f, 1.0f)
		});
	}

	[ScriptMethod]
	public void SmoothBall(
		Vector3 center,
		float radius,
		int blurRadius = 2)
	{
		ValidateRadius(radius);

		if (blurRadius < 1)
		{
			throw new ArgumentOutOfRangeException(
				nameof(blurRadius),
				"Blur radius must be at least one.");
		}

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.SmoothBall,
			Position = center,
			Radius = radius,
			IntegerValue = blurRadius
		});
	}

	[ScriptMethod]
	public void GrowBall(
		Vector3 center,
		float radius,
		float strength = 1.0f)
	{
		ValidateRadius(radius);
		ValidateStrength(strength);

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.GrowBall,
			Position = center,
			Radius = radius,
			Strength = strength
		});
	}

	[ScriptMethod]
	public void ErodeBall(
		Vector3 center,
		float radius,
		float strength = 1.0f)
	{
		ValidateRadius(radius);
		ValidateStrength(strength);

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.ErodeBall,
			Position = center,
			Radius = radius,
			Strength = strength
		});
	}

	#endregion

	#region Individual voxel API

	[ScriptMethod]
	public void SetVoxelSdf(Vector3 position, float sdf)
	{
		if (!float.IsFinite(sdf))
		{
			throw new ArgumentOutOfRangeException(
				nameof(sdf),
				"SDF value must be finite.");
		}

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.SetVoxelSdf,
			Position = position,
			Strength = sdf
		});
	}

	[ScriptMethod]
	public float GetVoxelSdf(Vector3 position)
	{
		EnsureTool();
		SetToolChannel("CHANNEL_SDF", 1);

		return _voxelTool!
			.Call("get_voxel_f", ToVector3I(position))
			.AsSingle();
	}

	[ScriptMethod]
	public void SetVoxelMaterial(Vector3 position, int material)
	{
		ValidateMaterial(material);

		ExecuteAndRecord(new TerrainOperation
		{
			Type = TerrainOperationType.SetVoxelMaterial,
			Position = position,
			Material = material
		});
	}

	[ScriptMethod]
	public int GetVoxelMaterial(Vector3 position)
	{
		EnsureTool();

		Vector3I voxelPosition = ToVector3I(position);

		SetToolChannel("CHANNEL_INDICES", 3);
		int packedIndices = _voxelTool!
			.Call("get_voxel", voxelPosition)
			.AsInt32();

		SetToolChannel("CHANNEL_WEIGHTS", 4);
		int packedWeights = _voxelTool!
			.Call("get_voxel", voxelPosition)
			.AsInt32();

		int bestMaterial = packedIndices & 0x0f;
		int bestWeight = packedWeights & 0x0f;

		for (int slot = 1; slot < 4; slot++)
		{
			int shift = slot * 4;
			int weight = (packedWeights >> shift) & 0x0f;

			if (weight > bestWeight)
			{
				bestWeight = weight;
				bestMaterial = (packedIndices >> shift) & 0x0f;
			}
		}

		return Math.Clamp(
			bestMaterial,
			0,
			TerrainMaterial.MaximumSlots - 1);
	}

	[ScriptMethod]
	public void SetVoxelMetadata(Vector3 position, Variant metadata)
	{
		EnsureTool();
		_voxelTool!.Call(
			"set_voxel_metadata",
			ToVector3I(position),
			metadata);
	}

	[ScriptMethod]
	public Variant GetVoxelMetadata(Vector3 position)
	{
		EnsureTool();

		return _voxelTool!.Call(
			"get_voxel_metadata",
			ToVector3I(position));
	}

	#endregion

	#region Queries

	[ScriptMethod]
	public bool IsAreaEditable(Vector3 minimum, Vector3 maximum)
	{
		EnsureTool();

		if (!_voxelTool!.HasMethod("is_area_editable"))
		{
			return true;
		}

		Vector3 min = new(
			Mathf.Min(minimum.X, maximum.X),
			Mathf.Min(minimum.Y, maximum.Y),
			Mathf.Min(minimum.Z, maximum.Z));

		Vector3 max = new(
			Mathf.Max(minimum.X, maximum.X),
			Mathf.Max(minimum.Y, maximum.Y),
			Mathf.Max(minimum.Z, maximum.Z));

		Aabb area = new(min, (max - min).Max(Vector3.One));

		return _voxelTool
			.Call("is_area_editable", area)
			.AsBool();
	}

	internal void SetEditorViewerPosition(Vector3 worldPosition)
	{
		if (_viewerNode != null &&
			GodotObject.IsInstanceValid(_viewerNode))
		{
			_viewerNode.GlobalPosition = worldPosition;
		}
	}

	internal bool TryRaycast(
		Vector3 origin,
		Vector3 direction,
		float maximumDistance,
		out Vector3 position,
		out Vector3 normal)
	{
		position = default;
		normal = Vector3.Up;

		if (direction.IsZeroApprox() || maximumDistance <= 0.0f)
		{
			return false;
		}

		EnsureTool();

		if (!_voxelTool!.HasMethod("raycast"))
		{
			return false;
		}

		_voxelTool.Call("set_raycast_normal_enabled", true);

		Variant raycastVariant = _voxelTool.Call(
			"raycast",
			origin,
			direction.Normalized(),
			maximumDistance);

		GodotObject? result = raycastVariant.AsGodotObject();

		if (result == null)
		{
			return false;
		}

		float distance = result.Get("distance").AsSingle();

		if (!float.IsFinite(distance) ||
			distance < 0.0f ||
			distance > maximumDistance)
		{
			return false;
		}

		position = origin + direction.Normalized() * distance;

		Variant normalVariant = result.Get("normal");

		if (normalVariant.VariantType == Variant.Type.Vector3)
		{
			Vector3 hitNormal = normalVariant.AsVector3();

			if (!hitNormal.IsZeroApprox())
			{
				normal = hitNormal.Normalized();
			}
		}

		return true;
	}

	[ScriptMethod]
	public int CountOperations()
	{
		return _operations.Count;
	}

	#endregion

	#region Persistence

	/// <summary>
	/// Serialises all edits performed through this Terrain instance and updates
	/// SerialisedTerrain.
	/// </summary>
	[ScriptMethod]
	public string SaveTerrain()
	{
		byte[] raw;

		using (MemoryStream rawStream = new())
		using (BinaryWriter writer = new(rawStream))
		{
			writer.Write(SerialisationMagic);
			writer.Write(SerialisationVersion);
			writer.Write(_operations.Count);

			foreach (TerrainOperation operation in _operations)
			{
				WriteOperation(writer, operation);
			}

			writer.Flush();
			raw = rawStream.ToArray();
		}

		byte[] compressed;

		using (MemoryStream compressedStream = new())
		{
			using (GZipStream gzip = new(
				compressedStream,
				CompressionLevel.SmallestSize,
				leaveOpen: true))
			{
				gzip.Write(raw, 0, raw.Length);
			}

			compressed = compressedStream.ToArray();
		}

		string encoded = Convert.ToBase64String(compressed);

		_isUpdatingSerialisedTerrain = true;

		try
		{
			_serialisedTerrain = encoded;
			_terrainDirty = false;
			OnPropertyChanged(nameof(SerialisedTerrain));
		}
		finally
		{
			_isUpdatingSerialisedTerrain = false;
		}

		return encoded;
	}

	internal void FlushTerrainSerialization()
	{
		if (_terrainDirty && !_isLoading && !_isUpdatingSerialisedTerrain)
			SaveTerrain();
	}

	/// <summary>
	/// Clears the active terrain and replays SerialisedTerrain.
	/// </summary>
	[ScriptMethod]
	public void LoadSerialisedTerrain()
	{
		if (!_initialized || _isLoading)
		{
			return;
		}

		_isLoading = true;
		TerrainOperation[] previousOperations = [.. _operations];
		bool terrainWasReplaced = false;

		try
		{
			if (string.IsNullOrWhiteSpace(_serialisedTerrain))
			{
				_pendingReplayOperations = null;
				_pendingReplayIndex = 0;
				_operations.Clear();
				CreateVoxelTerrain();
				terrainWasReplaced = true;
				return;
			}

			List<TerrainOperation> loadedOperations = [];
			byte[] compressed = Convert.FromBase64String(
				_serialisedTerrain.Trim());

			using MemoryStream compressedStream = new(compressed);
			using GZipStream gzip = new(
				compressedStream,
				CompressionMode.Decompress);
			using BinaryReader reader = new(gzip);

			int magic = reader.ReadInt32();

			if (magic != SerialisationMagic)
			{
				throw new InvalidDataException(
					"The supplied data is not valid BrickVerse terrain data.");
			}

			ushort version = reader.ReadUInt16();

			if (version > SerialisationVersion)
			{
				throw new InvalidDataException(
					$"Terrain version {version} is newer than supported " +
					$"version {SerialisationVersion}.");
			}

			int operationCount = reader.ReadInt32();

			if (operationCount < 0 ||
				operationCount > MaxSerialisedOperations)
			{
				throw new InvalidDataException(
					"Terrain operation count is invalid.");
			}

			for (int index = 0; index < operationCount; index++)
			{
				TerrainOperation operation = ReadOperation(reader);
				if (!Enum.IsDefined(operation.Type))
					throw new InvalidDataException(
						$"Terrain operation {index} has an unknown type.");
				loadedOperations.Add(operation);
			}

			// Do not destroy the currently rendered terrain until the complete
			// serialized stream has passed validation. VoxelLodTerrain loads
			// editable blocks asynchronously, so replay is queued and applied
			// in order as each operation's region becomes editable.
			CreateVoxelTerrain();
			terrainWasReplaced = true;
			_operations.Clear();
			_operations.AddRange(loadedOperations);
			_pendingReplayOperations = [.. loadedOperations];
			_pendingReplayIndex = 0;
			_terrainDirty = false;
			TryReplayPendingOperations();
		}
		catch (Exception exception)
		{
			if (terrainWasReplaced)
			{
				try
				{
					CreateVoxelTerrain();
					_operations.Clear();
					_operations.AddRange(previousOperations);
					_pendingReplayOperations = [.. previousOperations];
					_pendingReplayIndex = 0;
					TryReplayPendingOperations();
				}
				catch (Exception recoveryException)
				{
					GD.PushError(
						$"Failed to restore terrain after a load error: {recoveryException}");
				}
			}
			GD.PushError(
				$"Failed to load serialised terrain: {exception}");
		}
		finally
		{
			_isLoading = false;
		}
	}

	[ScriptMethod]
	public void Clear()
	{
		_pendingReplayOperations = null;
		_pendingReplayIndex = 0;
		_operations.Clear();
		_terrainDirty = true;
		CreateVoxelTerrain();

		if (AutoSerialise)
		{
			SaveTerrain();
		}
		else
		{
			_isUpdatingSerialisedTerrain = true;

			try
			{
				_serialisedTerrain = string.Empty;
				_terrainDirty = false;
				OnPropertyChanged(nameof(SerialisedTerrain));
			}
			finally
			{
				_isUpdatingSerialisedTerrain = false;
			}
		}
	}

	private static void WriteOperation(
		BinaryWriter writer,
		TerrainOperation operation)
	{
		writer.Write((byte)operation.Type);
		WriteVector3(writer, operation.Position);
		WriteVector3(writer, operation.Size);
		WriteVector3(writer, operation.SecondaryPosition);
		writer.Write(operation.Radius);
		writer.Write(operation.Strength);
		writer.Write(operation.Material);
		writer.Write(operation.IntegerValue);
	}

	private static TerrainOperation ReadOperation(BinaryReader reader)
	{
		return new TerrainOperation
		{
			Type = (TerrainOperationType)reader.ReadByte(),
			Position = ReadVector3(reader),
			Size = ReadVector3(reader),
			SecondaryPosition = ReadVector3(reader),
			Radius = reader.ReadSingle(),
			Strength = reader.ReadSingle(),
			Material = reader.ReadInt32(),
			IntegerValue = reader.ReadInt32()
		};
	}

	private static void WriteVector3(
		BinaryWriter writer,
		Vector3 value)
	{
		writer.Write(value.X);
		writer.Write(value.Y);
		writer.Write(value.Z);
	}

	private static Vector3 ReadVector3(BinaryReader reader)
	{
		return new Vector3(
			reader.ReadSingle(),
			reader.ReadSingle(),
			reader.ReadSingle());
	}

	#endregion

	#region Terrain setup

	private void CreateVoxelTerrain()
	{
		DestroyVoxelTerrain();

		_voxelTerrain = InstantiateExtensionClass("VoxelLodTerrain");
		_voxelTerrainNode = _voxelTerrain as Node3D;

		if (_voxelTerrainNode == null)
		{
			_voxelTerrain = null;

			throw new InvalidOperationException(
				"VoxelLodTerrain was created but was not a Node3D.");
		}

		_voxelTerrainNode.Name = "VoxelTerrain";
		GDNode.AddChild(
			_voxelTerrainNode,
			false,
			Node.InternalMode.Front);

		ConfigureVoxelFormat();
		ConfigureStream();
		ConfigureMesher();
		ConfigureGenerator();
		ConfigureTerrain();
		ApplyTerrainMaterial();
		CreateViewer();
		RefreshVoxelTool();
	}

	private void DestroyVoxelTerrain()
	{
		_voxelTool = null;
		_mesher = null;
		_generator = null;
		_stream = null;
		_voxelFormat = null;
		_terrainMaterial = null;
		_voxelTerrain = null;
		_viewerNode = null;

		if (_voxelTerrainNode != null &&
			GodotObject.IsInstanceValid(_voxelTerrainNode))
		{
			_voxelTerrainNode.QueueFree();
		}

		_voxelTerrainNode = null;
	}

	private void ConfigureVoxelFormat()
	{
		EnsureTerrain();

		if (!ClassDB.ClassExists("VoxelFormat"))
		{
			return;
		}

		_voxelFormat = InstantiateExtensionClass("VoxelFormat");

		TrySet(_voxelFormat, "sdf_depth", 1);
		TrySet(_voxelFormat, "indices_depth", 1);
		TrySet(_voxelFormat, "weights_depth", 1);

		TrySet(
			_voxelTerrain!,
			"voxel_format",
			Variant.From(_voxelFormat));
	}

	private void ConfigureStream()
	{
		EnsureTerrain();

		if (!ClassDB.ClassExists("VoxelStreamMemory"))
		{
			return;
		}

		_stream = InstantiateExtensionClass("VoxelStreamMemory");
		TrySet(_voxelTerrain!, "stream", Variant.From(_stream));
	}

	private void ConfigureMesher()
	{
		EnsureTerrain();

		_mesher = InstantiateExtensionClass("VoxelMesherTransvoxel");

		int mixel4Mode = ResolveTransvoxelTexturingMode();

		if (!TrySet(_mesher, "texturing_mode", mixel4Mode))
		{
			throw new InvalidOperationException(
				"VoxelMesherTransvoxel does not expose texturing_mode. " +
				"The installed Voxel Tools build is incompatible.");
		}

		_voxelTerrain!.Set("mesher", Variant.From(_mesher));
	}

	private static int ResolveTransvoxelTexturingMode()
	{
		const string className = "VoxelMesherTransvoxel";

		string[] candidateConstants =
		[
			"TEXTURES_MIXEL4_S4",
			"TEXTURES_BLEND_4_OVER_16",
			"TEXTURES_MIXEL4",
			"TEXTURING_MIXEL4",
			"TEXTURES_BLEND_4_OVER_16_INDEXED"
		];

		foreach (string constantName in candidateConstants)
		{
			if (ClassDB.ClassHasIntegerConstant(
				className,
				constantName))
			{
				return (int)ClassDB.ClassGetIntegerConstant(
					className,
					constantName);
			}
		}

		return 0;
	}

	private void ApplyTerrainMaterial()
	{
		EnsureTerrain();

		Shader shader = new()
		{
			Code = GetTerrainShaderCode()
		};

		_terrainMaterial = new ShaderMaterial
		{
			Shader = shader
		};

		UpdateTerrainMaterialParameters();
		_voxelTerrain!.Set("material", _terrainMaterial);
	}

	private void UpdateTerrainMaterialParameters()
	{
		if (_terrainMaterial == null)
		{
			return;
		}

		float[] roughness = new float[TerrainMaterial.MaximumSlots];
		float[] metallic = new float[TerrainMaterial.MaximumSlots];
		float[] normalStrength = new float[TerrainMaterial.MaximumSlots];
		float[] textureScale = new float[TerrainMaterial.MaximumSlots];
		int customMask = 0;
		for (int slot = 0; slot < TerrainMaterial.MaximumSlots; slot++)
		{
			_terrainMaterial.SetShaderParameter($"u_color_{slot}", _baseColor);
			roughness[slot] = 0.92f;
			normalStrength[slot] = 1.0f;
			textureScale[slot] = 0.1f;
		}
		foreach (TerrainMaterial material in GetMaterials())
		{
			_terrainMaterial.SetShaderParameter(
				$"u_color_{material.Slot}",
				material.Color
			);
			roughness[material.Slot] = material.Roughness;
			metallic[material.Slot] = material.Metallic;
			normalStrength[material.Slot] = material.NormalStrength;
			textureScale[material.Slot] = material.GetSurfaceTextureScale();
			if (material.SurfaceType == TerrainSurfaceType.Custom)
				customMask |= 1 << material.Slot;
		}
		_terrainMaterial.SetShaderParameter("u_custom_mask", customMask);
		_terrainMaterial.SetShaderParameter("u_roughness", roughness);
		_terrainMaterial.SetShaderParameter("u_metallic", metallic);
		_terrainMaterial.SetShaderParameter("u_normal_strength", normalStrength);
		_terrainMaterial.SetShaderParameter("u_texture_scale", textureScale);
		_terrainMaterial.SetShaderParameter(
			"u_albedo_textures",
			BuildTextureArray(
				material => material.SurfaceType == TerrainSurfaceType.Custom
					? material.GetTexture(material.AlbedoTexture)
					: material.GetSurfaceTexture("albedo", "use_albedo_texture"),
				Colors.White
			)
		);
		_terrainMaterial.SetShaderParameter(
			"u_normal_textures",
			BuildTextureArray(
				material => material.SurfaceType == TerrainSurfaceType.Custom
					? material.GetTexture(material.NormalTexture)
					: material.GetSurfaceTexture("normal_tex", "use_normal_texture"),
				new Color(0.5f, 0.5f, 1)
			)
		);
		_terrainMaterial.SetShaderParameter(
			"u_roughness_textures",
			BuildTextureArray(
				material => material.SurfaceType == TerrainSurfaceType.Custom
					? material.GetTexture(material.RoughnessTexture)
					: material.GetSurfaceTexture("orm", "use_orm_texture"),
				Colors.White
			)
		);
		_terrainMaterial.SetShaderParameter(
			"u_metallic_textures",
			BuildTextureArray(
				material => material.SurfaceType == TerrainSurfaceType.Custom
					? material.GetTexture(material.MetallicTexture)
					: material.GetSurfaceTexture("orm", "use_orm_texture"),
				Colors.White
			)
		);
	}

	private Texture2DArray BuildTextureArray(
		Func<TerrainMaterial, Texture2D?> selector,
		Color fallback)
	{
		const int textureSize = 256;
		Godot.Collections.Array<Image> images = [];
		for (int slot = 0; slot < TerrainMaterial.MaximumSlots; slot++)
		{
			Texture2D? texture = GetMaterial(slot) is TerrainMaterial material
				? selector(material)
				: null;
			Image image = texture?.GetImage() ?? Image.CreateEmpty(
				textureSize,
				textureSize,
				false,
				Image.Format.Rgba8
			);
			if (texture == null)
				image.Fill(fallback);
			else
			{
				image = (Image)image.Duplicate();
				if (image.IsCompressed())
					image.Decompress();
				image.Convert(Image.Format.Rgba8);
				image.Resize(textureSize, textureSize, Image.Interpolation.Lanczos);
			}
			if (!image.HasMipmaps())
				image.GenerateMipmaps();
			images.Add(image);
		}
		Texture2DArray array = new();
		array.CreateFromImages(images);
		return array;
	}

	private static string GetTerrainShaderCode()
	{
		return """
shader_type spatial;
render_mode cull_back, depth_draw_opaque;

uniform int u_transition_mask;

uniform vec4 u_color_0 : source_color = vec4(0.92, 0.92, 0.92, 1.0);
uniform vec4 u_color_1 : source_color = vec4(0.55, 0.78, 0.42, 1.0);
uniform vec4 u_color_2 : source_color = vec4(0.70, 0.72, 0.74, 1.0);
uniform vec4 u_color_3 : source_color = vec4(0.90, 0.82, 0.60, 1.0);
uniform vec4 u_color_4 : source_color = vec4(0.52, 0.40, 0.30, 1.0);
uniform vec4 u_color_5 : source_color = vec4(0.96, 0.98, 1.00, 1.0);
uniform vec4 u_color_6 : source_color = vec4(0.74, 0.74, 0.72, 1.0);
uniform vec4 u_color_7 : source_color = vec4(0.70, 0.32, 0.27, 1.0);
uniform vec4 u_color_8 : source_color = vec4(0.92, 0.92, 0.92, 1.0);
uniform vec4 u_color_9 : source_color = vec4(0.92, 0.92, 0.92, 1.0);
uniform vec4 u_color_10 : source_color = vec4(0.92, 0.92, 0.92, 1.0);
uniform vec4 u_color_11 : source_color = vec4(0.92, 0.92, 0.92, 1.0);
uniform vec4 u_color_12 : source_color = vec4(0.92, 0.92, 0.92, 1.0);
uniform vec4 u_color_13 : source_color = vec4(0.92, 0.92, 0.92, 1.0);
uniform vec4 u_color_14 : source_color = vec4(0.92, 0.92, 0.92, 1.0);
uniform vec4 u_color_15 : source_color = vec4(0.92, 0.92, 0.92, 1.0);
uniform int u_custom_mask = 0;
uniform float u_roughness[16];
uniform float u_metallic[16];
uniform float u_normal_strength[16];
uniform float u_texture_scale[16];
uniform sampler2DArray u_albedo_textures : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray u_normal_textures : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray u_roughness_textures : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray u_metallic_textures : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

varying vec4 v_indices;
varying vec4 v_weights;
varying vec3 v_world_normal;
varying vec3 v_world_position;

vec4 decode_8bit_vec4(float value) {
	uint packed = floatBitsToUint(value);
	return vec4(
		float(packed & uint(0xff)),
		float((packed >> uint(8)) & uint(0xff)),
		float((packed >> uint(16)) & uint(0xff)),
		float((packed >> uint(24)) & uint(0xff))
	);
}

float get_transvoxel_secondary_factor(int data) {
	int transition_mask = u_transition_mask & 0xff;
	int cell_border_mask = (data >> 0) & 63;
	int vertex_border_mask = (data >> 8) & 63;
	int matching = transition_mask & cell_border_mask;
	float factor = float(matching != 0);
	factor *= float((vertex_border_mask & ~transition_mask) == 0);
	return factor;
}

vec3 get_transvoxel_position(vec3 vertex_position, vec4 data) {
	int packed = floatBitsToInt(data.a);
	float secondary_factor =
		get_transvoxel_secondary_factor(packed);
	vec3 result =
		mix(vertex_position, data.xyz, secondary_factor);

	int transition = (packed >> 16) & 0xff;
	float transition_cull = float(
		transition == 0 ||
		(transition & u_transition_mask) != 0);

	return result * transition_cull;
}

vec3 palette_color(int index) {
	switch (index) {
		case 1: return u_color_1.rgb;
		case 2: return u_color_2.rgb;
		case 3: return u_color_3.rgb;
		case 4: return u_color_4.rgb;
		case 5: return u_color_5.rgb;
		case 6: return u_color_6.rgb;
		case 7: return u_color_7.rgb;
		case 8: return u_color_8.rgb;
		case 9: return u_color_9.rgb;
		case 10: return u_color_10.rgb;
		case 11: return u_color_11.rgb;
		case 12: return u_color_12.rgb;
		case 13: return u_color_13.rgb;
		case 14: return u_color_14.rgb;
		case 15: return u_color_15.rgb;
		default: return u_color_0.rgb;
	}
}

bool is_custom(int index) {
	return (u_custom_mask & (1 << index)) != 0;
}

vec2 triplanar_uv(vec3 position, vec3 normal, float scale) {
	vec3 axis = abs(normal);
	if (axis.y >= axis.x && axis.y >= axis.z) return position.xz * scale;
	if (axis.x >= axis.z) return position.zy * scale;
	return position.xy * scale;
}

vec3 material_albedo(int index, vec2 uv) {
	vec3 tint = palette_color(index);
	if (!is_custom(index)) return tint;
	return tint * texture(u_albedo_textures, vec3(uv, float(index))).rgb;
}

float material_roughness(int index, vec2 uv) {
	float value = u_roughness[index];
	vec3 map = texture(u_roughness_textures, vec3(uv, float(index))).rgb;
	value *= is_custom(index) ? map.r : map.g;
	return value;
}

float material_metallic(int index, vec2 uv) {
	float value = u_metallic[index];
	vec3 map = texture(u_metallic_textures, vec3(uv, float(index))).rgb;
	value *= is_custom(index) ? map.r : map.b;
	return value;
}

void vertex() {
	VERTEX = get_transvoxel_position(VERTEX, CUSTOM0);
	v_indices = decode_8bit_vec4(CUSTOM1.x);
	v_weights = decode_8bit_vec4(CUSTOM1.y) / 255.0;
	v_world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
	v_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec4 weights = max(v_weights, vec4(0.0));
	float total =
		weights.x +
		weights.y +
		weights.z +
		weights.w;

	if (total <= 0.00001) {
		weights = vec4(1.0, 0.0, 0.0, 0.0);
	} else {
		weights /= total;
	}

	int i0 = int(v_indices.x + 0.5);
	int i1 = int(v_indices.y + 0.5);
	int i2 = int(v_indices.z + 0.5);
	int i3 = int(v_indices.w + 0.5);
	vec2 uv0 = triplanar_uv(v_world_position, v_world_normal, u_texture_scale[i0]);
	vec2 uv1 = triplanar_uv(v_world_position, v_world_normal, u_texture_scale[i1]);
	vec2 uv2 = triplanar_uv(v_world_position, v_world_normal, u_texture_scale[i2]);
	vec2 uv3 = triplanar_uv(v_world_position, v_world_normal, u_texture_scale[i3]);
	vec3 color =
		material_albedo(i0, uv0) * weights.x +
		material_albedo(i1, uv1) * weights.y +
		material_albedo(i2, uv2) * weights.z +
		material_albedo(i3, uv3) * weights.w;
	float roughness =
		material_roughness(i0, uv0) * weights.x +
		material_roughness(i1, uv1) * weights.y +
		material_roughness(i2, uv2) * weights.z +
		material_roughness(i3, uv3) * weights.w;
	float metallic =
		material_metallic(i0, uv0) * weights.x +
		material_metallic(i1, uv1) * weights.y +
		material_metallic(i2, uv2) * weights.z +
		material_metallic(i3, uv3) * weights.w;
	vec3 normal_map =
		texture(u_normal_textures, vec3(uv0, float(i0))).rgb * weights.x +
		texture(u_normal_textures, vec3(uv1, float(i1))).rgb * weights.y +
		texture(u_normal_textures, vec3(uv2, float(i2))).rgb * weights.z +
		texture(u_normal_textures, vec3(uv3, float(i3))).rgb * weights.w;
	float normal_strength =
		u_normal_strength[i0] * weights.x +
		u_normal_strength[i1] * weights.y +
		u_normal_strength[i2] * weights.z +
		u_normal_strength[i3] * weights.w;

	float up = clamp(v_world_normal.y * 0.5 + 0.5, 0.0, 1.0);
	color *= mix(0.78, 1.05, up);

	ALBEDO = color;
	ROUGHNESS = clamp(roughness, 0.0, 1.0);
	METALLIC = clamp(metallic, 0.0, 1.0);
	NORMAL_MAP = normal_map;
	NORMAL_MAP_DEPTH = normal_strength;
}
""";
	}

	private void ConfigureGenerator()
	{
		EnsureTerrain();

		_generator = InstantiateExtensionClass("VoxelGeneratorFlat");

		// Keep a coherent SDF source, but place its generated ground far
		// below the editable workspace. A height of zero creates the large
		// square platform that previously returned after Clear().
		TrySet(_generator, "height", -100000.0f);
		TrySet(
			_generator,
			"channel",
			GetVoxelConstant("CHANNEL_SDF", 1));

		_voxelTerrain!.Set("generator", Variant.From(_generator));
	}

	private void ConfigureTerrain()
	{
		EnsureTerrain();

		TrySet(
			_voxelTerrain!,
			"generate_collisions",
			GenerateCollisions);
		TrySet(_voxelTerrain!, "collision_layer", (long)CollisionLayer);
		TrySet(_voxelTerrain!, "collision_mask", (long)CollisionMask);
		TrySet(
			_voxelTerrain!,
			"collision_update_delay",
			CollisionUpdateDelay
		);
		TrySet(_voxelTerrain!, "collision_margin", CollisionMargin);
		// Generate colliders at every active terrain LOD. This prevents
		// characters and rigid bodies from falling through during LOD changes.
		TrySet(_voxelTerrain!, "collision_lod_count", 0);
		// Install completed collision meshes during the physics tick.
		TrySet(_voxelTerrain!, "process_callback", 1);

		TrySet(
			_voxelTerrain!,
			"automatic_loading_enabled",
			true);

		TrySet(
			_voxelTerrain!,
			"full_load_mode_enabled",
			true);

		TrySet(_voxelTerrain!, "lod_count", 5);
		TrySet(_voxelTerrain!, "lod_distance", 64.0f);
		TrySet(_voxelTerrain!, "secondary_lod_distance", 64.0f);
		TrySet(_voxelTerrain!, "view_distance", ViewDistance);
		TrySet(_voxelTerrain!, "lod_fade_duration", 0.0f);
	}

	private Color GetTintForMaterial(Part.PartMaterialEnum materialEnum)
	{
		return materialEnum switch
		{
			Part.PartMaterialEnum.Grass => _grassColor,
			Part.PartMaterialEnum.Stone => _stoneColor,
			Part.PartMaterialEnum.Sand => _sandColor,
			Part.PartMaterialEnum.Dirt => _dirtColor,
			Part.PartMaterialEnum.Snow => _snowColor,
			Part.PartMaterialEnum.Concrete => _concreteColor,
			Part.PartMaterialEnum.Brick => _brickColor,
			_ => _baseColor,
		};
	}

	private void SetMaterialTint(ref Color target, Color value, string propertyName)
	{
		if (target == value)
		{
			return;
		}

		target = value;
		UpdateTerrainMaterialParameters();
		OnPropertyChanged(propertyName);
	}

	private void CreateViewer()
	{
		EnsureTerrain();

		GodotObject viewer = InstantiateExtensionClass("VoxelViewer");
		_viewerNode = viewer as Node3D;

		if (_viewerNode == null)
		{
			return;
		}

		_viewerNode.Name = "TerrainViewer";
		_voxelTerrainNode!.AddChild(
			_viewerNode,
			false,
			Node.InternalMode.Front);

		TrySet(_viewerNode, "view_distance", ViewDistance);
		TrySet(_viewerNode, "requires_visuals", true);
		TrySet(
			_viewerNode,
			"requires_collisions",
			GenerateCollisions);
	}

	private void RefreshVoxelTool()
	{
		EnsureTerrain();

		Variant result = _voxelTerrain!.Call("get_voxel_tool");
		_voxelTool = result.AsGodotObject();

		if (_voxelTool == null)
		{
			throw new InvalidOperationException(
				"VoxelLodTerrain.get_voxel_tool() returned null.");
		}

		ConfigureSdfTool(ToolMode.Add);
	}

	#endregion

	#region Operation execution

	private enum ToolMode
	{
		Add = 0,
		Remove = 1,
		Set = 2,
		TexturePaint = 3
	}

	private void ExecuteAndRecord(TerrainOperation operation)
	{
		if (_pendingReplayOperations != null)
		{
			TryReplayPendingOperations();
			if (_pendingReplayOperations != null)
				throw new InvalidOperationException(
					"Terrain is still restoring saved voxel data.");
		}

		ExecuteOperation(operation);

		if (!_isLoading)
		{
			_operations.Add(operation);
			_terrainDirty = true;
		}

		if (AutoSerialise && !_isLoading)
		{
			SaveTerrain();
		}
	}

	private void TryReplayPendingOperations()
	{
		TerrainOperation[]? pending = _pendingReplayOperations;
		if (pending == null) return;

		const int maximumOperationsPerFrame = 256;
		int appliedThisFrame = 0;
		try
		{
			while (_pendingReplayIndex < pending.Length
				&& appliedThisFrame < maximumOperationsPerFrame)
			{
				TerrainOperation operation = pending[_pendingReplayIndex];
				(Vector3 minimum, Vector3 maximum) = GetOperationBounds(operation);
				SetEditorViewerPosition((minimum + maximum) * 0.5f);
				if (!IsAreaEditable(minimum, maximum))
					return;

				ExecuteOperation(operation);
				_pendingReplayIndex++;
				appliedThisFrame++;
			}

			if (_pendingReplayIndex >= pending.Length)
			{
				_pendingReplayOperations = null;
				_pendingReplayIndex = 0;
				BV.Print(
					"Terrain restored ",
					pending.Length,
					" serialized operation(s).");
			}
		}
		catch (Exception exception)
		{
			_pendingReplayOperations = null;
			_pendingReplayIndex = 0;
			GD.PushError($"Failed while replaying saved terrain: {exception}");
		}
	}

	private static (Vector3 Minimum, Vector3 Maximum) GetOperationBounds(
		TerrainOperation operation)
	{
		Vector3 padding;
		switch (operation.Type)
		{
			case TerrainOperationType.FillBlock:
			case TerrainOperationType.DigBlock:
				padding = operation.Size.Abs() * 0.5f + Vector3.One * 2.0f;
				return (operation.Position - padding, operation.Position + padding);

			case TerrainOperationType.FillCylinder:
			case TerrainOperationType.DigCylinder:
				padding = Vector3.One * (operation.Radius + 2.0f);
				return (
					operation.Position.Min(operation.SecondaryPosition) - padding,
					operation.Position.Max(operation.SecondaryPosition) + padding);

			case TerrainOperationType.SetVoxelSdf:
			case TerrainOperationType.SetVoxelMaterial:
				padding = Vector3.One * 2.0f;
				return (operation.Position - padding, operation.Position + padding);

			default:
				padding = Vector3.One * (Math.Max(operation.Radius, 1.0f) + 2.0f);
				return (operation.Position - padding, operation.Position + padding);
		}
	}

	private void ExecuteOperation(TerrainOperation operation)
	{
		EnsureTool();

		switch (operation.Type)
		{
			case TerrainOperationType.FillBall:
				ConfigureSdfTool(ToolMode.Add);
				_voxelTool!.Call(
					"do_sphere",
					operation.Position,
					operation.Radius);
				PaintShape(operation);
				break;

			case TerrainOperationType.DigBall:
				ConfigureSdfTool(ToolMode.Remove);
				_voxelTool!.Call(
					"do_sphere",
					operation.Position,
					operation.Radius);
				break;

			case TerrainOperationType.FillBlock:
				ConfigureSdfTool(ToolMode.Add);
				ExecuteBox(operation.Position, operation.Size);
				PaintShape(operation);
				break;

			case TerrainOperationType.DigBlock:
				ConfigureSdfTool(ToolMode.Remove);
				ExecuteBox(operation.Position, operation.Size);
				break;

			case TerrainOperationType.FillCylinder:
				ConfigureSdfTool(ToolMode.Add);
				ExecutePath(
					operation.Position,
					operation.SecondaryPosition,
					operation.Radius);
				PaintShape(operation);
				break;

			case TerrainOperationType.DigCylinder:
				ConfigureSdfTool(ToolMode.Remove);
				ExecutePath(
					operation.Position,
					operation.SecondaryPosition,
					operation.Radius);
				break;

			case TerrainOperationType.PaintBall:
				ConfigureTexturePaintTool(
					operation.Material,
					operation.Strength);
				_voxelTool!.Call(
					"do_sphere",
					operation.Position,
					operation.Radius);
				break;

			case TerrainOperationType.SmoothBall:
				SetToolChannel("CHANNEL_SDF", 1);
				_voxelTool!.Call(
					"smooth_sphere",
					operation.Position,
					operation.Radius,
					operation.IntegerValue);
				break;

			case TerrainOperationType.GrowBall:
				ConfigureSdfTool(ToolMode.Add);
				_voxelTool!.Call(
					"grow_sphere",
					operation.Position,
					operation.Radius,
					operation.Strength);
				break;

			case TerrainOperationType.ErodeBall:
				ConfigureSdfTool(ToolMode.Remove);
				_voxelTool!.Call(
					"grow_sphere",
					operation.Position,
					operation.Radius,
					operation.Strength);
				break;

			case TerrainOperationType.SetVoxelSdf:
				SetToolChannel("CHANNEL_SDF", 1);
				_voxelTool!.Call(
					"set_voxel_f",
					ToVector3I(operation.Position),
					operation.Strength);
				break;

			case TerrainOperationType.SetVoxelMaterial:
				ConfigureTexturePaintTool(operation.Material, 1.0f);
				_voxelTool!.Call(
					"do_sphere",
					(Vector3)ToVector3I(operation.Position),
					0.75f);
				break;

			default:
				throw new InvalidDataException(
					$"Unknown terrain operation type: {operation.Type}.");
		}
	}

	private void PaintShape(TerrainOperation operation)
	{
		if (operation.Material < 0)
		{
			return;
		}

		ConfigureTexturePaintTool(operation.Material, 1.0f);

		// Texture weights live on voxels surrounding the generated isosurface.
		// Cover a one-voxel shell beyond the SDF brush so newly-added terrain
		// receives valid Mixel4 data before its first mesh is generated.
		const float surfaceShell = 1.5f;
		switch (operation.Type)
		{
			case TerrainOperationType.FillBall:
				_voxelTool!.Call(
					"do_sphere",
					operation.Position,
					operation.Radius + surfaceShell);
				break;

			case TerrainOperationType.FillBlock:
				ExecuteBox(
					operation.Position,
					operation.Size + Vector3.One * surfaceShell * 2.0f
				);
				break;

			case TerrainOperationType.FillCylinder:
				ExecutePath(
					operation.Position,
					operation.SecondaryPosition,
					operation.Radius + surfaceShell);
				break;
		}
	}

	private void ExecuteBox(Vector3 center, Vector3 size)
	{
		Vector3 halfSize = size * 0.5f;

		Vector3I begin = new(
			Mathf.FloorToInt(center.X - halfSize.X),
			Mathf.FloorToInt(center.Y - halfSize.Y),
			Mathf.FloorToInt(center.Z - halfSize.Z));

		Vector3I end = new(
			Mathf.CeilToInt(center.X + halfSize.X),
			Mathf.CeilToInt(center.Y + halfSize.Y),
			Mathf.CeilToInt(center.Z + halfSize.Z));

		_voxelTool!.Call("do_box", begin, end);
	}

	private void ExecutePath(
		Vector3 start,
		Vector3 end,
		float radius)
	{
		// Godot C# marshals managed Vector3[] and float[] to the packed array
		// argument types expected by the GDExtension method.
		Vector3[] points =
		[
			start,
			end
		];

		float[] radii =
		[
			radius,
			radius
		];

		_voxelTool!.Call("do_path", points, radii);
	}

	private void ConfigureSdfTool(ToolMode mode)
	{
		EnsureTool();
		SetToolChannel("CHANNEL_SDF", 1);

		_voxelTool!.Set("mode", (int)mode);
		TrySet(
			_voxelTool,
			"sdf_strength",
			DefaultSdfStrength);
		TrySet(
			_voxelTool,
			"sdf_scale",
			DefaultSdfScale);
	}

	private void ConfigureTexturePaintTool(
		int material,
		float opacity)
	{
		EnsureTool();
		ValidateMaterial(material);

		SetToolChannel("CHANNEL_SDF", 1);

		_voxelTool!.Set(
			"mode",
			(int)ToolMode.TexturePaint);

		_voxelTool.Set("texture_index", material);

		float clampedOpacity =
			Mathf.Clamp(opacity, 0.0f, 1.0f);

		if (!TrySet(
			_voxelTool,
			"texture_opacity",
			clampedOpacity))
		{
			TrySet(
				_voxelTool,
				"set_texture_opacity",
				clampedOpacity);
		}

		TrySet(
			_voxelTool,
			"texture_falloff",
			0.1f);
	}

	private void SetToolChannel(
		string constantName,
		int fallback)
	{
		EnsureTool();

		_voxelTool!.Set(
			"channel",
			GetVoxelConstant(constantName, fallback));
	}

	#endregion

	#region Helpers

	private static GodotObject InstantiateExtensionClass(string className)
	{
		if (!ClassDB.ClassExists(className))
		{
			throw new InvalidOperationException(
				$"Voxel Tools class '{className}' is unavailable. " +
				"Make sure the GDExtension is installed and loaded.");
		}

		Variant result = ClassDB.Instantiate(className);
		GodotObject? instance = result.AsGodotObject();

		return instance ?? throw new InvalidOperationException(
			$"Failed to instantiate Voxel Tools class '{className}'.");
	}

	private static int GetVoxelConstant(
		string constantName,
		int fallback)
	{
		if (!ClassDB.ClassExists("VoxelBuffer"))
		{
			return fallback;
		}

		if (!ClassDB.ClassHasIntegerConstant(
			"VoxelBuffer",
			constantName))
		{
			return fallback;
		}

		return (int)ClassDB.ClassGetIntegerConstant(
			"VoxelBuffer",
			constantName);
	}

	private static bool TrySet(
		GodotObject target,
		StringName property,
		Variant value)
	{
		Godot.Collections.Array<Godot.Collections.Dictionary> properties =
			target.GetPropertyList();

		foreach (Godot.Collections.Dictionary propertyInfo in properties)
		{
			if (!propertyInfo.TryGetValue("name", out Variant propertyName))
			{
				continue;
			}

			if (propertyName.AsStringName() != property)
			{
				continue;
			}

			target.Set(property, value);
			return true;
		}

		return false;
	}

	private static Vector3I ToVector3I(Vector3 value)
	{
		return new Vector3I(
			Mathf.RoundToInt(value.X),
			Mathf.RoundToInt(value.Y),
			Mathf.RoundToInt(value.Z));
	}

	private void EnsureTerrain()
	{
		if (_voxelTerrain == null ||
			_voxelTerrainNode == null ||
			!GodotObject.IsInstanceValid(_voxelTerrainNode))
		{
			throw new InvalidOperationException(
				"Voxel terrain has not been initialized.");
		}
	}

	private void EnsureTool()
	{
		EnsureTerrain();

		if (_voxelTool == null)
		{
			RefreshVoxelTool();
		}
	}

	private static void ValidateRadius(float radius)
	{
		if (!float.IsFinite(radius) || radius <= 0.0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(radius),
				"Radius must be finite and greater than zero.");
		}
	}

	private static void ValidateSize(Vector3 size)
	{
		if (!size.IsFinite() ||
			size.X <= 0.0f ||
			size.Y <= 0.0f ||
			size.Z <= 0.0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(size),
				"Every size component must be finite and greater than zero.");
		}
	}

	private static void ValidateCylinder(
		float height,
		float radius)
	{
		ValidateRadius(radius);

		if (!float.IsFinite(height) || height <= 0.0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(height),
				"Height must be finite and greater than zero.");
		}
	}

	private void ValidateMaterial(int material)
	{
		if (material < 0 || material >= TerrainMaterial.MaximumSlots)
		{
			throw new ArgumentOutOfRangeException(
				nameof(material),
				$"Material slot must be between 0 and {TerrainMaterial.MaximumSlots - 1}.");
		}
		if (!_isLoading && GetMaterial(material) == null)
			throw new InvalidOperationException(
				$"Terrain material slot {material} does not exist under Terrain.");
	}

	private static void ValidateStrength(float strength)
	{
		if (!float.IsFinite(strength) || strength < 0.0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(strength),
				"Strength must be finite and non-negative.");
		}
	}

	#endregion
}

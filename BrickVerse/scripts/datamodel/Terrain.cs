// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

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
	private Node3D? _viewerNode;

	private readonly List<TerrainOperation> _operations = [];

	private bool _initialized;
	private bool _isLoading;
	private bool _isUpdatingSerialisedTerrain;

	private string _serialisedTerrain = string.Empty;
	private bool _autoSerialise = true;
	private bool _generateCollisions = true;
	private float _defaultSdfStrength = 1.0f;
	private float _defaultSdfScale = 1.0f;
	private int _viewDistance = 512;

	/// <summary>
	/// Compressed terrain edit data.
	///
	/// Assigning this value after initialization rebuilds the terrain
	/// immediately. Save this property with the rest of the world data.
	/// </summary>
	[Editable, ScriptProperty, DefaultValue("")]
	public string SerialisedTerrain
	{
		get => _serialisedTerrain;
		set
		{
			value ??= string.Empty;

			if (_serialisedTerrain == value)
			{
				return;
			}

			_serialisedTerrain = value;
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

			OnPropertyChanged();
		}
	}

	internal Node3D? VoxelTerrainNode => _voxelTerrainNode;

	public override void Init()
	{
		CreateVoxelTerrain();
		_initialized = true;

		if (!string.IsNullOrWhiteSpace(_serialisedTerrain))
		{
			LoadSerialisedTerrain();
		}

		base.Init();
	}

	public override void PreDelete()
	{
		_initialized = false;
		_operations.Clear();
		DestroyVoxelTerrain();
		base.PreDelete();
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
		SetToolChannel("CHANNEL_INDICES", 4);

		return _voxelTool!
			.Call("get_voxel", ToVector3I(position))
			.AsInt32();
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

		return _voxelTool
			.Call(
				"is_area_editable",
				ToVector3I(minimum),
				ToVector3I(maximum))
			.AsBool();
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
			OnPropertyChanged(nameof(SerialisedTerrain));
		}
		finally
		{
			_isUpdatingSerialisedTerrain = false;
		}

		return encoded;
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

		try
		{
			_operations.Clear();
			CreateVoxelTerrain();

			if (string.IsNullOrWhiteSpace(_serialisedTerrain))
			{
				return;
			}

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
				ExecuteOperation(operation);
				_operations.Add(operation);
			}
		}
		catch (Exception exception)
		{
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
		_operations.Clear();
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

		_voxelTerrain = InstantiateExtensionClass("VoxelTerrain");
		_voxelTerrainNode = _voxelTerrain as Node3D;

		if (_voxelTerrainNode == null)
		{
			_voxelTerrain = null;

			throw new InvalidOperationException(
				"VoxelTerrain was created but was not a Node3D.");
		}

		_voxelTerrainNode.Name = "VoxelTerrain";
		GDNode.AddChild(
			_voxelTerrainNode,
			false,
			Node.InternalMode.Front);

		ConfigureMesher();
		ConfigureGenerator();
		ConfigureTerrain();
		CreateViewer();
		RefreshVoxelTool();
	}

	private void DestroyVoxelTerrain()
	{
		_voxelTool = null;
		_mesher = null;
		_generator = null;
		_voxelTerrain = null;
		_viewerNode = null;

		if (_voxelTerrainNode != null &&
			GodotObject.IsInstanceValid(_voxelTerrainNode))
		{
			_voxelTerrainNode.QueueFree();
		}

		_voxelTerrainNode = null;
	}

	private void ConfigureMesher()
	{
		EnsureTerrain();

		_mesher = InstantiateExtensionClass("VoxelMesherTransvoxel");

		// Mixel4 is the VoxelTool texture-paint compatible mode.
		TrySet(_mesher, "texturing_mode", 0);
		_voxelTerrain!.Set("mesher", Variant.From(_mesher));
	}

	private void ConfigureGenerator()
	{
		EnsureTerrain();

		_generator = InstantiateExtensionClass("VoxelGeneratorFlat");

		TrySet(_generator, "height", 0.0f);
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

		TrySet(
			_voxelTerrain!,
			"automatic_loading_enabled",
			true);

		TrySet(
			_voxelTerrain!,
			"full_load_mode_enabled",
			true);
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
				"VoxelTerrain.get_voxel_tool() returned null.");
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
		ExecuteOperation(operation);

		if (!_isLoading)
		{
			_operations.Add(operation);
		}

		if (AutoSerialise && !_isLoading)
		{
			SaveTerrain();
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
				SetToolChannel("CHANNEL_INDICES", 4);
				_voxelTool!.Set("mode", (int)ToolMode.Set);
				_voxelTool.Set("value", operation.Material);
				_voxelTool.Call(
					"do_point",
					ToVector3I(operation.Position));
				break;

			default:
				throw new InvalidDataException(
					$"Unknown terrain operation type: {operation.Type}.");
		}
	}

	private void PaintShape(TerrainOperation operation)
	{
		if (operation.Material <= 0)
		{
			return;
		}

		ConfigureTexturePaintTool(operation.Material, 1.0f);

		switch (operation.Type)
		{
			case TerrainOperationType.FillBall:
				_voxelTool!.Call(
					"do_sphere",
					operation.Position,
					operation.Radius);
				break;

			case TerrainOperationType.FillBlock:
				ExecuteBox(operation.Position, operation.Size);
				break;

			case TerrainOperationType.FillCylinder:
				ExecutePath(
					operation.Position,
					operation.SecondaryPosition,
					operation.Radius);
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

		_voxelTool!.Set(
			"mode",
			(int)ToolMode.TexturePaint);

		_voxelTool.Set("texture_index", material);

		// Current Voxel Tools uses set_texture_opacity. Older builds exposed
		// texture_opacity, so support either property.
		if (!TrySet(
			_voxelTool,
			"set_texture_opacity",
			Mathf.Clamp(opacity, 0.0f, 1.0f)))
		{
			TrySet(
				_voxelTool,
				"texture_opacity",
				Mathf.Clamp(opacity, 0.0f, 1.0f));
		}
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

	private static void ValidateMaterial(int material)
	{
		if (material < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(material),
				"Material index cannot be negative.");
		}
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

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A native editable voxel grid that generates render and collision geometry.</summary>
[Instantiable]
public sealed partial class VoxelVolume : Entity
{
	private const int MaximumAxis = 64;
	private const int PaletteSize = 16;
	private static readonly Vector3I[] Directions =
	[
		Vector3I.Right,
		Vector3I.Left,
		Vector3I.Up,
		Vector3I.Down,
		Vector3I.Back,
		Vector3I.Forward,
	];
	private static readonly Vector3[] Normals =
	[
		Vector3.Right,
		Vector3.Left,
		Vector3.Up,
		Vector3.Down,
		Vector3.Back,
		Vector3.Forward,
	];
	private static readonly int[,] FaceCorners =
	{
		{ 1, 5, 7, 3 },
		{ 4, 0, 2, 6 },
		{ 2, 3, 7, 6 },
		{ 4, 5, 1, 0 },
		{ 5, 4, 6, 7 },
		{ 0, 1, 3, 2 },
	};
	private static readonly Vector3[] CubeCorners =
	{
		new(0, 0, 0),
		new(1, 0, 0),
		new(0, 1, 0),
		new(1, 1, 0),
		new(0, 0, 1),
		new(1, 0, 1),
		new(0, 1, 1),
		new(1, 1, 1),
	};

	private Vector3I _dimensions = new(16, 8, 16);
	private float _cellSize = 1f;
	private byte[] _voxelData = new byte[16 * 8 * 16];
	private byte[] _paletteData = CreateDefaultPalette();
	private bool _autoRebuild = true;
	private bool _collisionEnabled = true;
	private int _editDepth;
	private bool _dirty = true;
	private Color _color = Colors.White;
	private bool _castShadows = true;
	private MeshInstance3D _meshInstance = null!;
	private CollisionShape3D _collision = null!;
	private ArrayMesh _generatedMesh = new();
	private StandardMaterial3D _material = null!;

	[Editable, ScriptProperty]
	public Vector3I Dimensions
	{
		get => _dimensions;
		set
		{
			Vector3I next = new(
				Math.Clamp(value.X, 1, MaximumAxis),
				Math.Clamp(value.Y, 1, MaximumAxis),
				Math.Clamp(value.Z, 1, MaximumAxis)
			);
			if (next == _dimensions)
				return;
			Resize(next);
			OnPropertyChanged();
			MarkDirty();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float CellSize
	{
		get => _cellSize;
		set
		{
			float next = Mathf.Clamp(value, 0.05f, 100f);
			if (Mathf.IsEqualApprox(_cellSize, next))
				return;
			_cellSize = next;
			MarkDirty();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool AutoRebuild
	{
		get => _autoRebuild;
		set
		{
			_autoRebuild = value;
			if (value && _dirty && _editDepth == 0)
				Commit();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool CollisionEnabled
	{
		get => _collisionEnabled;
		set
		{
			_collisionEnabled = value;
			if (_collision != null)
				_collision.Disabled = !value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty, SyncVar]
	public byte[] VoxelData
	{
		get => _voxelData;
		set
		{
			int required = CellCount(_dimensions);
			_voxelData = new byte[required];
			if (value != null)
				Array.Copy(value, _voxelData, Math.Min(value.Length, required));
			for (int i = 0; i < _voxelData.Length; i++)
				if (_voxelData[i] >= PaletteSize)
					_voxelData[i] = PaletteSize - 1;
			MarkDirty();
		}
	}

	[ScriptProperty, SyncVar]
	public byte[] PaletteData
	{
		get => _paletteData;
		set
		{
			_paletteData = NormalizePalette(value);
			MarkDirty();
		}
	}

	[ScriptProperty]
	public int FilledCellCount => _voxelData.Count(value => value != 0);

	[ScriptProperty]
	public int TriangleCount { get; private set; }

	[ScriptProperty]
	public BVSignal Changed { get; private set; } = new();

	[ScriptProperty]
	public BVSignal Rebuilt { get; private set; } = new();

	[Editable, ScriptProperty]
	public override Color Color
	{
		get => _color;
		set
		{
			_color = value;
			if (_material != null)
				_material.AlbedoColor = GetVisualColor(value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public override bool CastShadows
	{
		get => _castShadows;
		set
		{
			_castShadows = value;
			if (_meshInstance != null)
				_meshInstance.CastShadow = value
					? GeometryInstance3D.ShadowCastingSetting.On
					: GeometryInstance3D.ShadowCastingSetting.Off;
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		base.Init();
		_meshInstance = new MeshInstance3D
		{
			CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
		};
		_collision = new CollisionShape3D();
		_material = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			Roughness = 0.85f,
			AlbedoColor = GetVisualColor(_color),
		};
		GDNode3D.AddChild(_meshInstance, false, Node.InternalMode.Back);
		GDNode3D.AddChild(_collision, false, Node.InternalMode.Back);
		Commit();
	}

	public override void InitOverrides()
	{
		Anchored = true;
		UseGravity = false;
		base.InitOverrides();
	}

	public override void Ready()
	{
		AddCollisionShape(_collision);
		UpdateCollision();
		base.Ready();
	}

	public override void PreDelete()
	{
		RemoveCollisionShape(_collision, free: false);
		_generatedMesh.Dispose();
		_material.Dispose();
		base.PreDelete();
	}

	internal override (Godot.Mesh Mesh, Transform3D Transform)[] GetBooleanGeometry() =>
		TriangleCount > 0 ? [(_generatedMesh, GDNode3D.GlobalTransform.ScaledLocal(NodeSize))] : [];

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		if (_meshInstance != null)
			_meshInstance.Scale = newSize;
		if (_collision != null)
			_collision.Scale = newSize;
		base.OnNodeSizeChanged(newSize);
	}

	internal override void OnNegatedChanged()
	{
		if (_material != null)
			_material.AlbedoColor = GetVisualColor(_color);
	}

	[ScriptMethod]
	public int GetVoxel(int x, int y, int z) => _voxelData[IndexChecked(x, y, z)];

	[ScriptMethod]
	public void SetVoxel(int x, int y, int z, int paletteIndex)
	{
		if (paletteIndex is < 0 or >= PaletteSize)
			throw new ArgumentOutOfRangeException(nameof(paletteIndex));
		int index = IndexChecked(x, y, z);
		if (_voxelData[index] == paletteIndex)
			return;
		_voxelData[index] = (byte)paletteIndex;
		MarkDirty();
	}

	[ScriptMethod]
	public void FillBox(Vector3I position, Vector3I size, int paletteIndex)
	{
		ValidatePaletteIndex(paletteIndex);
		if (size.X < 0 || size.Y < 0 || size.Z < 0)
			throw new ArgumentOutOfRangeException(nameof(size));
		BeginEdit();
		for (int z = Math.Max(0, position.Z); z < Math.Min(_dimensions.Z, position.Z + size.Z); z++)
			for (int y = Math.Max(0, position.Y); y < Math.Min(_dimensions.Y, position.Y + size.Y); y++)
				for (int x = Math.Max(0, position.X); x < Math.Min(_dimensions.X, position.X + size.X); x++)
					_voxelData[CellIndex(x, y, z)] = (byte)paletteIndex;
		MarkDirty();
		EndEdit();
	}

	[ScriptMethod]
	public void FillSphere(Vector3 center, float radius, int paletteIndex)
	{
		ValidatePaletteIndex(paletteIndex);
		if (radius < 0 || !float.IsFinite(radius))
			throw new ArgumentOutOfRangeException(nameof(radius));
		float squared = radius * radius;
		BeginEdit();
		for (int z = 0; z < _dimensions.Z; z++)
			for (int y = 0; y < _dimensions.Y; y++)
				for (int x = 0; x < _dimensions.X; x++)
					if (new Vector3(x + 0.5f, y + 0.5f, z + 0.5f).DistanceSquaredTo(center) <= squared)
						_voxelData[CellIndex(x, y, z)] = (byte)paletteIndex;
		MarkDirty();
		EndEdit();
	}

	[ScriptMethod]
	public void Clear()
	{
		Array.Clear(_voxelData);
		MarkDirty();
	}

	[ScriptMethod]
	public Color GetPaletteColor(int paletteIndex)
	{
		ValidatePaletteIndex(paletteIndex);
		int i = paletteIndex * 4;
		return new Color(
			_paletteData[i] / 255f,
			_paletteData[i + 1] / 255f,
			_paletteData[i + 2] / 255f,
			_paletteData[i + 3] / 255f
		);
	}

	[ScriptMethod]
	public void SetPaletteColor(int paletteIndex, Color color)
	{
		if (paletteIndex == 0)
			throw new InvalidOperationException("Palette index zero is reserved for empty cells.");
		ValidatePaletteIndex(paletteIndex);
		int i = paletteIndex * 4;
		_paletteData[i] = ToByte(color.R);
		_paletteData[i + 1] = ToByte(color.G);
		_paletteData[i + 2] = ToByte(color.B);
		_paletteData[i + 3] = ToByte(color.A);
		OnPropertyChanged(nameof(PaletteData));
		MarkDirty();
	}

	[ScriptMethod]
	public void BeginEdit() => _editDepth++;

	[ScriptMethod]
	public void EndEdit()
	{
		if (_editDepth == 0)
			throw new InvalidOperationException("EndEdit was called without BeginEdit.");
		_editDepth--;
		if (_editDepth == 0 && _autoRebuild && _dirty)
			Commit();
	}

	[ScriptMethod]
	public void Commit()
	{
		if (_meshInstance == null)
			return;
		SurfaceTool surface = new();
		surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
		List<Vector3> collisionFaces = [];
		int visibleFaces = 0;
		Vector3 centeredOffset = -new Vector3(_dimensions.X, _dimensions.Y, _dimensions.Z) * 0.5f;
		Vector2[] uv = [Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down];

		for (int z = 0; z < _dimensions.Z; z++)
			for (int y = 0; y < _dimensions.Y; y++)
				for (int x = 0; x < _dimensions.X; x++)
				{
					byte palette = _voxelData[CellIndex(x, y, z)];
					if (palette == 0)
						continue;
					Color color = GetPaletteColor(palette);
					Vector3 basePosition = (new Vector3(x, y, z) + centeredOffset) * _cellSize;
					for (int face = 0; face < 6; face++)
					{
						Vector3I neighbor = new(x, y, z);
						neighbor += Directions[face];
						if (
							Inside(neighbor.X, neighbor.Y, neighbor.Z)
							&& _voxelData[CellIndex(neighbor.X, neighbor.Y, neighbor.Z)] != 0
						)
							continue;
						Vector3[] corners = new Vector3[4];
						for (int corner = 0; corner < 4; corner++)
							corners[corner] =
								basePosition + CubeCorners[FaceCorners[face, corner]] * _cellSize;
						AddTriangle(
							surface,
							collisionFaces,
							corners[0],
							corners[1],
							corners[2],
							Normals[face],
							color,
							uv[0],
							uv[1],
							uv[2]
						);
						AddTriangle(
							surface,
							collisionFaces,
							corners[0],
							corners[2],
							corners[3],
							Normals[face],
							color,
							uv[0],
							uv[2],
							uv[3]
						);
						visibleFaces++;
					}
				}

		ArrayMesh nextMesh = surface.Commit();
		if (nextMesh.GetSurfaceCount() > 0)
			nextMesh.SurfaceSetMaterial(0, _material);
		_generatedMesh.Dispose();
		_generatedMesh = nextMesh;
		_meshInstance.Mesh = _generatedMesh;
		ConcavePolygonShape3D shape = new();
		shape.Data = [.. collisionFaces];
		_collision.Shape = shape;
		_collision.Disabled = !_collisionEnabled;
		TriangleCount = visibleFaces * 2;
		_dirty = false;
		Rebuilt.Invoke();
		UpdateNegateHighlight();
	}

	private void Resize(Vector3I next)
	{
		byte[] previous = _voxelData;
		Vector3I old = _dimensions;
		_dimensions = next;
		_voxelData = new byte[CellCount(next)];
		for (int z = 0; z < Math.Min(old.Z, next.Z); z++)
			for (int y = 0; y < Math.Min(old.Y, next.Y); y++)
				for (int x = 0; x < Math.Min(old.X, next.X); x++)
					_voxelData[CellIndex(x, y, z)] = previous[x + old.X * (y + old.Y * z)];
	}

	private void MarkDirty()
	{
		_dirty = true;
		OnPropertyChanged(nameof(VoxelData));
		Changed.Invoke();
		if (_autoRebuild && _editDepth == 0 && _meshInstance != null)
			Commit();
	}

	private int IndexChecked(int x, int y, int z) =>
		Inside(x, y, z)
			? CellIndex(x, y, z)
			: throw new ArgumentOutOfRangeException(
				$"Voxel coordinate ({x}, {y}, {z}) is outside the volume."
			);

	private int CellIndex(int x, int y, int z) => x + _dimensions.X * (y + _dimensions.Y * z);

	private bool Inside(int x, int y, int z) =>
		x >= 0 && y >= 0 && z >= 0 && x < _dimensions.X && y < _dimensions.Y && z < _dimensions.Z;

	private static int CellCount(Vector3I size) => checked(size.X * size.Y * size.Z);

	private static void ValidatePaletteIndex(int value)
	{
		if (value is < 0 or >= PaletteSize)
			throw new ArgumentOutOfRangeException(nameof(value));
	}

	private static byte ToByte(float value) =>
		(byte)Mathf.RoundToInt(Mathf.Clamp(value, 0, 1) * 255);

	private static byte[] NormalizePalette(byte[]? source)
	{
		byte[] result = CreateDefaultPalette();
		if (source != null)
			Array.Copy(source, result, Math.Min(source.Length, result.Length));
		return result;
	}

	private static byte[] CreateDefaultPalette()
	{
		byte[] data = new byte[PaletteSize * 4];
		Color[] colors =
		[
			Colors.Transparent,
			new("d9d9d9"),
			new("808080"),
			new("252525"),
			new("e74c3c"),
			new("f39c12"),
			new("f1c40f"),
			new("2ecc71"),
			new("16a085"),
			new("3498db"),
			new("34495e"),
			new("9b59b6"),
			new("ff8ac4"),
			new("8b5a2b"),
			new("f5e6c8"),
			Colors.White,
		];
		for (int i = 0; i < colors.Length; i++)
		{
			data[i * 4] = ToByte(colors[i].R);
			data[i * 4 + 1] = ToByte(colors[i].G);
			data[i * 4 + 2] = ToByte(colors[i].B);
			data[i * 4 + 3] = ToByte(colors[i].A);
		}
		return data;
	}

	private static void AddTriangle(
		SurfaceTool surface,
		List<Vector3> collision,
		Vector3 a,
		Vector3 b,
		Vector3 c,
		Vector3 normal,
		Color color,
		Vector2 uvA,
		Vector2 uvB,
		Vector2 uvC
	)
	{
		AddVertex(surface, a, normal, color, uvA);
		AddVertex(surface, b, normal, color, uvB);
		AddVertex(surface, c, normal, color, uvC);
		collision.Add(a);
		collision.Add(b);
		collision.Add(c);
	}

	private static void AddVertex(
		SurfaceTool surface,
		Vector3 position,
		Vector3 normal,
		Color color,
		Vector2 uv
	)
	{
		surface.SetNormal(normal);
		surface.SetColor(color);
		surface.SetUV(uv);
		surface.AddVertex(position);
	}
}

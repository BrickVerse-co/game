using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>Liquid material field used by smooth terrain and oceans.</summary>
[Instantiable]
public sealed partial class TerrainWater : Instance
{
	private readonly record struct Exclusion(Vector3 Center, Vector3 Size);

	private const int MaximumExclusions = 32,
		MaximumWaterCells = 500_000;
	private readonly List<Exclusion> _exclusions = [];
	private readonly HashSet<Vector3I> _cells = [];
	private readonly Dictionary<Vector2I, int> _columnTops = [];
	private MeshInstance3D? _oceanRenderer,
		_voxelRenderer;
	private ShaderMaterial? _material;
	private string _serialisedExclusions = "",
		_serialisedVoxels = "";
	private Vector2 _size = new(2048, 2048),
		_waveDirection = new(1, .25f);
	private float _cellSize = 4,
		_waterLevel,
		_waveHeight = .45f,
		_waveLength = 18,
		_waveSpeed = 1.4f,
		_waveSteepness = .3f;
	private float _shorelineWidth = 3.5f,
		_transparency = .72f,
		_roughness = .12f,
		_refractionStrength = .035f,
		_normalStrength = .7f,
		_textureScale = .035f,
		_foamAmount = .65f;
	private Color _shallowColor = new("32a8c7"),
		_deepColor = new("07577d"),
		_foamColor = new("dffcff");
	private bool _enabled = true,
		_oceanEnabled;
	private int _editDepth;
	private bool _voxelEditDirty;

	[Editable, ScriptProperty, SyncVar, DefaultValue(true)]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			_enabled = value;
			RefreshVisibility();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar, DefaultValue(false)]
	public bool OceanEnabled
	{
		get => _oceanEnabled;
		set
		{
			_oceanEnabled = value;
			RefreshVisibility();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Vector2 Size
	{
		get => _size;
		set
		{
			_size = new(
				Mathf.Clamp(Mathf.Abs(value.X), 8, 32768),
				Mathf.Clamp(Mathf.Abs(value.Y), 8, 32768)
			);
			RebuildOcean();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float CellSize
	{
		get => _cellSize;
		set
		{
			float next = Mathf.Clamp(Finite(value, 4), 1, 32);
			if (Mathf.IsEqualApprox(next, _cellSize))
				return;
			List<Vector3> centers = [];
			foreach (Vector3I cell in _cells)
				centers.Add(CellCenter(cell));
			_cellSize = next;
			_cells.Clear();
			foreach (Vector3 center in centers)
				_cells.Add(WorldToCell(center));
			RebuildColumns();
			EncodeVoxels();
			RebuildVoxelMesh();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float WaterLevel
	{
		get => _waterLevel;
		set
		{
			_waterLevel = Finite(value, 0);
			if (_oceanRenderer != null)
				_oceanRenderer.Position = new(0, _waterLevel, 0);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float WaveHeight
	{
		get => _waveHeight;
		set
		{
			_waveHeight = Mathf.Clamp(Finite(value, .45f), 0, 128);
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float WaveLength
	{
		get => _waveLength;
		set
		{
			_waveLength = Mathf.Clamp(Finite(value, 18), .25f, 2048);
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float WaveSpeed
	{
		get => _waveSpeed;
		set
		{
			_waveSpeed = Mathf.Clamp(Finite(value, 1.4f), -50, 50);
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float WaveSteepness
	{
		get => _waveSteepness;
		set
		{
			_waveSteepness = Mathf.Clamp(Finite(value, .3f), 0, 1);
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Vector2 WaveDirection
	{
		get => _waveDirection;
		set
		{
			_waveDirection = value.LengthSquared() > .0001f ? value.Normalized() : Vector2.Right;
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float ShorelineWidth
	{
		get => _shorelineWidth;
		set
		{
			_shorelineWidth = Mathf.Clamp(Finite(value, 3.5f), .05f, 100);
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float Transparency
	{
		get => _transparency;
		set
		{
			_transparency = Mathf.Clamp(Finite(value, .72f), .05f, 1);
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float Roughness
	{
		get => _roughness;
		set
		{
			_roughness = Mathf.Clamp(Finite(value, .12f), 0, 1);
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float RefractionStrength
	{
		get => _refractionStrength;
		set
		{
			_refractionStrength = Mathf.Clamp(Finite(value, .035f), 0, .25f);
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float NormalStrength
	{
		get => _normalStrength;
		set
		{
			_normalStrength = Mathf.Clamp(Finite(value, .7f), 0, 4);
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float TextureScale
	{
		get => _textureScale;
		set
		{
			_textureScale = Mathf.Clamp(Finite(value, .035f), .001f, 2);
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float FoamAmount
	{
		get => _foamAmount;
		set
		{
			_foamAmount = Mathf.Clamp(Finite(value, .65f), 0, 2);
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color ShallowColor
	{
		get => _shallowColor;
		set
		{
			_shallowColor = value;
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color DeepColor
	{
		get => _deepColor;
		set
		{
			_deepColor = value;
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color FoamColor
	{
		get => _foamColor;
		set
		{
			_foamColor = value;
			UpdateShader();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar, DefaultValue("")]
	public string SerialisedVoxels
	{
		get => _serialisedVoxels;
		set
		{
			_serialisedVoxels = value ?? "";
			DecodeVoxels();
			RebuildVoxelMesh();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar, DefaultValue("")]
	public string SerialisedExclusions
	{
		get => _serialisedExclusions;
		set
		{
			_serialisedExclusions = value ?? "";
			DecodeExclusions();
			UpdateExclusions();
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		base.Init();
		Name = "TerrainWater";
		_material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
		_oceanRenderer = CreateRenderer("OceanSurface");
		_voxelRenderer = CreateRenderer("TerrainWaterSurface");
		RebuildOcean();
		RebuildVoxelMesh();
		UpdateShader();
		UpdateExclusions();
		RefreshVisibility();
		WaterLevel = _waterLevel;
	}

	private MeshInstance3D CreateRenderer(string name)
	{
		MeshInstance3D r = new()
		{
			Name = name,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		GDNode.AddChild(r, false, Node.InternalMode.Back);
		return r;
	}

	[ScriptMethod]
	public void SetWave(
		Vector2 direction,
		float height,
		float length,
		float speed,
		float steepness = .3f
	)
	{
		WaveDirection = direction;
		WaveHeight = height;
		WaveLength = length;
		WaveSpeed = speed;
		WaveSteepness = steepness;
	}

	[ScriptMethod]
	public void FillWaterBlock(Vector3 center, Vector3 size) => EditBlock(center, size, true);

	[ScriptMethod]
	public void DrainWaterBlock(Vector3 center, Vector3 size) => EditBlock(center, size, false);

	[ScriptMethod]
	public void FillWaterBall(Vector3 center, float radius) => EditBall(center, radius, true);

	[ScriptMethod]
	public void DrainWaterBall(Vector3 center, float radius) => EditBall(center, radius, false);

	[ScriptMethod]
	public void FillWaterCylinder(Vector3 center, float height, float radius) =>
		EditCylinder(center, height, radius, true);

	[ScriptMethod]
	public void DrainWaterCylinder(Vector3 center, float height, float radius) =>
		EditCylinder(center, height, radius, false);

	[ScriptMethod]
	public void ClearVoxelWater()
	{
		_cells.Clear();
		CommitVoxelEdit();
	}

	[ScriptMethod]
	public int GetWaterCellCount() => _cells.Count;

	[ScriptMethod]
	public bool HasVoxelWater(Vector3 position) => _cells.Contains(WorldToCell(position));

	public void BeginVoxelEdit() => _editDepth++;

	public void EndVoxelEdit()
	{
		if (_editDepth > 0)
			_editDepth--;
		if (_editDepth == 0 && _voxelEditDirty)
		{
			_voxelEditDirty = false;
			CommitVoxelEdit();
		}
	}

	internal bool TryRaycast(
		Vector3 origin,
		Vector3 direction,
		float maxDistance,
		out Vector3 position,
		out Vector3 normal
	)
	{
		direction = direction.Normalized();
		float step = Mathf.Max(_cellSize * .35f, .25f);
		Vector3I previous = WorldToCell(origin);
		for (float distance = 0; distance <= maxDistance; distance += step)
		{
			Vector3 point = origin + direction * distance;
			Vector3I cell = WorldToCell(point);
			if (_cells.Contains(cell))
			{
				position = point;
				Vector3I delta = previous - cell;
				normal = delta == Vector3I.Zero ? -direction : ((Vector3)delta).Normalized();
				return true;
			}
			previous = cell;
		}
		if (_oceanEnabled && Mathf.Abs(direction.Y) > .0001f)
		{
			float distance = (_waterLevel - origin.Y) / direction.Y;
			Vector3 point = origin + direction * distance;
			if (
				distance >= 0
				&& distance <= maxDistance
				&& Mathf.Abs(point.X) <= _size.X * .5f
				&& Mathf.Abs(point.Z) <= _size.Y * .5f
			)
			{
				position = point;
				normal = Vector3.Up;
				return true;
			}
		}
		position = default;
		normal = Vector3.Up;
		return false;
	}

	private void EditBlock(Vector3 center, Vector3 size, bool fill)
	{
		Vector3 half = new(Mathf.Abs(size.X), Mathf.Abs(size.Y), Mathf.Abs(size.Z));
		half *= .5f;
		EditCells(WorldToCell(center - half), WorldToCell(center + half), _ => true, fill);
	}

	private void EditBall(Vector3 center, float radius, bool fill)
	{
		radius = Mathf.Clamp(Finite(radius, 1), .1f, 2048);
		Vector3I min = WorldToCell(center - Vector3.One * radius),
			max = WorldToCell(center + Vector3.One * radius);
		float r2 = radius * radius;
		EditCells(min, max, c => CellCenter(c).DistanceSquaredTo(center) <= r2, fill);
	}

	private void EditCylinder(Vector3 center, float height, float radius, bool fill)
	{
		height = Mathf.Clamp(Finite(height, 1), .1f, 4096);
		radius = Mathf.Clamp(Finite(radius, 1), .1f, 2048);
		Vector3 half = new(radius, height * .5f, radius);
		Vector3I min = WorldToCell(center - half),
			max = WorldToCell(center + half);
		float r2 = radius * radius;
		EditCells(
			min,
			max,
			c =>
			{
				Vector3 p = CellCenter(c);
				float x = p.X - center.X,
					z = p.Z - center.Z;
				return x * x + z * z <= r2;
			},
			fill
		);
	}

	private void EditCells(Vector3I min, Vector3I max, Func<Vector3I, bool> include, bool fill)
	{
		long volume = (long)(max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);
		if (volume > MaximumWaterCells)
			throw new InvalidOperationException(
				"Water edit is too large. Increase CellSize or use OceanEnabled for oceans."
			);
		for (int y = min.Y; y <= max.Y; y++)
		for (int z = min.Z; z <= max.Z; z++)
		for (int x = min.X; x <= max.X; x++)
		{
			Vector3I c = new(x, y, z);
			if (!include(c))
				continue;
			if (fill)
			{
				if (_cells.Count >= MaximumWaterCells)
					throw new InvalidOperationException(
						$"TerrainWater supports {MaximumWaterCells:N0} cells."
					);
				_cells.Add(c);
			}
			else
				_cells.Remove(c);
		}
		CommitVoxelEdit();
	}

	private void CommitVoxelEdit()
	{
		if (_editDepth > 0)
		{
			_voxelEditDirty = true;
			return;
		}
		RebuildColumns();
		EncodeVoxels();
		RebuildVoxelMesh();
	}

	[ScriptMethod]
	public float GetWaterHeight(Vector3 p, float time = -1)
	{
		if (IsExcluded(p))
			return float.NegativeInfinity;
		float height = float.NegativeInfinity;
		Vector3I c = WorldToCell(p);
		if (_columnTops.TryGetValue(new(c.X, c.Z), out int top))
			height = (top + 1) * _cellSize;
		if (_oceanEnabled && Mathf.Abs(p.X) <= _size.X * .5f && Mathf.Abs(p.Z) <= _size.Y * .5f)
			height = Mathf.Max(height, _waterLevel);
		if (!float.IsFinite(height))
			return height;
		float t = time >= 0 ? time : (float)Time.GetTicksMsec() / 1000f;
		Vector2 q = new(p.X, p.Z);
		return height
			+ Wave(q, _waveDirection, t, 1)
			+ Wave(q, _waveDirection.Rotated(.83f), t, .42f)
			+ Wave(q, _waveDirection.Rotated(-1.17f), t, .22f);
	}

	[ScriptMethod]
	public Vector3 GetWaterNormal(Vector3 p, float time = -1)
	{
		float e = Mathf.Max(.05f, _waveLength * .002f),
			h = GetWaterHeight(p, time);
		if (!float.IsFinite(h))
			return Vector3.Up;
		float hx = GetWaterHeight(p + Vector3.Right * e, time),
			hz = GetWaterHeight(p + Vector3.Back * e, time);
		if (!float.IsFinite(hx))
			hx = h;
		if (!float.IsFinite(hz))
			hz = h;
		return new Vector3(h - hx, e, h - hz).Normalized();
	}

	[ScriptMethod]
	public bool IsSubmerged(Vector3 p)
	{
		if (IsExcluded(p))
			return false;
		if (_cells.Contains(WorldToCell(p)))
			return true;
		return _oceanEnabled && p.Y < GetWaterHeight(p);
	}

	[ScriptMethod]
	public int AddExclusionBox(Vector3 center, Vector3 size, float padding = 0)
	{
		if (_exclusions.Count >= MaximumExclusions)
			throw new InvalidOperationException(
				$"TerrainWater supports {MaximumExclusions} exclusion boxes."
			);
		Vector3 safe = new(
			Mathf.Max(.05f, Mathf.Abs(size.X) + padding * 2),
			Mathf.Max(.05f, Mathf.Abs(size.Y) + padding * 2),
			Mathf.Max(.05f, Mathf.Abs(size.Z) + padding * 2)
		);
		_exclusions.Add(new(center, safe));
		EncodeExclusions();
		UpdateExclusions();
		return _exclusions.Count;
	}

	[ScriptMethod]
	public bool RemoveExclusion(int id)
	{
		int i = id - 1;
		if (i < 0 || i >= _exclusions.Count)
			return false;
		_exclusions.RemoveAt(i);
		EncodeExclusions();
		UpdateExclusions();
		return true;
	}

	[ScriptMethod]
	public bool RemoveExclusionAt(Vector3 p)
	{
		for (int i = _exclusions.Count - 1; i >= 0; i--)
		{
			Exclusion a = _exclusions[i];
			Vector3 h = a.Size * .5f;
			if (
				Mathf.Abs(p.X - a.Center.X) > h.X
				|| Mathf.Abs(p.Y - a.Center.Y) > h.Y
				|| Mathf.Abs(p.Z - a.Center.Z) > h.Z
			)
				continue;
			_exclusions.RemoveAt(i);
			EncodeExclusions();
			UpdateExclusions();
			return true;
		}
		return false;
	}

	[ScriptMethod]
	public void ClearExclusions()
	{
		_exclusions.Clear();
		EncodeExclusions();
		UpdateExclusions();
	}

	[ScriptMethod]
	public int GetExclusionCount() => _exclusions.Count;

	private Vector3I WorldToCell(Vector3 p) =>
		new(
			Mathf.FloorToInt(p.X / _cellSize),
			Mathf.FloorToInt(p.Y / _cellSize),
			Mathf.FloorToInt(p.Z / _cellSize)
		);

	private Vector3 CellCenter(Vector3I c) =>
		(Vector3)c * _cellSize + Vector3.One * (_cellSize * .5f);

	private float Wave(Vector2 p, Vector2 d, float t, float scale) =>
		Mathf.Sin(p.Dot(d) * (Mathf.Tau / _waveLength) + t * _waveSpeed / Mathf.Max(scale, .1f))
		* _waveHeight
		* scale;

	private bool IsExcluded(Vector3 p)
	{
		foreach (Exclusion a in _exclusions)
		{
			Vector3 h = a.Size * .5f;
			if (
				Mathf.Abs(p.X - a.Center.X) <= h.X
				&& Mathf.Abs(p.Y - a.Center.Y) <= h.Y
				&& Mathf.Abs(p.Z - a.Center.Z) <= h.Z
			)
				return true;
		}
		return false;
	}

	private void RebuildOcean()
	{
		if (_oceanRenderer == null)
			return;
		_oceanRenderer.Mesh = new PlaneMesh
		{
			Size = _size,
			SubdivideWidth = 255,
			SubdivideDepth = 255,
			Material = _material,
		};
	}

	private void RebuildVoxelMesh()
	{
		if (_voxelRenderer == null)
			return;
		SurfaceTool st = new();
		st.Begin(Godot.Mesh.PrimitiveType.Triangles);
		// Render the liquid boundary as one continuous height field. Occupancy is
		// only storage; individual cells must never be visible to creators.
		foreach ((Vector2I column, int top) in _columnTops)
		{
			float fallback = (top + 1) * _cellSize;
			Vector3 a = new(column.X * _cellSize, CornerHeight(column.X, column.Y, fallback), column.Y * _cellSize);
			Vector3 b = new((column.X + 1) * _cellSize, CornerHeight(column.X + 1, column.Y, fallback), column.Y * _cellSize);
			Vector3 c = new((column.X + 1) * _cellSize, CornerHeight(column.X + 1, column.Y + 1, fallback), (column.Y + 1) * _cellSize);
			Vector3 d = new(column.X * _cellSize, CornerHeight(column.X, column.Y + 1, fallback), (column.Y + 1) * _cellSize);
			AddQuad(st, a, b, c, d, Vector3.Up, fallback, true);
		}
		ArrayMesh mesh = st.Commit();
		if (mesh.GetSurfaceCount() > 0)
			mesh.SurfaceSetMaterial(0, _material);
		_voxelRenderer.Mesh = mesh;
		RefreshVisibility();
	}

	private float CornerHeight(int x, int z, float fallback)
	{
		float sum = 0;
		int count = 0;
		for (int dz = -1; dz <= 0; dz++)
		for (int dx = -1; dx <= 0; dx++)
			if (_columnTops.TryGetValue(new Vector2I(x + dx, z + dz), out int top))
			{
				sum += (top + 1) * _cellSize;
				count++;
			}
		return count == 0 ? fallback : sum / count;
	}

	private void AddCellFace(SurfaceTool st, Vector3I cell, Vector3I d)
	{
		Vector3 c = CellCenter(cell),
			h = Vector3.One * (_cellSize * .5f),
			n = d,
			u = d.Y != 0 ? Vector3.Right * h.X : Vector3.Up * h.Y,
			v = d.X != 0
				? Vector3.Back * h.Z
				: (d.Z != 0 ? Vector3.Right * h.X : Vector3.Back * h.Z),
			face = c + new Vector3(d.X * h.X, d.Y * h.Y, d.Z * h.Z);
		if (u.Cross(v).Dot(n) < 0)
			v = -v;
		AddQuad(
			st,
			face - u - v,
			face + u - v,
			face + u + v,
			face - u + v,
			n,
			c.Y + h.Y,
			!_cells.Contains(cell + Vector3I.Up)
		);
	}

	private static void AddQuad(
		SurfaceTool st,
		Vector3 a,
		Vector3 b,
		Vector3 c,
		Vector3 d,
		Vector3 n,
		float topY,
		bool surfaceCell
	)
	{
		AddVertex(st, a, n, new(0, 0), WaveWeight(a, n, topY, surfaceCell));
		AddVertex(st, b, n, new(1, 0), WaveWeight(b, n, topY, surfaceCell));
		AddVertex(st, c, n, new(1, 1), WaveWeight(c, n, topY, surfaceCell));
		AddVertex(st, a, n, new(0, 0), WaveWeight(a, n, topY, surfaceCell));
		AddVertex(st, c, n, new(1, 1), WaveWeight(c, n, topY, surfaceCell));
		AddVertex(st, d, n, new(0, 1), WaveWeight(d, n, topY, surfaceCell));
	}

	private static float WaveWeight(Vector3 p, Vector3 n, float topY, bool surfaceCell) =>
		n.Y > .5f || (surfaceCell && Mathf.Abs(p.Y - topY) < .01f) ? 1 : 0;

	private static void AddVertex(
		SurfaceTool st,
		Vector3 p,
		Vector3 n,
		Vector2 uv,
		float waveWeight
	)
	{
		st.SetNormal(n);
		st.SetUV(uv);
		st.SetColor(new Color(waveWeight, 0, 0, 1));
		st.AddVertex(p);
	}

	private void RebuildColumns()
	{
		_columnTops.Clear();
		foreach (Vector3I c in _cells)
		{
			Vector2I key = new(c.X, c.Z);
			if (!_columnTops.TryGetValue(key, out int top) || c.Y > top)
				_columnTops[key] = c.Y;
		}
	}

	private void RefreshVisibility()
	{
		if (_oceanRenderer != null)
			_oceanRenderer.Visible = _enabled && _oceanEnabled;
		if (_voxelRenderer != null)
			_voxelRenderer.Visible = _enabled && _cells.Count > 0;
	}

	private void UpdateShader()
	{
		if (_material == null)
			return;
		_material.SetShaderParameter("wave_height", _waveHeight);
		_material.SetShaderParameter("wave_length", _waveLength);
		_material.SetShaderParameter("wave_speed", _waveSpeed);
		_material.SetShaderParameter("wave_steepness", _waveSteepness);
		_material.SetShaderParameter("wave_direction", _waveDirection);
		_material.SetShaderParameter("shoreline_width", _shorelineWidth);
		_material.SetShaderParameter("transparency", _transparency);
		_material.SetShaderParameter("roughness", _roughness);
		_material.SetShaderParameter("refraction_strength", _refractionStrength);
		_material.SetShaderParameter("normal_strength", _normalStrength);
		_material.SetShaderParameter("texture_scale", _textureScale);
		_material.SetShaderParameter("foam_amount", _foamAmount);
		_material.SetShaderParameter("shallow_color", _shallowColor);
		_material.SetShaderParameter("deep_color", _deepColor);
		_material.SetShaderParameter("foam_color", _foamColor);
	}

	private void UpdateExclusions()
	{
		if (_material == null)
			return;
		_material.SetShaderParameter("exclusion_count", _exclusions.Count);
		for (int i = 0; i < MaximumExclusions; i++)
		{
			Vector4 p =
				i < _exclusions.Count
					? new(
						_exclusions[i].Center.X,
						_exclusions[i].Center.Z,
						_exclusions[i].Size.X * .5f,
						_exclusions[i].Size.Z * .5f
					)
					: Vector4.Zero;
			Vector2 h =
				i < _exclusions.Count
					? new(_exclusions[i].Center.Y, _exclusions[i].Size.Y * .5f)
					: Vector2.Zero;
			_material.SetShaderParameter($"exclusion_{i}", p);
			_material.SetShaderParameter($"exclusion_height_{i}", h);
		}
	}

	private void EncodeVoxels()
	{
		using MemoryStream raw = new();
		using (BinaryWriter w = new(raw, System.Text.Encoding.UTF8, true))
		{
			w.Write(1);
			w.Write(_cells.Count);
			foreach (Vector3I c in _cells)
			{
				w.Write(c.X);
				w.Write(c.Y);
				w.Write(c.Z);
			}
		}
		using MemoryStream packed = new();
		using (DeflateStream zip = new(packed, CompressionLevel.Fastest, true))
			zip.Write(raw.ToArray());
		_serialisedVoxels = Convert.ToBase64String(packed.ToArray());
		OnPropertyChanged(nameof(SerialisedVoxels));
	}

	private void DecodeVoxels()
	{
		_cells.Clear();
		if (string.IsNullOrWhiteSpace(_serialisedVoxels))
		{
			RebuildColumns();
			return;
		}
		try
		{
			using MemoryStream packed = new(Convert.FromBase64String(_serialisedVoxels));
			using DeflateStream zip = new(packed, CompressionMode.Decompress);
			using MemoryStream raw = new();
			zip.CopyTo(raw);
			raw.Position = 0;
			using BinaryReader r = new(raw);
			_ = r.ReadInt32();
			int count = r.ReadInt32();
			if (count < 0 || count > MaximumWaterCells || raw.Length < 8L + count * 12L)
				throw new InvalidDataException("Invalid voxel water data.");
			for (int i = 0; i < count; i++)
				_cells.Add(new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
		}
		catch
		{
			_cells.Clear();
		}
		RebuildColumns();
	}

	private void EncodeExclusions()
	{
		using MemoryStream s = new();
		using BinaryWriter w = new(s);
		w.Write(_exclusions.Count);
		foreach (Exclusion a in _exclusions)
		{
			w.Write(a.Center.X);
			w.Write(a.Center.Y);
			w.Write(a.Center.Z);
			w.Write(a.Size.X);
			w.Write(a.Size.Y);
			w.Write(a.Size.Z);
		}
		_serialisedExclusions = Convert.ToBase64String(s.ToArray());
		OnPropertyChanged(nameof(SerialisedExclusions));
	}

	private void DecodeExclusions()
	{
		_exclusions.Clear();
		if (string.IsNullOrWhiteSpace(_serialisedExclusions))
			return;
		try
		{
			using MemoryStream s = new(Convert.FromBase64String(_serialisedExclusions));
			using BinaryReader r = new(s);
			int count = Math.Clamp(r.ReadInt32(), 0, MaximumExclusions);
			for (int i = 0; i < count; i++)
				_exclusions.Add(
					new(
						new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
						new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle())
					)
				);
		}
		catch
		{
			_exclusions.Clear();
		}
	}

	private static float Finite(float value, float fallback) =>
		float.IsFinite(value) ? value : fallback;

	private static readonly string ShaderCode = BuildShader();

	private static string BuildShader()
	{
		string uniforms = "",
			checks = "";
		for (int i = 0; i < MaximumExclusions; i++)
		{
			uniforms += $"uniform vec4 exclusion_{i};uniform vec2 exclusion_height_{i};\n";
			checks +=
				$"if(exclusion_count>{i}&&all(lessThanEqual(abs(world.xz-exclusion_{i}.xy),exclusion_{i}.zw))&&abs(world.y-exclusion_height_{i}.x)<=exclusion_height_{i}.y)discard;\n";
		}
		return "shader_type spatial;render_mode blend_mix,depth_draw_always,cull_disabled;\n"
			+ uniforms
			+ @"
uniform int exclusion_count=0;uniform float wave_height=.45,wave_length=18.,wave_speed=1.4,wave_steepness=.3,shoreline_width=3.5,transparency=.72,roughness=.12,refraction_strength=.035,normal_strength=.7,texture_scale=.035,foam_amount=.65;uniform vec2 wave_direction=vec2(1.,.25);uniform vec4 shallow_color:source_color=vec4(.2,.66,.78,1.),deep_color:source_color=vec4(.03,.34,.49,1.),foam_color:source_color=vec4(.87,.99,1.,1.);uniform sampler2D depth_texture:hint_depth_texture,filter_nearest;uniform sampler2D screen_texture:hint_screen_texture,filter_linear_mipmap;varying vec3 world;varying float crest;varying float top_face;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}float noise(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.-2.*f);return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),f.x),f.y);}float wave(vec2 p,vec2 d,float s){return sin(dot(p,d)*6.283185/max(wave_length,.01)+TIME*wave_speed/max(s,.1))*wave_height*s;}
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.)).xyz;top_face=max(step(.55,NORMAL.y),COLOR.r);vec2 d=normalize(wave_direction),q=vec2(-d.y,d.x);float h=wave(world.xz,d,1.)+wave(world.xz,normalize(d+q*.7),.42)+wave(world.xz,normalize(d-q*1.1),.22);VERTEX.y+=h*top_face;VERTEX.xz+=d*h*wave_steepness*top_face;crest=h/max(wave_height,.001);world=(MODEL_MATRIX*vec4(VERTEX,1.)).xyz;}
void fragment(){"
			+ checks
			+ @"vec2 p=world.xz*texture_scale;float n1=noise(p+TIME*wave_speed*.08),n2=noise(p*2.13-TIME*wave_speed*.05);vec2 slope=vec2(dFdx(n1+n2*.5),dFdy(n1+n2*.5))*normal_strength;float raw=texture(depth_texture,SCREEN_UV).r;vec4 view=INV_PROJECTION_MATRIX*vec4(SCREEN_UV*2.-1.,raw,1.);view.xyz/=view.w;float depth=max(-view.z-VERTEX.z,0.);float shore=1.-smoothstep(0.,shoreline_width,depth);float foam=clamp(max(shore,smoothstep(.58,1.,crest))*foam_amount,0.,1.);vec3 refracted=textureLod(screen_texture,SCREEN_UV+slope*refraction_strength,roughness*4.).rgb;vec3 water=mix(shallow_color.rgb,deep_color.rgb,smoothstep(0.,shoreline_width*5.,depth));ALBEDO=mix(mix(refracted,water,.62),foam_color.rgb,foam*.82);ROUGHNESS=mix(roughness,.72,foam);METALLIC=.03;ALPHA=mix(transparency,1.,foam*.6);NORMAL=normalize(NORMAL+vec3(slope.x,0.,slope.y)*top_face);}";
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel;

/// <summary>
/// Runtime-editable mesh data with stable IDs
///
/// Logical vertices are stored once, while render vertices are generated per
/// face corner so each face can have independent normals, UVs and colors.
/// Call Commit() after a batch of edits, or enable AutoCommit.
/// </summary>
public sealed partial class EditableMesh : RefCounted, IScriptObject
{
	private sealed class VertexData
	{
		public Vector3 Position;
		public readonly List<int> BoneIds = [];
		public readonly List<float> BoneWeights = [];
	}

	private sealed class FaceData
	{
		public readonly int[] Vertices = new int[3];
		public readonly int[] Normals = new int[3];
		public readonly int[] UVs = new int[3];
		public readonly int[] Colors = new int[3];
	}

	private sealed class NormalData
	{
		public Vector3 Value;
		public bool Automatic;
	}

	private sealed class ColorData
	{
		public Color Value;
	}

	private sealed class BoneData
	{
		public string Name = "";
		public int ParentId;
		public Transform3D Transform = Transform3D.Identity;
		public bool Virtual;
	}

	private readonly Dictionary<int, VertexData> _vertices = [];
	private readonly Dictionary<int, FaceData> _faces = [];
	private readonly Dictionary<int, NormalData> _normals = [];
	private readonly Dictionary<int, Vector2> _uvs = [];
	private readonly Dictionary<int, ColorData> _colors = [];
	private readonly Dictionary<int, BoneData> _bones = [];
	private readonly List<WeakReference<MeshInstance3D>> _boundInstances = [];

	private int _nextVertexId = 1;
	private int _nextFaceId = 1;
	private int _nextNormalId = 1;
	private int _nextUvId = 1;
	private int _nextColorId = 1;
	private int _nextBoneId = 1;

	private bool _dirty = true;
	private bool _destroyed;
	private ArrayMesh? _committedMesh;
	private Material? _material;

	[ScriptProperty]
	public bool FixedSize { get; private set; }

	[ScriptProperty]
	public bool AutoCommit { get; set; } = true;

	[ScriptProperty]
	public int Version { get; private set; }

	[ScriptProperty]
	public int VertexCount => _vertices.Count;

	[ScriptProperty]
	public int FaceCount => _faces.Count;

	[ScriptProperty]
	public int BoneCount => _bones.Count;

	public EditableMesh(bool fixedSize = false)
	{
		FixedSize = fixedSize;
	}

	/// <summary>Builds welded topology in one pass for generated geometry.</summary>
	internal static EditableMesh FromTriangleSoup(Vector3[] triangles)
	{
		EditableMesh mesh = new() { AutoCommit = false };
		Dictionary<Vector3, int> vertexIds = [];
		Dictionary<int, Vector3> normalSums = [];
		Dictionary<int, int> normalIds = [], uvIds = [], colorIds = [];
		for (int i = 0; i < triangles.Length; i++)
		{
			Vector3 position = triangles[i];
			if (vertexIds.ContainsKey(position)) continue;
			int id = mesh._nextVertexId++;
			vertexIds[position] = id;
			mesh._vertices[id] = new VertexData { Position = position };
			normalSums[id] = Vector3.Zero;
			uvIds[id] = mesh.AddUvInternal(Vector2.Zero);
			colorIds[id] = mesh.AddColorInternal(Colors.White);
		}
		for (int i = 0; i + 2 < triangles.Length; i += 3)
		{
			int a = vertexIds[triangles[i]], b = vertexIds[triangles[i + 1]], c = vertexIds[triangles[i + 2]];
			if (a == b || b == c || c == a) continue;
			Vector3 normal = (triangles[i + 1] - triangles[i]).Cross(triangles[i + 2] - triangles[i]).Normalized();
			normalSums[a] += normal; normalSums[b] += normal; normalSums[c] += normal;
			FaceData face = new(); face.Vertices[0] = a; face.Vertices[1] = b; face.Vertices[2] = c;
			mesh._faces[mesh._nextFaceId++] = face;
		}
		foreach ((int vertexId, Vector3 sum) in normalSums) normalIds[vertexId] = mesh.AddNormalInternal(sum.Normalized(), automatic: true);
		foreach (FaceData face in mesh._faces.Values) for (int corner = 0; corner < 3; corner++)
			{
				int vertexId = face.Vertices[corner]; face.Normals[corner] = normalIds[vertexId]; face.UVs[corner] = uvIds[vertexId]; face.Colors[corner] = colorIds[vertexId];
			}
		mesh.MarkDirty(); mesh.Commit(); return mesh;
	}

	/// <summary>
	/// Optional material used for the generated surface.
	/// This is intentionally not a ScriptProperty because Material is a Godot resource.
	/// </summary>
	public Material? Material
	{
		get => _material;
		set
		{
			ThrowIfDestroyed();
			_material = value;
			MarkDirty();
		}
	}

	[ScriptMethod]
	public int AddVertex(Vector3 position)
	{
		RequireTopologyEditable();
		int id = _nextVertexId++;
		_vertices[id] = new VertexData { Position = position };
		MarkDirty();
		return id;
	}

	[ScriptMethod]
	public int AddTriangle(int vertexId0, int vertexId1, int vertexId2)
	{
		RequireTopologyEditable();
		RequireVertex(vertexId0);
		RequireVertex(vertexId1);
		RequireVertex(vertexId2);

		if (vertexId0 == vertexId1 || vertexId1 == vertexId2 || vertexId2 == vertexId0)
		{
			throw new ArgumentException("A triangle must contain three different vertex IDs.");
		}

		int faceId = _nextFaceId++;
		FaceData face = new();
		face.Vertices[0] = vertexId0;
		face.Vertices[1] = vertexId1;
		face.Vertices[2] = vertexId2;

		Vector3 faceNormal = CalculateFaceNormal(face);
		for (int corner = 0; corner < 3; corner++)
		{
			int vertexId = face.Vertices[corner];
			face.Normals[corner] = FindReusableCornerNormal(vertexId) ?? AddNormalInternal(faceNormal, automatic: true);
			face.UVs[corner] = FindReusableCornerUv(vertexId) ?? AddUvInternal(Vector2.Zero);
			face.Colors[corner] = FindReusableCornerColor(vertexId) ?? AddColorInternal(Colors.White);
		}

		_faces[faceId] = face;
		RecalculateAutomaticNormals();
		MarkDirty();
		return faceId;
	}

	[ScriptMethod]
	public int AddNormal(Vector3 normal)
	{
		ThrowIfDestroyed();
		return AddNormalInternal(normal, automatic: false);
	}

	/// <summary>Adds an automatically calculated normal.</summary>
	[ScriptMethod]
	public int AddAutomaticNormal()
	{
		ThrowIfDestroyed();
		return AddNormalInternal(Vector3.Up, automatic: true);
	}

	[ScriptMethod]
	public int AddUV(Vector2 uv)
	{
		ThrowIfDestroyed();
		return AddUvInternal(uv);
	}

	[ScriptMethod]
	public int AddColor(Color color, float alpha = 1.0f)
	{
		ThrowIfDestroyed();
		color.A = Mathf.Clamp(alpha, 0f, 1f);
		return AddColorInternal(color);
	}

	[ScriptMethod]
	public int AddBone(string name, int parentId = 0, Transform3D transform = default, bool isVirtual = false)
	{
		RequireTopologyEditable();

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("Bone name cannot be empty.", nameof(name));
		}
		if (_bones.Values.Any(b => string.Equals(b.Name, name, StringComparison.Ordinal)))
		{
			throw new ArgumentException($"A bone named '{name}' already exists.", nameof(name));
		}
		if (parentId != 0)
		{
			RequireBone(parentId);
		}

		int id = _nextBoneId++;
		_bones[id] = new BoneData
		{
			Name = name,
			ParentId = parentId,
			Transform = transform == default ? Transform3D.Identity : transform,
			Virtual = isVirtual,
		};
		MarkDirty();
		return id;
	}

	[ScriptMethod]
	public void RemoveFace(int faceId)
	{
		RequireTopologyEditable();
		if (!_faces.Remove(faceId))
		{
			throw new KeyNotFoundException($"Face ID {faceId} does not exist.");
		}
		RecalculateAutomaticNormals();
		MarkDirty();
	}

	[ScriptMethod]
	public void RemoveBone(int boneId)
	{
		RequireTopologyEditable();
		RequireBone(boneId);

		foreach (BoneData child in _bones.Values)
		{
			if (child.ParentId == boneId)
			{
				child.ParentId = 0;
			}
		}

		foreach (VertexData vertex in _vertices.Values)
		{
			for (int i = vertex.BoneIds.Count - 1; i >= 0; i--)
			{
				if (vertex.BoneIds[i] == boneId)
				{
					vertex.BoneIds.RemoveAt(i);
					vertex.BoneWeights.RemoveAt(i);
				}
			}
			NormalizeBoneWeights(vertex);
		}

		_bones.Remove(boneId);
		MarkDirty();
	}

	[ScriptMethod]
	public void SetPosition(int vertexId, Vector3 position)
	{
		VertexData vertex = RequireVertex(vertexId);
		vertex.Position = position;
		RecalculateAutomaticNormals();
		MarkDirty();
	}

	[ScriptMethod]
	public Vector3 GetPosition(int vertexId) => RequireVertex(vertexId).Position;

	[ScriptMethod]
	public void SetNormal(int normalId, Vector3 normal)
	{
		NormalData data = RequireNormal(normalId);
		data.Value = SafeNormal(normal);
		data.Automatic = false;
		MarkDirty();
	}

	[ScriptMethod]
	public Vector3 GetNormal(int normalId) => RequireNormal(normalId).Value;

	[ScriptMethod]
	public void ResetNormal(int normalId)
	{
		NormalData data = RequireNormal(normalId);
		data.Automatic = true;
		RecalculateAutomaticNormals();
		MarkDirty();
	}

	[ScriptMethod]
	public void SetUV(int uvId, Vector2 uv)
	{
		RequireUv(uvId);
		_uvs[uvId] = uv;
		MarkDirty();
	}

	[ScriptMethod]
	public Vector2 GetUV(int uvId)
	{
		RequireUv(uvId);
		return _uvs[uvId];
	}

	[ScriptMethod]
	public void SetColor(int colorId, Color color)
	{
		RequireColor(colorId).Value = color;
		MarkDirty();
	}

	[ScriptMethod]
	public Color GetColor(int colorId) => RequireColor(colorId).Value;

	[ScriptMethod]
	public void SetColorAlpha(int colorId, float alpha)
	{
		ColorData data = RequireColor(colorId);
		Color color = data.Value;
		color.A = Mathf.Clamp(alpha, 0f, 1f);
		data.Value = color;
		MarkDirty();
	}

	[ScriptMethod]
	public float GetColorAlpha(int colorId) => RequireColor(colorId).Value.A;

	[ScriptMethod]
	public int[] GetVertices() => [.. _vertices.Keys.Order()];

	[ScriptMethod]
	public int[] GetFaces() => [.. _faces.Keys.Order()];

	[ScriptMethod]
	public int[] GetNormals() => [.. _normals.Keys.Order()];

	[ScriptMethod]
	public int[] GetUVs() => [.. _uvs.Keys.Order()];

	[ScriptMethod]
	public int[] GetColors() => [.. _colors.Keys.Order()];

	[ScriptMethod]
	public int[] GetBones() => [.. _bones.Keys.Order()];

	[ScriptMethod]
	public int[] GetFaceVertices(int faceId) => [.. RequireFace(faceId).Vertices];

	[ScriptMethod]
	public int[] GetFaceNormals(int faceId) => [.. RequireFace(faceId).Normals];

	[ScriptMethod]
	public int[] GetFaceUVs(int faceId) => [.. RequireFace(faceId).UVs];

	[ScriptMethod]
	public int[] GetFaceColors(int faceId) => [.. RequireFace(faceId).Colors];

	[ScriptMethod]
	public void SetFaceVertices(int faceId, int[] vertexIds)
	{
		FaceData face = RequireFace(faceId);
		RequireThreeIds(vertexIds, nameof(vertexIds));
		foreach (int id in vertexIds)
		{
			RequireVertex(id);
		}
		if (vertexIds.Distinct().Count() != 3)
		{
			throw new ArgumentException("A triangle must contain three different vertex IDs.", nameof(vertexIds));
		}

		Array.Copy(vertexIds, face.Vertices, 3);
		RecalculateAutomaticNormals();
		MarkDirty();
	}

	[ScriptMethod]
	public void SetFaceNormals(int faceId, int[] normalIds)
	{
		FaceData face = RequireFace(faceId);
		RequireThreeIds(normalIds, nameof(normalIds));
		foreach (int id in normalIds)
		{
			RequireNormal(id);
		}
		Array.Copy(normalIds, face.Normals, 3);
		MarkDirty();
	}

	[ScriptMethod]
	public void SetFaceUVs(int faceId, int[] uvIds)
	{
		FaceData face = RequireFace(faceId);
		RequireThreeIds(uvIds, nameof(uvIds));
		foreach (int id in uvIds)
		{
			RequireUv(id);
		}
		Array.Copy(uvIds, face.UVs, 3);
		MarkDirty();
	}

	[ScriptMethod]
	public void SetFaceColors(int faceId, int[] colorIds)
	{
		FaceData face = RequireFace(faceId);
		RequireThreeIds(colorIds, nameof(colorIds));
		foreach (int id in colorIds)
		{
			RequireColor(id);
		}
		Array.Copy(colorIds, face.Colors, 3);
		MarkDirty();
	}

	[ScriptMethod]
	public int GetVertexFaceNormal(int vertexId, int faceId)
	{
		return GetCornerAttribute(vertexId, faceId, RequireFace(faceId).Normals);
	}

	[ScriptMethod]
	public int GetVertexFaceUV(int vertexId, int faceId)
	{
		return GetCornerAttribute(vertexId, faceId, RequireFace(faceId).UVs);
	}

	[ScriptMethod]
	public int GetVertexFaceColor(int vertexId, int faceId)
	{
		return GetCornerAttribute(vertexId, faceId, RequireFace(faceId).Colors);
	}

	[ScriptMethod]
	public void SetVertexFaceNormal(int vertexId, int faceId, int normalId)
	{
		RequireNormal(normalId);
		SetCornerAttribute(vertexId, faceId, RequireFace(faceId).Normals, normalId);
		MarkDirty();
	}

	[ScriptMethod]
	public void SetVertexFaceUV(int vertexId, int faceId, int uvId)
	{
		RequireUv(uvId);
		SetCornerAttribute(vertexId, faceId, RequireFace(faceId).UVs, uvId);
		MarkDirty();
	}

	[ScriptMethod]
	public void SetVertexFaceColor(int vertexId, int faceId, int colorId)
	{
		RequireColor(colorId);
		SetCornerAttribute(vertexId, faceId, RequireFace(faceId).Colors, colorId);
		MarkDirty();
	}

	[ScriptMethod]
	public int[] GetVertexFaces(int vertexId)
	{
		RequireVertex(vertexId);
		return [.. _faces
			.Where(pair => Array.IndexOf(pair.Value.Vertices, vertexId) >= 0)
			.Select(pair => pair.Key)
			.Order()];
	}

	[ScriptMethod]
	public int[] GetAdjacentVertices(int vertexId)
	{
		RequireVertex(vertexId);
		HashSet<int> result = [];
		foreach (FaceData face in _faces.Values)
		{
			if (Array.IndexOf(face.Vertices, vertexId) < 0)
			{
				continue;
			}
			foreach (int other in face.Vertices)
			{
				if (other != vertexId)
				{
					result.Add(other);
				}
			}
		}
		return [.. result.Order()];
	}

	[ScriptMethod]
	public int[] GetAdjacentFaces(int faceId)
	{
		FaceData target = RequireFace(faceId);
		HashSet<int> vertices = [.. target.Vertices];
		return [.. _faces
			.Where(pair => pair.Key != faceId && pair.Value.Vertices.Count(vertices.Contains) >= 2)
			.Select(pair => pair.Key)
			.Order()];
	}

	[ScriptMethod]
	public int FindClosestVertex(Vector3 point)
	{
		ThrowIfDestroyed();
		if (_vertices.Count == 0)
		{
			return 0;
		}

		int closestId = 0;
		float closestDistanceSquared = float.PositiveInfinity;
		foreach ((int id, VertexData vertex) in _vertices)
		{
			float distanceSquared = vertex.Position.DistanceSquaredTo(point);
			if (distanceSquared < closestDistanceSquared)
			{
				closestDistanceSquared = distanceSquared;
				closestId = id;
			}
		}
		return closestId;
	}

	[ScriptMethod]
	public int[] FindVerticesWithinSphere(Vector3 center, float radius)
	{
		ThrowIfDestroyed();
		if (radius < 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(radius));
		}
		float radiusSquared = radius * radius;
		return [.. _vertices
			.Where(pair => pair.Value.Position.DistanceSquaredTo(center) <= radiusSquared)
			.Select(pair => pair.Key)
			.Order()];
	}

	[ScriptMethod]
	public Vector3 GetCenter()
	{
		ThrowIfDestroyed();
		if (_vertices.Count == 0)
		{
			return Vector3.Zero;
		}

		Vector3 center = Vector3.Zero;
		foreach (VertexData vertex in _vertices.Values)
		{
			center += vertex.Position;
		}
		return center / _vertices.Count;
	}

	[ScriptMethod]
	public Vector3 GetSize()
	{
		ThrowIfDestroyed();
		if (_vertices.Count == 0)
		{
			return Vector3.Zero;
		}

		Vector3 min = _vertices.Values.First().Position;
		Vector3 max = min;
		foreach (VertexData vertex in _vertices.Values)
		{
			min = min.Min(vertex.Position);
			max = max.Max(vertex.Position);
		}
		return max - min;
	}

	[ScriptMethod]
	public void SetVertexBones(int vertexId, int[] boneIds)
	{
		VertexData vertex = RequireVertex(vertexId);
		if (boneIds.Length > 4)
		{
			throw new ArgumentException("Godot supports at most four bone influences per rendered vertex.", nameof(boneIds));
		}
		foreach (int boneId in boneIds)
		{
			RequireBone(boneId);
		}

		vertex.BoneIds.Clear();
		vertex.BoneIds.AddRange(boneIds);
		while (vertex.BoneWeights.Count > boneIds.Length)
		{
			vertex.BoneWeights.RemoveAt(vertex.BoneWeights.Count - 1);
		}
		while (vertex.BoneWeights.Count < boneIds.Length)
		{
			vertex.BoneWeights.Add(1f);
		}
		NormalizeBoneWeights(vertex);
		MarkDirty();
	}

	[ScriptMethod]
	public int[] GetVertexBones(int vertexId) => [.. RequireVertex(vertexId).BoneIds];

	[ScriptMethod]
	public void SetVertexBoneWeights(int vertexId, float[] weights)
	{
		VertexData vertex = RequireVertex(vertexId);
		if (weights.Length != vertex.BoneIds.Count)
		{
			throw new ArgumentException("Bone weight count must match the vertex bone ID count.", nameof(weights));
		}
		if (weights.Any(weight => weight < 0f))
		{
			throw new ArgumentException("Bone weights cannot be negative.", nameof(weights));
		}

		vertex.BoneWeights.Clear();
		vertex.BoneWeights.AddRange(weights);
		NormalizeBoneWeights(vertex);
		MarkDirty();
	}

	[ScriptMethod]
	public float[] GetVertexBoneWeights(int vertexId) => [.. RequireVertex(vertexId).BoneWeights];

	[ScriptMethod]
	public string GetBoneName(int boneId) => RequireBone(boneId).Name;

	[ScriptMethod]
	public void SetBoneName(int boneId, string name)
	{
		BoneData bone = RequireBone(boneId);
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("Bone name cannot be empty.", nameof(name));
		}
		if (_bones.Any(pair => pair.Key != boneId && pair.Value.Name == name))
		{
			throw new ArgumentException($"A bone named '{name}' already exists.", nameof(name));
		}
		bone.Name = name;
		MarkDirty();
	}

	[ScriptMethod]
	public int GetBoneByName(string name)
	{
		foreach ((int id, BoneData bone) in _bones)
		{
			if (bone.Name == name)
			{
				return id;
			}
		}
		return 0;
	}

	[ScriptMethod]
	public int GetBoneParent(int boneId) => RequireBone(boneId).ParentId;

	[ScriptMethod]
	public void SetBoneParent(int boneId, int parentBoneId)
	{
		BoneData bone = RequireBone(boneId);
		if (parentBoneId != 0)
		{
			RequireBone(parentBoneId);
		}
		if (parentBoneId == boneId || IsBoneDescendant(parentBoneId, boneId))
		{
			throw new InvalidOperationException("Bone parenting would create a cycle.");
		}
		bone.ParentId = parentBoneId;
		MarkDirty();
	}

	[ScriptMethod]
	public Transform3D GetBoneTransform(int boneId) => RequireBone(boneId).Transform;

	[ScriptMethod]
	public void SetBoneTransform(int boneId, Transform3D transform)
	{
		RequireBone(boneId).Transform = transform;
		MarkDirty();
	}

	[ScriptMethod]
	public bool GetBoneIsVirtual(int boneId) => RequireBone(boneId).Virtual;

	[ScriptMethod]
	public void SetBoneIsVirtual(int boneId, bool isVirtual)
	{
		RequireBone(boneId).Virtual = isVirtual;
		MarkDirty();
	}

	/// <summary>
	/// Merges vertices within tolerance and returns old/new ID pairs:
	/// [oldId0, newId0, oldId1, newId1, ...].
	/// </summary>
	[ScriptMethod]
	public int[] MergeVertices(float mergeTolerance)
	{
		RequireTopologyEditable();
		if (mergeTolerance < 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(mergeTolerance));
		}

		float toleranceSquared = mergeTolerance * mergeTolerance;
		Dictionary<int, int> remap = [];
		List<int> ids = [.. _vertices.Keys.Order()];

		for (int i = 0; i < ids.Count; i++)
		{
			int sourceId = ids[i];
			if (remap.ContainsKey(sourceId))
			{
				continue;
			}

			remap[sourceId] = sourceId;
			for (int j = i + 1; j < ids.Count; j++)
			{
				int candidateId = ids[j];
				if (remap.ContainsKey(candidateId))
				{
					continue;
				}
				if (_vertices[sourceId].Position.DistanceSquaredTo(_vertices[candidateId].Position) <= toleranceSquared)
				{
					remap[candidateId] = sourceId;
				}
			}
		}

		foreach (FaceData face in _faces.Values)
		{
			for (int corner = 0; corner < 3; corner++)
			{
				face.Vertices[corner] = remap[face.Vertices[corner]];
			}
		}

		foreach ((int oldId, int newId) in remap)
		{
			if (oldId != newId)
			{
				_vertices.Remove(oldId);
			}
		}

		foreach (int degenerateFaceId in _faces
			.Where(pair => pair.Value.Vertices.Distinct().Count() != 3)
			.Select(pair => pair.Key)
			.ToArray())
		{
			_faces.Remove(degenerateFaceId);
		}

		RemoveUnused();
		RecalculateAutomaticNormals();
		MarkDirty();

		List<int> flattened = [];
		foreach ((int oldId, int newId) in remap.OrderBy(pair => pair.Key))
		{
			flattened.Add(oldId);
			flattened.Add(newId);
		}
		return [.. flattened];
	}

	/// <summary>
	/// Removes unreferenced vertices and corner attributes.
	/// Returns removed IDs as [vertex..., -1, normal..., -1, uv..., -1, color...].
	/// </summary>
	[ScriptMethod]
	public int[] RemoveUnused()
	{
		RequireTopologyEditable();

		HashSet<int> usedVertices = [];
		HashSet<int> usedNormals = [];
		HashSet<int> usedUvs = [];
		HashSet<int> usedColors = [];

		foreach (FaceData face in _faces.Values)
		{
			usedVertices.UnionWith(face.Vertices);
			usedNormals.UnionWith(face.Normals);
			usedUvs.UnionWith(face.UVs);
			usedColors.UnionWith(face.Colors);
		}

		int[] removedVertices = RemoveUnusedKeys(_vertices, usedVertices);
		int[] removedNormals = RemoveUnusedKeys(_normals, usedNormals);
		int[] removedUvs = RemoveUnusedKeys(_uvs, usedUvs);
		int[] removedColors = RemoveUnusedKeys(_colors, usedColors);

		List<int> result = [];
		result.AddRange(removedVertices);
		result.Add(-1);
		result.AddRange(removedNormals);
		result.Add(-1);
		result.AddRange(removedUvs);
		result.Add(-1);
		result.AddRange(removedColors);

		MarkDirty();
		return [.. result];
	}

	/// <summary>
	/// Faces created by this API are already triangles. This method validates
	/// imported/internal topology and removes invalid degenerate triangles.
	/// </summary>
	[ScriptMethod]
	public void Triangulate()
	{
		ThrowIfDestroyed();
		if (FixedSize)
		{
			return;
		}

		foreach (int faceId in _faces
			.Where(pair =>
				pair.Value.Vertices.Distinct().Count() != 3 ||
				pair.Value.Vertices.Any(vertexId => !_vertices.ContainsKey(vertexId)))
			.Select(pair => pair.Key)
			.ToArray())
		{
			_faces.Remove(faceId);
		}

		RecalculateAutomaticNormals();
		MarkDirty();
	}

	/// <summary>
	/// Builds the Godot mesh immediately. Bound MeshInstance3D nodes are updated.
	/// </summary>
	public ArrayMesh Commit()
	{
		ThrowIfDestroyed();

		ArrayMesh mesh = new();
		if (_faces.Count > 0)
		{
			List<Vector3> positions = [];
			List<Vector3> normals = [];
			List<Vector2> uvs = [];
			List<Color> colors = [];
			List<int> indices = [];
			List<int> bones = [];
			List<float> weights = [];

			foreach ((_, FaceData face) in _faces.OrderBy(pair => pair.Key))
			{
				for (int corner = 0; corner < 3; corner++)
				{
					VertexData vertex = RequireVertex(face.Vertices[corner]);
					positions.Add(vertex.Position);
					normals.Add(RequireNormal(face.Normals[corner]).Value);
					uvs.Add(_uvs[face.UVs[corner]]);
					colors.Add(RequireColor(face.Colors[corner]).Value);
					indices.Add(indices.Count);

					for (int influence = 0; influence < 4; influence++)
					{
						bones.Add(influence < vertex.BoneIds.Count
							? GetGodotBoneIndex(vertex.BoneIds[influence])
							: 0);
						weights.Add(influence < vertex.BoneWeights.Count
							? vertex.BoneWeights[influence]
							: 0f);
					}
				}
			}

			Godot.Collections.Array arrays = [];
			arrays.Resize((int)Godot.Mesh.ArrayType.Max);
			arrays[(int)Godot.Mesh.ArrayType.Vertex] = positions.ToArray();
			arrays[(int)Godot.Mesh.ArrayType.Normal] = normals.ToArray();
			arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs.ToArray();
			arrays[(int)Godot.Mesh.ArrayType.Color] = colors.ToArray();
			arrays[(int)Godot.Mesh.ArrayType.Index] = indices.ToArray();

			if (_bones.Count > 0)
			{
				arrays[(int)Godot.Mesh.ArrayType.Bones] = bones.ToArray();
				arrays[(int)Godot.Mesh.ArrayType.Weights] = weights.ToArray();
			}

			mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
			if (_material != null)
			{
				mesh.SurfaceSetMaterial(0, _material);
			}
		}

		_committedMesh = mesh;
		_dirty = false;
		Version++;
		UpdateBoundInstances(mesh);
		return mesh;
	}

	public ArrayMesh GetArrayMesh()
	{
		ThrowIfDestroyed();
		return _dirty || _committedMesh == null ? Commit() : _committedMesh;
	}

	public void BindTo(MeshInstance3D meshInstance)
	{
		ThrowIfDestroyed();
		ArgumentNullException.ThrowIfNull(meshInstance);

		if (!_boundInstances.Any(reference =>
			reference.TryGetTarget(out MeshInstance3D? existing) && existing == meshInstance))
		{
			_boundInstances.Add(new WeakReference<MeshInstance3D>(meshInstance));
		}
		meshInstance.Mesh = GetArrayMesh();
	}

	public void UnbindFrom(MeshInstance3D meshInstance)
	{
		for (int i = _boundInstances.Count - 1; i >= 0; i--)
		{
			if (!_boundInstances[i].TryGetTarget(out MeshInstance3D? existing) || existing == meshInstance)
			{
				_boundInstances.RemoveAt(i);
			}
		}
	}

	[ScriptMethod]
	public string IdDebugString(int id)
	{
		List<string> kinds = [];
		if (_vertices.ContainsKey(id)) kinds.Add("Vertex");
		if (_faces.ContainsKey(id)) kinds.Add("Face");
		if (_normals.ContainsKey(id)) kinds.Add("Normal");
		if (_uvs.ContainsKey(id)) kinds.Add("UV");
		if (_colors.ContainsKey(id)) kinds.Add("Color");
		if (_bones.ContainsKey(id)) kinds.Add("Bone");
		return kinds.Count == 0 ? $"Unknown({id})" : $"{string.Join("/", kinds)}({id})";
	}

	[ScriptMethod]
	public void Destroy()
	{
		if (_destroyed)
		{
			return;
		}

		foreach (WeakReference<MeshInstance3D> reference in _boundInstances)
		{
			if (reference.TryGetTarget(out MeshInstance3D? instance) &&
				GodotObject.IsInstanceValid(instance))
			{
				instance.Mesh = null;
			}
		}

		_boundInstances.Clear();
		_vertices.Clear();
		_faces.Clear();
		_normals.Clear();
		_uvs.Clear();
		_colors.Clear();
		_bones.Clear();
		_committedMesh?.Dispose();
		_committedMesh = null;
		_destroyed = true;
		Dispose();
	}

	private void MarkDirty()
	{
		_dirty = true;
		if (AutoCommit && _boundInstances.Count > 0)
		{
			Commit();
		}
	}

	private int AddNormalInternal(Vector3 normal, bool automatic)
	{
		int id = _nextNormalId++;
		_normals[id] = new NormalData
		{
			Value = SafeNormal(normal),
			Automatic = automatic,
		};
		MarkDirty();
		return id;
	}

	private int AddUvInternal(Vector2 uv)
	{
		int id = _nextUvId++;
		_uvs[id] = uv;
		MarkDirty();
		return id;
	}

	private int AddColorInternal(Color color)
	{
		int id = _nextColorId++;
		_colors[id] = new ColorData { Value = color };
		MarkDirty();
		return id;
	}

	private int? FindReusableCornerNormal(int vertexId)
	{
		foreach (FaceData face in _faces.Values)
		{
			int corner = Array.IndexOf(face.Vertices, vertexId);
			if (corner >= 0)
			{
				return face.Normals[corner];
			}
		}
		return null;
	}

	private int? FindReusableCornerUv(int vertexId)
	{
		foreach (FaceData face in _faces.Values)
		{
			int corner = Array.IndexOf(face.Vertices, vertexId);
			if (corner >= 0)
			{
				return face.UVs[corner];
			}
		}
		return null;
	}

	private int? FindReusableCornerColor(int vertexId)
	{
		foreach (FaceData face in _faces.Values)
		{
			int corner = Array.IndexOf(face.Vertices, vertexId);
			if (corner >= 0)
			{
				return face.Colors[corner];
			}
		}
		return null;
	}

	private void RecalculateAutomaticNormals()
	{
		Dictionary<int, Vector3> accumulated = [];

		foreach (FaceData face in _faces.Values)
		{
			Vector3 normal = CalculateFaceNormal(face);
			for (int corner = 0; corner < 3; corner++)
			{
				int normalId = face.Normals[corner];
				if (!RequireNormal(normalId).Automatic)
				{
					continue;
				}
				accumulated[normalId] = accumulated.GetValueOrDefault(normalId) + normal;
			}
		}

		foreach ((int id, Vector3 value) in accumulated)
		{
			_normals[id].Value = SafeNormal(value);
		}
	}

	private Vector3 CalculateFaceNormal(FaceData face)
	{
		Vector3 a = RequireVertex(face.Vertices[0]).Position;
		Vector3 b = RequireVertex(face.Vertices[1]).Position;
		Vector3 c = RequireVertex(face.Vertices[2]).Position;
		return SafeNormal((b - a).Cross(c - a));
	}

	private static Vector3 SafeNormal(Vector3 normal)
	{
		return normal.LengthSquared() <= 0.0000001f ? Vector3.Up : normal.Normalized();
	}

	private int GetGodotBoneIndex(int boneId)
	{
		int index = 0;
		foreach (int id in _bones.Keys.Order())
		{
			if (id == boneId)
			{
				return index;
			}
			index++;
		}
		return 0;
	}

	private void UpdateBoundInstances(ArrayMesh mesh)
	{
		for (int i = _boundInstances.Count - 1; i >= 0; i--)
		{
			if (!_boundInstances[i].TryGetTarget(out MeshInstance3D? instance) ||
				!GodotObject.IsInstanceValid(instance))
			{
				_boundInstances.RemoveAt(i);
				continue;
			}
			instance.Mesh = mesh;
		}
	}

	private static void NormalizeBoneWeights(VertexData vertex)
	{
		if (vertex.BoneWeights.Count == 0)
		{
			return;
		}

		float total = vertex.BoneWeights.Sum();
		if (total <= 0.000001f)
		{
			float equal = 1f / vertex.BoneWeights.Count;
			for (int i = 0; i < vertex.BoneWeights.Count; i++)
			{
				vertex.BoneWeights[i] = equal;
			}
			return;
		}

		for (int i = 0; i < vertex.BoneWeights.Count; i++)
		{
			vertex.BoneWeights[i] /= total;
		}
	}

	private bool IsBoneDescendant(int candidateId, int ancestorId)
	{
		int current = candidateId;
		HashSet<int> visited = [];
		while (current != 0 && visited.Add(current))
		{
			if (current == ancestorId)
			{
				return true;
			}
			current = RequireBone(current).ParentId;
		}
		return false;
	}

	private int GetCornerAttribute(int vertexId, int faceId, int[] attributes)
	{
		RequireVertex(vertexId);
		FaceData face = RequireFace(faceId);
		int corner = Array.IndexOf(face.Vertices, vertexId);
		if (corner < 0)
		{
			throw new ArgumentException($"Vertex {vertexId} is not part of face {faceId}.");
		}
		return attributes[corner];
	}

	private void SetCornerAttribute(int vertexId, int faceId, int[] attributes, int attributeId)
	{
		RequireVertex(vertexId);
		FaceData face = RequireFace(faceId);
		int corner = Array.IndexOf(face.Vertices, vertexId);
		if (corner < 0)
		{
			throw new ArgumentException($"Vertex {vertexId} is not part of face {faceId}.");
		}
		attributes[corner] = attributeId;
	}

	private static void RequireThreeIds(int[] ids, string parameterName)
	{
		if (ids == null || ids.Length != 3)
		{
			throw new ArgumentException("Exactly three IDs are required.", parameterName);
		}
	}

	private static int[] RemoveUnusedKeys<T>(Dictionary<int, T> dictionary, HashSet<int> used)
	{
		int[] removed = [.. dictionary.Keys.Where(id => !used.Contains(id)).Order()];
		foreach (int id in removed)
		{
			dictionary.Remove(id);
		}
		return removed;
	}

	private VertexData RequireVertex(int id)
	{
		ThrowIfDestroyed();
		return _vertices.TryGetValue(id, out VertexData? value)
			? value
			: throw new KeyNotFoundException($"Vertex ID {id} does not exist.");
	}

	private FaceData RequireFace(int id)
	{
		ThrowIfDestroyed();
		return _faces.TryGetValue(id, out FaceData? value)
			? value
			: throw new KeyNotFoundException($"Face ID {id} does not exist.");
	}

	private NormalData RequireNormal(int id)
	{
		ThrowIfDestroyed();
		return _normals.TryGetValue(id, out NormalData? value)
			? value
			: throw new KeyNotFoundException($"Normal ID {id} does not exist.");
	}

	private void RequireUv(int id)
	{
		ThrowIfDestroyed();
		if (!_uvs.ContainsKey(id))
		{
			throw new KeyNotFoundException($"UV ID {id} does not exist.");
		}
	}

	private ColorData RequireColor(int id)
	{
		ThrowIfDestroyed();
		return _colors.TryGetValue(id, out ColorData? value)
			? value
			: throw new KeyNotFoundException($"Color ID {id} does not exist.");
	}

	private BoneData RequireBone(int id)
	{
		ThrowIfDestroyed();
		return _bones.TryGetValue(id, out BoneData? value)
			? value
			: throw new KeyNotFoundException($"Bone ID {id} does not exist.");
	}

	private void RequireTopologyEditable()
	{
		ThrowIfDestroyed();
		if (FixedSize)
		{
			throw new InvalidOperationException(
				"This EditableMesh is fixed-size. Vertex, face and bone topology cannot be changed.");
		}
	}

	private void ThrowIfDestroyed()
	{
		ObjectDisposedException.ThrowIf(_destroyed, this);
	}
}

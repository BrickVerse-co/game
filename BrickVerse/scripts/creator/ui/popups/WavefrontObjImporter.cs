using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>Runtime Wavefront OBJ importer for files outside res://.</summary>
internal static class WavefrontObjImporter
{
	private readonly record struct VertexRef(int Position, int UV, int Normal);
	private sealed class Surface(string name)
	{
		public string Name { get; } = name;
		public List<VertexRef[]> Faces { get; } = [];
	}

	public static ArrayMesh Import(string path)
	{
		List<Vector3> positions = [];
		List<Vector2> uvs = [];
		List<Vector3> normals = [];
		Dictionary<string, Surface> surfaces = new(StringComparer.OrdinalIgnoreCase);
		Surface current = GetSurface("default");

		foreach (string rawLine in File.ReadLines(path))
		{
			string line = rawLine.Trim();
			if (line.Length == 0 || line[0] == '#') continue;
			string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0) continue;
			switch (parts[0])
			{
				case "v" when parts.Length >= 4:
					positions.Add(new(Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
					break;
				case "vt" when parts.Length >= 3:
					uvs.Add(new(Parse(parts[1]), 1f - Parse(parts[2])));
					break;
				case "vn" when parts.Length >= 4:
					normals.Add(new Vector3(Parse(parts[1]), Parse(parts[2]), Parse(parts[3])).Normalized());
					break;
				case "usemtl":
				case "g":
				case "o":
					current = GetSurface(parts.Length > 1 ? string.Join('_', parts[1..]) : "default");
					break;
				case "f" when parts.Length >= 4:
					VertexRef[] face = new VertexRef[parts.Length - 1];
					for (int i = 1; i < parts.Length; i++) face[i - 1] = ParseVertex(parts[i], positions.Count, uvs.Count, normals.Count);
					current.Faces.Add(face);
					break;
			}
		}

		if (positions.Count == 0) throw new InvalidDataException("The OBJ contains no vertices.");
		ArrayMesh mesh = new();
		foreach (Surface surface in surfaces.Values)
		{
			if (surface.Faces.Count == 0) continue;
			SurfaceTool tool = new();
			tool.Begin(Godot.Mesh.PrimitiveType.Triangles);
			bool needsNormals = false;
			foreach (VertexRef[] face in surface.Faces)
			{
				for (int triangle = 1; triangle < face.Length - 1; triangle++)
				{
					Add(face[0]);
					Add(face[triangle]);
					Add(face[triangle + 1]);
				}
			}
			if (needsNormals) tool.GenerateNormals();
			tool.Index();
			tool.Commit(mesh);

			void Add(VertexRef vertex)
			{
				if (vertex.UV >= 0) tool.SetUV(uvs[vertex.UV]);
				if (vertex.Normal >= 0) tool.SetNormal(normals[vertex.Normal]);
				else needsNormals = true;
				tool.AddVertex(positions[vertex.Position]);
			}
		}

		if (mesh.GetSurfaceCount() == 0) throw new InvalidDataException("The OBJ contains no polygon faces.");
		return mesh;

		Surface GetSurface(string name)
		{
			if (!surfaces.TryGetValue(name, out Surface? surface)) surfaces[name] = surface = new Surface(name);
			return surface;
		}
	}

	private static VertexRef ParseVertex(string value, int positions, int uvs, int normals)
	{
		string[] indices = value.Split('/');
		int position = Resolve(indices[0], positions, required: true);
		int uv = indices.Length > 1 ? Resolve(indices[1], uvs, required: false) : -1;
		int normal = indices.Length > 2 ? Resolve(indices[2], normals, required: false) : -1;
		return new(position, uv, normal);
	}

	private static int Resolve(string value, int count, bool required)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			if (required) throw new InvalidDataException("An OBJ face is missing a vertex index.");
			return -1;
		}
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index == 0)
			throw new InvalidDataException($"Invalid OBJ index '{value}'.");
		int resolved = index > 0 ? index - 1 : count + index;
		if (resolved < 0 || resolved >= count) throw new InvalidDataException($"OBJ index {index} is outside the available data.");
		return resolved;
	}

	private static float Parse(string value)
	{
		if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
			throw new InvalidDataException($"Invalid OBJ number '{value}'.");
		return result;
	}
}

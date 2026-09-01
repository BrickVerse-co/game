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
		public string MaterialName { get; set; } = "";
		public List<VertexRef[]> Faces { get; } = [];
	}
	private sealed class MaterialSpec
	{
		public Color Diffuse = Colors.White;
		public float Opacity = 1f;
		public string TexturePath = "";
	}

	public static ArrayMesh Import(string path)
	{
		List<Vector3> positions = [];
		List<Vector2> uvs = [];
		List<Vector3> normals = [];
		Dictionary<string, Surface> surfaces = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, MaterialSpec> materials = new(StringComparer.OrdinalIgnoreCase);
		string objectName = "default", materialName = "";
		Surface current = GetSurface();

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
				case "g":
				case "o":
					objectName = parts.Length > 1 ? string.Join('_', parts[1..]) : "default";
					current = GetSurface();
					break;
				case "usemtl":
					materialName = parts.Length > 1 ? string.Join(' ', parts[1..]) : "";
					current = GetSurface();
					break;
				case "mtllib" when parts.Length > 1:
					for (int i = 1; i < parts.Length; i++) LoadMaterialLibrary(Path.Combine(Path.GetDirectoryName(path) ?? "", parts[i]), materials);
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
			if (!string.IsNullOrWhiteSpace(surface.MaterialName) && materials.TryGetValue(surface.MaterialName, out MaterialSpec? spec))
				mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, CreateMaterial(spec));

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

		Surface GetSurface()
		{
			string key = objectName + "\n" + materialName;
			if (!surfaces.TryGetValue(key, out Surface? surface))
			{
				surfaces[key] = surface = new Surface(objectName);
				surface.MaterialName = materialName;
			}
			return surface;
		}
	}

	private static void LoadMaterialLibrary(string path, Dictionary<string, MaterialSpec> materials)
	{
		if (!File.Exists(path)) return;
		MaterialSpec? current = null;
		foreach (string rawLine in File.ReadLines(path))
		{
			string line = rawLine.Trim();
			if (line.Length == 0 || line[0] == '#') continue;
			string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0) continue;
			switch (parts[0].ToLowerInvariant())
			{
				case "newmtl" when parts.Length > 1:
					string name = string.Join(' ', parts[1..]);
					materials[name] = current = new MaterialSpec();
					break;
				case "kd" when current != null && parts.Length >= 4:
					current.Diffuse = new Color(Parse(parts[1]), Parse(parts[2]), Parse(parts[3]));
					break;
				case "d" when current != null && parts.Length >= 2:
					current.Opacity = Mathf.Clamp(Parse(parts[1]), 0, 1);
					break;
				case "tr" when current != null && parts.Length >= 2:
					current.Opacity = 1f - Mathf.Clamp(Parse(parts[1]), 0, 1);
					break;
				case "map_kd" when current != null && parts.Length >= 2:
					current.TexturePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? "", string.Join(' ', parts[1..])));
					break;
			}
		}
	}

	private static StandardMaterial3D CreateMaterial(MaterialSpec spec)
	{
		StandardMaterial3D material = new()
		{
			AlbedoColor = new Color(spec.Diffuse, spec.Opacity),
			Transparency = spec.Opacity < 0.999f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
		};
		if (!string.IsNullOrWhiteSpace(spec.TexturePath) && File.Exists(spec.TexturePath))
		{
			Godot.Image image = Godot.Image.LoadFromFile(spec.TexturePath);
			if (!image.IsEmpty()) material.AlbedoTexture = ImageTexture.CreateFromImage(image);
		}
		return material;
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

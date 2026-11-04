// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BrickVerse.Datamodel;
using Godot;

namespace BrickVerse.Creator.Utils;

/// <summary>Wavefront OBJ interchange for native editable meshes.</summary>
public static class EditableMeshObjCodec
{
	public static void Export(EditableMesh mesh, string path)
	{
		ArgumentNullException.ThrowIfNull(mesh);
		Dictionary<int, int> vertexIndices = [];
		StringBuilder text = new("# BrickVerse Creator editable mesh\n");
		foreach (int vertexId in mesh.GetVertices())
		{
			Vector3 value = mesh.GetPosition(vertexId);
			vertexIndices[vertexId] = vertexIndices.Count + 1;
			text.AppendLine($"v {F(value.X)} {F(value.Y)} {F(value.Z)}");
		}

		List<(int FaceId, int Corner)> corners = [];
		foreach (int faceId in mesh.GetFaces())
			for (int corner = 0; corner < 3; corner++)
				corners.Add((faceId, corner));
		foreach ((int faceId, int corner) in corners)
		{
			Vector2 uv = mesh.GetUV(mesh.GetFaceUVs(faceId)[corner]);
			text.AppendLine($"vt {F(uv.X)} {F(1f - uv.Y)}");
		}
		foreach ((int faceId, int corner) in corners)
		{
			Vector3 normal = mesh.GetNormal(mesh.GetFaceNormals(faceId)[corner]);
			text.AppendLine($"vn {F(normal.X)} {F(normal.Y)} {F(normal.Z)}");
		}

		int cornerIndex = 1;
		foreach (int faceId in mesh.GetFaces())
		{
			int[] vertices = mesh.GetFaceVertices(faceId);
			text.Append("f");
			for (int corner = 0; corner < 3; corner++, cornerIndex++)
				text.Append($" {vertexIndices[vertices[corner]]}/{cornerIndex}/{cornerIndex}");
			text.AppendLine();
		}
		File.WriteAllText(path, text.ToString());
	}

	public static EditableMesh Import(string path)
	{
		List<Vector3> positions = [];
		List<Vector2> uvs = [];
		List<Vector3> normals = [];
		List<string[]> faces = [];
		foreach (string raw in File.ReadLines(path))
		{
			string line = raw.Trim();
			if (line.Length == 0 || line[0] == '#')
				continue;
			string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length == 0)
				continue;
			if (fields[0] == "v" && fields.Length >= 4)
				positions.Add(new(Parse(fields[1]), Parse(fields[2]), Parse(fields[3])));
			else if (fields[0] == "vt" && fields.Length >= 3)
				uvs.Add(new(Parse(fields[1]), 1f - Parse(fields[2])));
			else if (fields[0] == "vn" && fields.Length >= 4)
				normals.Add(new Vector3(Parse(fields[1]), Parse(fields[2]), Parse(fields[3])).Normalized());
			else if (fields[0] == "f" && fields.Length >= 4)
				faces.Add(fields[1..]);
		}
		if (positions.Count == 0 || faces.Count == 0)
			throw new InvalidDataException("The Blender exchange OBJ contains no mesh geometry.");

		EditableMesh mesh = new() { AutoCommit = false };
		int[] vertexIds = positions.Select(mesh.AddVertex).ToArray();
		foreach (string[] polygon in faces)
			for (int triangle = 1; triangle < polygon.Length - 1; triangle++)
				AddTriangle(polygon[0], polygon[triangle], polygon[triangle + 1]);
		mesh.Commit();
		mesh.AutoCommit = true;
		return mesh;

		void AddTriangle(string a, string b, string c)
		{
			(string Token, int Corner)[] tokens = [(a, 0), (b, 1), (c, 2)];
			(int Position, int UV, int Normal)[] refs = tokens
				.Select(item => ParseReference(item.Token, positions.Count, uvs.Count, normals.Count))
				.ToArray();
			int faceId = mesh.AddTriangle(
				vertexIds[refs[0].Position],
				vertexIds[refs[1].Position],
				vertexIds[refs[2].Position]
			);
			for (int corner = 0; corner < 3; corner++)
			{
				int vertexId = vertexIds[refs[corner].Position];
				if (refs[corner].UV >= 0)
					mesh.SetVertexFaceUV(vertexId, faceId, mesh.AddUV(uvs[refs[corner].UV]));
				if (refs[corner].Normal >= 0)
					mesh.SetVertexFaceNormal(
						vertexId,
						faceId,
						mesh.AddNormal(normals[refs[corner].Normal])
					);
			}
		}
	}

	private static (int Position, int UV, int Normal) ParseReference(
		string token,
		int positionCount,
		int uvCount,
		int normalCount
	)
	{
		string[] parts = token.Split('/');
		return (
			Resolve(parts[0], positionCount),
			parts.Length > 1 && parts[1].Length > 0 ? Resolve(parts[1], uvCount) : -1,
			parts.Length > 2 && parts[2].Length > 0 ? Resolve(parts[2], normalCount) : -1
		);
	}

	private static int Resolve(string value, int count)
	{
		int index = int.Parse(value, CultureInfo.InvariantCulture);
		index = index < 0 ? count + index : index - 1;
		if (index < 0 || index >= count)
			throw new InvalidDataException("OBJ index is outside its source array.");
		return index;
	}

	private static float Parse(string value) =>
		float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

	private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}

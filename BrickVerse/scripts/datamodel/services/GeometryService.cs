using BrickVerse.Attributes;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BrickVerse.Datamodel.Services;

/// <summary>Solid modelling backed by Godot's exact CSG baker.</summary>
[Static("GeometryService")]
public sealed partial class GeometryService : Instance
{
	[ScriptMethod]
	public async Task<UnionOperation> UnionAsync(Instance[] operands)
	{
		Entity[] entities = operands.OfType<Entity>().Where(item => !item.IsDeleted).ToArray();
		if (entities.Length == 0) throw new ArgumentException("UnionAsync requires at least one Part, Mesh, or NegateOperation.");
		UnionOperation result = Root.New<UnionOperation>();
		result.Position = entities[0].Position;
		result.Parent = entities[0].Parent ?? Root.Environment;
		result.SetGeometry(await BakeEntities(entities, result.GDNode3D.GlobalTransform));
		foreach (Entity entity in entities) { entity.Parent = result; entity.IsHidden = true; entity.CanCollide = false; }
#if CREATOR
		Root.CreatorContext.Selections.SelectOnly(result);
#endif
		return result;
	}

	[ScriptMethod]
	public async Task<EditableMesh> UnionMeshesAsync(EditableMesh[] meshes)
	{
		if (meshes.Length == 0) throw new ArgumentException("At least one EditableMesh is required.");
		return await BakeMeshes(meshes.Select(mesh => (mesh.GetArrayMesh() as Godot.Mesh, Transform3D.Identity, false)));
	}

	[ScriptMethod]
	public async Task<EditableMesh> SubtractMeshesAsync(EditableMesh source, EditableMesh[] cutters)
	{
		List<(Godot.Mesh, Transform3D, bool)> geometry = [(source.GetArrayMesh(), Transform3D.Identity, false)];
		geometry.AddRange(cutters.Select(mesh => (mesh.GetArrayMesh() as Godot.Mesh, Transform3D.Identity, true)));
		return await BakeMeshes(geometry);
	}

	internal async Task RebuildAsync(UnionOperation operation)
	{
		Entity[] sources = operation.GetChildren().OfType<Entity>().Where(child => child != operation).ToArray();
		if (sources.Length == 0) throw new InvalidOperationException("UnionOperation has no source geometry.");
		operation.SetGeometry(await BakeEntities(sources, operation.GDNode3D.GlobalTransform));
	}

	private async Task<EditableMesh> BakeEntities(IEnumerable<Entity> entities, Transform3D outputTransform)
	{
		Transform3D inverse = outputTransform.AffineInverse();
		List<(Godot.Mesh, Transform3D, bool)> geometry = [];
		foreach (Entity entity in entities)
		{
			bool subtract = entity.IsNegated;
			if (entity is Part part)
			{
				(Godot.Mesh mesh, Transform3D transform) = part.GetBooleanGeometry();
				geometry.Add((mesh, inverse * transform, subtract));
			}
			else if (entity is Mesh imported)
			{
				geometry.AddRange(imported.GetBooleanGeometry().Select(item => (item.Mesh, inverse * item.Transform, subtract)));
			}
		}
		if (geometry.Count == 0) throw new InvalidOperationException("The selected instances contain no loaded mesh geometry.");
		return await BakeMeshes(geometry);
	}

	private Task<EditableMesh> BakeMeshes(IEnumerable<(Godot.Mesh Mesh, Transform3D Transform, bool Subtract)> inputs)
	{
		List<RuntimeCsg.Solid> positive = [];
		List<RuntimeCsg.Solid> negative = [];
		foreach ((Godot.Mesh mesh, Transform3D transform, bool subtract) in inputs)
		{
			RuntimeCsg.Solid solid = RuntimeCsg.FromMesh(mesh, transform);
			(subtract ? negative : positive).Add(solid);
		}
		if (positive.Count == 0) throw new InvalidOperationException("A boolean operation requires at least one non-negated solid.");
		RuntimeCsg.Solid result = positive.Skip(1).Aggregate(positive[0], (current, solid) => current.Union(solid));
		foreach (RuntimeCsg.Solid cutter in negative) result = result.Subtract(cutter);
		EditableMesh editable = ToEditableMesh(result);
		if (editable.FaceCount == 0) throw new InvalidOperationException("Boolean operation produced empty geometry.");
		return Task.FromResult(editable);
	}

	private static EditableMesh ToEditableMesh(RuntimeCsg.Solid solid)
	{
		EditableMesh editable = new();
		foreach (RuntimeCsg.Polygon polygon in solid.Polygons)
		{
			for (int i = 2; i < polygon.Vertices.Count; i++)
			{
				int a = editable.AddVertex(polygon.Vertices[0]); int b = editable.AddVertex(polygon.Vertices[i - 1]); int c = editable.AddVertex(polygon.Vertices[i]); editable.AddTriangle(a, b, c);
			}
		}
		editable.Commit(); return editable;
	}
}

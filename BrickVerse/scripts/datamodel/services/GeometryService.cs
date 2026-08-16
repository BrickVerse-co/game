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
		foreach (Entity entity in entities)
		{
			Transform3D transform = entity.GDNode3D.GlobalTransform;
			entity.Parent = result;
			entity.GDNode3D.GlobalTransform = transform;
			entity.IsHidden = true;
			entity.CanCollide = false;
		}
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
				geometry.AddRange(part.GetBooleanGeometry().Select(item => (item.Mesh, inverse * item.Transform, subtract)));
			}
			else if (entity is Mesh imported)
			{
				geometry.AddRange(imported.GetBooleanGeometry().Select(item => (item.Mesh, inverse * item.Transform, subtract)));
			}
		}
		if (geometry.Count == 0) throw new InvalidOperationException("The selected instances contain no loaded mesh geometry.");
		return await BakeMeshes(geometry);
	}

	private async Task<EditableMesh> BakeMeshes(IEnumerable<(Godot.Mesh Mesh, Transform3D Transform, bool Subtract)> inputs)
	{
		List<RuntimeVoxelBoolean.Input> geometry = [];
		foreach ((Godot.Mesh mesh, Transform3D transform, bool subtract) in inputs)
		{
			Vector3[] faces = mesh.GetFaces();
			for (int i = 0; i < faces.Length; i++) faces[i] = transform * faces[i];
			geometry.Add(new RuntimeVoxelBoolean.Input(faces, subtract));
		}
		if (!geometry.Any(input => !input.Subtract)) throw new InvalidOperationException("A boolean operation requires at least one non-negated solid.");
		Vector3[] triangles = await Task.Run(() => RuntimeVoxelBoolean.Bake(geometry));
		EditableMesh editable = ToEditableMesh(triangles);
		if (editable.FaceCount == 0) throw new InvalidOperationException("Boolean operation produced empty geometry.");
		return editable;
	}

	private static EditableMesh ToEditableMesh(Vector3[] triangles)
	{
		return EditableMesh.FromTriangleSoup(triangles);
	}
}

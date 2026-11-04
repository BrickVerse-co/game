// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Creator.Managers;

/// <summary>Undoable Creator commands for selected editable mesh entities.</summary>
public static class EditableMeshTools
{
	public static void PromptExportObj()
	{
		EditableMeshPart? target = World.Current?.CreatorContext.Selections.GetSelected()
			.OfType<EditableMeshPart>().FirstOrDefault();
		if (target == null)
		{
			Report("Select an EditableMeshPart to export");
			return;
		}
		CreatorService.Interface.PromptFileSelect(
			new FileSelectPromptPayload
			{
				Title = "Export editable mesh",
				FileName = target.Name + ".obj",
				DialogMode = DisplayServer.FileDialogMode.SaveFile,
				Filters = ["*.obj;Wavefront OBJ"],
			},
			paths =>
			{
				string path = paths[0].EndsWith(".obj", StringComparison.OrdinalIgnoreCase)
					? paths[0]
					: paths[0] + ".obj";
				EditableMeshObjCodec.Export(target.Geometry, path);
				Report("Exported editable mesh to " + path);
			}
		);
	}

	public static void PromptImportObj()
	{
		CreatorService.Interface.PromptFileSelect(
			new FileSelectPromptPayload
			{
				Title = "Import editable mesh",
				DialogMode = DisplayServer.FileDialogMode.OpenFile,
				Filters = ["*.obj;Wavefront OBJ"],
			},
			paths =>
			{
				World? world = World.Current;
				if (world == null) return;
				EditableMeshPart? target = world.CreatorContext.Selections.GetSelected()
					.OfType<EditableMeshPart>().FirstOrDefault();
				if (target == null)
				{
					target = world.New<EditableMeshPart>(world.Environment);
					target.Name = System.IO.Path.GetFileNameWithoutExtension(paths[0]);
					target.Position = world.CreatorContext.Freelook.GetPlacementPosition();
				}
				target.Geometry = EditableMeshObjCodec.Import(paths[0]);
				world.CreatorContext.Selections.SelectOnly(target);
				Report("Imported editable OBJ " + target.Name);
			}
		);
	}

	public static bool Subdivide(int iterations = 1) =>
		Apply("Subdivide editable meshes", mesh => mesh.Subdivide(iterations: iterations));

	public static bool MergeByDistance(float tolerance = 0.001f) =>
		Apply("Merge editable mesh vertices", mesh => mesh.MergeVertices(tolerance));

	public static bool ProjectUVs(EditableMesh.MeshProjectionPlane plane) =>
		Apply("Project editable mesh UVs", mesh => mesh.ProjectUVs(plane));

	public static bool FlipFaces() =>
		Apply("Flip editable mesh faces", mesh => mesh.FlipFaces());

	private static bool Apply(string title, Action<EditableMesh> operation)
	{
		World? world = World.Current;
		EditableMeshPart[] targets =
			world?.CreatorContext.Selections.GetSelected().OfType<EditableMeshPart>().ToArray()
			?? [];
		if (world == null || targets.Length == 0)
		{
			CreatorService.Interface.StatusBar?.SetStatus("Select one or more EditableMeshPart objects");
			return false;
		}

		Dictionary<EditableMeshPart, EditableMesh> before = [];
		Dictionary<EditableMeshPart, EditableMesh> after = [];
		foreach (EditableMeshPart target in targets)
		{
			before[target] = Clone(target.Geometry);
			EditableMesh edited = Clone(target.Geometry);
			edited.AutoCommit = false;
			operation(edited);
			edited.Commit();
			edited.AutoCommit = true;
			after[target] = edited;
			target.Geometry = edited;
		}

		void Restore(Dictionary<EditableMeshPart, EditableMesh> state)
		{
			foreach ((EditableMeshPart target, EditableMesh geometry) in state)
				if (GodotObject.IsInstanceValid(target.GDNode))
					target.Geometry = geometry;
			world.CreatorContext.Selections.RefreshProperties();
		}

		world.CreatorContext.History.RecordAppliedAction(
			title,
			new BVCallback(_ => Restore(after)),
			new BVCallback(_ => Restore(before))
		);
		CreatorService.Interface.StatusBar?.SetStatus(
			$"Updated {targets.Length} editable mesh{(targets.Length == 1 ? "" : "es")}"
		);
		return true;
	}

	private static EditableMesh Clone(EditableMesh source) =>
		source.SeparateFaces(source.GetFaces());

	private static void Report(string message) =>
		CreatorService.Interface.StatusBar?.SetStatus(message);
}

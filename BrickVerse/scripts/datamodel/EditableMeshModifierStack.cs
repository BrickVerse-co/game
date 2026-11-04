// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>
/// Reorderable, non-destructive operations evaluated against a copy of an EditableMesh.
/// Modifier IDs remain stable while the stack is edited.
/// </summary>
public sealed partial class EditableMeshModifierStack : RefCounted, IScriptObject
{
	private sealed class Modifier
	{
		public int Id;
		public ModifierType Type;
		public bool Enabled = true;
		public Transform3D Transform = Transform3D.Identity;
		public float Value;
		public int Iterations = 1;
		public EditableMesh.MeshProjectionPlane ProjectionPlane;
		public Vector2 Offset;
	}

	private readonly List<Modifier> _modifiers = [];
	private int _nextId = 1;

	[ScriptProperty]
	public int Count => _modifiers.Count;

	[ScriptMethod]
	public int AddTransform(Transform3D transform)
	{
		Modifier modifier = NewModifier(ModifierType.Transform);
		modifier.Transform = transform;
		return modifier.Id;
	}

	[ScriptMethod]
	public int AddSubdivision(int iterations = 1)
	{
		if (iterations is < 1 or > 6)
			throw new ArgumentOutOfRangeException(nameof(iterations));
		Modifier modifier = NewModifier(ModifierType.Subdivision);
		modifier.Iterations = iterations;
		return modifier.Id;
	}

	[ScriptMethod]
	public int AddWeld(float tolerance)
	{
		if (!float.IsFinite(tolerance) || tolerance < 0f)
			throw new ArgumentOutOfRangeException(nameof(tolerance));
		Modifier modifier = NewModifier(ModifierType.Weld);
		modifier.Value = tolerance;
		return modifier.Id;
	}

	[ScriptMethod]
	public int AddPlanarUV(
		EditableMesh.MeshProjectionPlane plane = EditableMesh.MeshProjectionPlane.XZ,
		float scale = 1f,
		Vector2 offset = default
	)
	{
		if (!float.IsFinite(scale) || Mathf.IsZeroApprox(scale))
			throw new ArgumentOutOfRangeException(nameof(scale));
		Modifier modifier = NewModifier(ModifierType.PlanarUV);
		modifier.ProjectionPlane = plane;
		modifier.Value = scale;
		modifier.Offset = offset;
		return modifier.Id;
	}

	[ScriptMethod]
	public int[] GetModifiers() => [.. _modifiers.Select(modifier => modifier.Id)];

	[ScriptMethod]
	public ModifierType GetModifierType(int modifierId) => Require(modifierId).Type;

	[ScriptMethod]
	public bool GetEnabled(int modifierId) => Require(modifierId).Enabled;

	[ScriptMethod]
	public void SetEnabled(int modifierId, bool enabled) => Require(modifierId).Enabled = enabled;

	[ScriptMethod]
	public void Remove(int modifierId)
	{
		int index = IndexOf(modifierId);
		_modifiers.RemoveAt(index);
	}

	[ScriptMethod]
	public void Move(int modifierId, int targetIndex)
	{
		if (targetIndex < 0 || targetIndex >= _modifiers.Count)
			throw new ArgumentOutOfRangeException(nameof(targetIndex));
		int sourceIndex = IndexOf(modifierId);
		Modifier modifier = _modifiers[sourceIndex];
		_modifiers.RemoveAt(sourceIndex);
		_modifiers.Insert(targetIndex, modifier);
	}

	[ScriptMethod]
	public void Clear() => _modifiers.Clear();

	/// <summary>Evaluates the stack into a new mesh. The source is never changed.</summary>
	[ScriptMethod]
	public EditableMesh Evaluate(EditableMesh source)
	{
		if (source == null)
			throw new ArgumentNullException(nameof(source));
		EditableMesh result = source.SeparateFaces(source.GetFaces());
		result.AutoCommit = false;
		foreach (Modifier modifier in _modifiers.Where(item => item.Enabled))
		{
			switch (modifier.Type)
			{
				case ModifierType.Transform:
					result.TransformVertices(modifier.Transform);
					break;
				case ModifierType.Subdivision:
					result.Subdivide(iterations: modifier.Iterations);
					break;
				case ModifierType.Weld:
					result.MergeVertices(modifier.Value);
					break;
				case ModifierType.PlanarUV:
					result.ProjectUVs(
						modifier.ProjectionPlane,
						modifier.Value,
						modifier.Offset
					);
					break;
			}
		}
		result.Commit();
		result.AutoCommit = source.AutoCommit;
		return result;
	}

	private Modifier NewModifier(ModifierType type)
	{
		Modifier modifier = new() { Id = _nextId++, Type = type };
		_modifiers.Add(modifier);
		return modifier;
	}

	private Modifier Require(int modifierId)
	{
		Modifier? modifier = _modifiers.FirstOrDefault(item => item.Id == modifierId);
		return modifier
			?? throw new KeyNotFoundException($"Modifier ID {modifierId} does not exist.");
	}

	private int IndexOf(int modifierId)
	{
		int index = _modifiers.FindIndex(item => item.Id == modifierId);
		return index >= 0
			? index
			: throw new KeyNotFoundException($"Modifier ID {modifierId} does not exist.");
	}

	[ScriptEnum]
	public enum ModifierType
	{
		Transform,
		Subdivision,
		Weld,
		PlanarUV,
	}
}

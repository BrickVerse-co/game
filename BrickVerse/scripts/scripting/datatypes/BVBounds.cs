// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Scripting.Datatypes;

public class BVBounds : IScriptGDObject
{
	internal Aabb aabb;

	[ScriptProperty]
	public Vector3 Center => aabb.GetCenter();

	[ScriptProperty]
	public Vector3 Size
	{
		get => aabb.Size;
		set => aabb.Size = value;
	}

	[ScriptProperty]
	public Vector3 Extents => aabb.Size / 2;

	[ScriptProperty, ScriptLegacyProperty("Min")]
	public Vector3 Start => aabb.Position;

	[ScriptProperty, ScriptLegacyProperty("Max")]
	public Vector3 End
	{
		get => aabb.End;
		set => aabb.End = value;
	}

	[ScriptProperty]
	public float Volume => aabb.Volume;

	public static BVBounds FromGDClass(Aabb bound)
	{
		return new BVBounds() { aabb = bound };
	}

	public object ToGDClass()
	{
		return aabb;
	}

	[ScriptMethod]
	public static BVBounds New()
	{
		return FromGDClass(new());
	}

	[ScriptMethod]
	public static BVBounds New(Vector3 position, Vector3 size)
	{
		return FromGDClass(new(position, size));
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Eq)]
	public static bool Eq(BVBounds a, BVBounds b)
	{
		return a.aabb == b.aabb;
	}

	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(BVBounds? v)
	{
		if (v == null)
			return "<Bounds>";
		return $"<Bounds:({v.Start}, {v.End}, {v.Size})>";
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static Vector3 ClosestPoint(BVBounds bounds, BVector3 point) =>
		point.vector.Clamp(bounds.aabb.Position, bounds.aabb.End);

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static bool Contains(BVBounds bounds, BVector3 point) =>
		bounds.aabb.HasPoint(point.vector);

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVBounds Encapsulate(BVBounds bounds, BVector3 point) =>
		FromGDClass(bounds.aabb.Expand(point.vector));

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVBounds Expand(BVBounds bounds, float amount) =>
		FromGDClass(bounds.aabb.Grow(amount));

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static bool Intersects(BVBounds bounds, BVBounds other) =>
		bounds.aabb.Intersects(other.aabb);

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVBounds Intersection(BVBounds bounds, BVBounds other) =>
		FromGDClass(bounds.aabb.Intersection(other.aabb));

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVBounds SetMinMax(BVBounds bounds, BVector3 min, BVector3 max)
	{
		Aabb aabb = bounds.aabb;
		aabb.Position = min.vector;
		aabb.Size = max.vector - min.vector;
		return FromGDClass(aabb);
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static float Distance(BVBounds bounds, BVector3 point) =>
		point.vector.DistanceTo(ClosestPoint(bounds, point));

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static float SqrDistance(BVBounds bounds, BVector3 point) =>
		point.vector.DistanceSquaredTo(ClosestPoint(bounds, point));
}

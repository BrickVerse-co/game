// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;

namespace BrickVerse.Scripting.Datatypes;

public class BVBounds : IScriptGDObject
{
	internal Aabb aabb;

	[ScriptProperty] public Vector3 Center => aabb.GetCenter();
	[ScriptProperty] public Vector3 Size { get => aabb.Size; set => aabb.Size = value; }
	[ScriptProperty] public Vector3 Extents => aabb.Size / 2;
	[ScriptProperty, ScriptLegacyProperty("Min")] public Vector3 Start => aabb.Position;
	[ScriptProperty, ScriptLegacyProperty("Max")] public Vector3 End { get => aabb.End; set => aabb.End = value; }
	[ScriptProperty] public float Volume => aabb.Volume;

	public static BVBounds FromGDClass(Aabb bound)
	{
		return new BVBounds()
		{
			aabb = bound
		};
	}

	public object ToGDClass()
	{
		return aabb;
	}

	[ScriptMethod]
	public static BVBounds New()
	{
		return FromGDClass(new Aabb(Vector3.Zero, Vector3.Zero));
	}

	[ScriptMethod]
	public static BVBounds New(Vector3 position, Vector3 size)
	{
		return FromGDClass(new Aabb(position, size));
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Eq)]
	public static bool Eq(BVBounds a, BVBounds b)
	{
		return a.aabb == b.aabb;
	}

	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(BVBounds? v)
	{
		if (v == null) return "<Bounds>";
		return $"<Bounds:({v.Start}, {v.End}, {v.Size})>";
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static Vector3 ClosestPoint(BVBounds bounds, BVector3 point) => bounds.aabb.GetSupport(point.vector);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static bool Contains(BVBounds bounds, BVector3 point) => bounds.aabb.HasPoint(point.vector);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVBounds Encapsulate(BVBounds bounds, BVector3 point) => FromGDClass(bounds.aabb.Expand(point.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVBounds Expand(BVBounds bounds, float amount) => FromGDClass(bounds.aabb.Grow(amount));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static bool Intersects(BVBounds bounds, BVBounds other) => bounds.aabb.Intersects(other.aabb);

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVBounds SetMinMax(BVBounds bounds, BVector3 min, BVector3 max)
	{
		Aabb aabb = bounds.aabb;
		aabb.Position = min.vector;
		aabb.Size = max.vector - min.vector;
		return FromGDClass(aabb);
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static float Distance(BVBounds bounds, BVector3 point)
	{
		Vector3 closest = bounds.aabb.GetCenter().Clamp(bounds.aabb.Position, bounds.aabb.End);
		return point.vector.DistanceSquaredTo(closest);
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static float SqrDistance(BVBounds bounds, BVector3 point)
	{
		Vector3 closest = Vector3.Zero;
		closest.X = Mathf.Clamp(point.vector.X, bounds.aabb.Position.X, bounds.aabb.End.X);
		closest.Y = Mathf.Clamp(point.vector.Y, bounds.aabb.Position.Y, bounds.aabb.End.Y);
		closest.Z = Mathf.Clamp(point.vector.Z, bounds.aabb.Position.Z, bounds.aabb.End.Z);

		return point.vector.DistanceSquaredTo(closest);
	}
}

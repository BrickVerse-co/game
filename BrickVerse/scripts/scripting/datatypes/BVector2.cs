// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using System;

namespace BrickVerse.Scripting.Datatypes;

public class BVector2 : IScriptGDObject
{
	Vector2 vector;

	[ScriptProperty] public float X { get => vector.X; set => vector.X = value; }
	[ScriptProperty] public float Y { get => vector.Y; set => vector.Y = value; }


	[ScriptProperty] public static BVector2 Down { get; private set; } = new() { X = 0, Y = -1 };
	[ScriptProperty] public static BVector2 Left { get; private set; } = new() { X = -1, Y = 0 };
	[ScriptProperty] public static BVector2 One { get; private set; } = new() { X = 1, Y = 1 };
	[ScriptProperty] public static BVector2 Zero { get; private set; } = new() { X = 0, Y = 0 };
	[ScriptProperty] public static BVector2 Right { get; private set; } = new() { X = 1, Y = 0 };
	[ScriptProperty] public static BVector2 Up { get; private set; } = new() { X = 0, Y = 1 };

	[ScriptProperty] public float Magnitude => vector.Length();
	[ScriptProperty] public BVector2 Normalized => FromGDClass(vector.Normalized());
	[ScriptProperty] public float SqrMagnitude => vector.LengthSquared();

	public static BVector2 FromGDClass(Vector2 vec)
	{
		return new BVector2()
		{
			vector = (Vector2)vec
		};
	}

	public object ToGDClass()
	{
		return vector;
	}

	[ScriptMethod]
	public static BVector2 New()
	{
		return new()
		{
			X = 0,
			Y = 0,
		};
	}

	[ScriptMethod]
	public static BVector2 New(float d)
	{
		return new()
		{
			X = d,
			Y = d,
		};
	}

	[ScriptMethod]
	public static BVector2 New(float x, float y)
	{
		return new()
		{
			X = x,
			Y = y,
		};
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Add)]
	public static BVector2 Add(BVector2 a, BVector2 b)
	{
		return FromGDClass(a.vector + b.vector);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Sub)]
	public static BVector2 Sub(BVector2 a, BVector2 b)
	{
		return FromGDClass(a.vector - b.vector);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static BVector2 MulVectorVector(BVector2 a, BVector2 b)
		=> FromGDClass(a.vector * b.vector);

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static BVector2 MulVectorScalar(BVector2 a, double scalar)
		=> FromGDClass(a.vector * (float)scalar);

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static BVector2 MulScalarVector(double scalar, BVector2 b)
		=> FromGDClass(b.vector * (float)scalar);

	[ScriptMetamethod(ScriptObjectMetamethod.Div)]
	public static BVector2 Div(BVector2 a, double b)
	{
		return FromGDClass(a.vector / (float)b);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Mod)]
	public static BVector2 Mod(BVector2 a, BVector2 b)
	{
		return FromGDClass(new Vector2(
			a.vector.X % b.vector.X,
			a.vector.Y % b.vector.Y
		));
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Unm)]
	public static BVector2 Unm(BVector2 a)
	{
		return FromGDClass(-a.vector);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Pow)]
	public static BVector2 Pow(BVector2 a, BVector2 b)
	{
		return FromGDClass(new Vector2(
			(float)Math.Pow(a.vector.X, b.vector.X),
			(float)Math.Pow(a.vector.Y, b.vector.Y)
		));
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Eq)]
	public static bool Eq(BVector2 a, BVector2 b)
	{
		return a.vector == b.vector;
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Lt)]
	public static bool Lt(BVector2 a, BVector2 b)
	{
		return a.vector.LengthSquared() < b.vector.LengthSquared();
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Le)]
	public static bool Le(BVector2 a, BVector2 b)
	{
		return a.vector.LengthSquared() <= b.vector.LengthSquared();
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Len)]
	public static double Len(BVector2 a)
	{
		return a.vector.Length();
	}

	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(BVector2? v)
	{
		if (v == null) return "<Vector2>";
		return $"<Vector2:({v.vector.X}, {v.vector.Y})>";
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float Angle(BVector2 from, BVector2 to) => from.vector.AngleTo(to.vector);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float Cross(BVector2 lhs, BVector2 rhs) => lhs.vector.Cross(rhs.vector);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float Distance(BVector2 a, BVector2 b) => a.vector.DistanceTo(b.vector);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float Dot(BVector2 lhs, BVector2 rhs) => lhs.vector.Dot(rhs.vector);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Lerp(BVector2 a, BVector2 b, float t) => FromGDClass(a.vector.Lerp(b.vector, t));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Max(BVector2 lhs, BVector2 rhs) => FromGDClass(lhs.vector.Max(rhs.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Min(BVector2 lhs, BVector2 rhs) => FromGDClass(lhs.vector.Min(rhs.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 MoveTowards(BVector2 current, BVector2 target, float maxDistanceDelta) => FromGDClass(current.vector.MoveToward(target.vector, maxDistanceDelta));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Normalize(BVector2 value) => FromGDClass(value.vector.Normalized());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Project(BVector2 vector, BVector2 onNormal) => FromGDClass(vector.vector.Project(onNormal.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Reflect(BVector2 inDirection, BVector2 inNormal) => FromGDClass(inDirection.vector.Reflect(inNormal.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Slerp(BVector2 a, BVector2 b, float t) => FromGDClass(a.vector.Slerp(b.vector, t));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Floor(BVector2 val) => FromGDClass(val.vector.Floor());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Ceil(BVector2 val) => FromGDClass(val.vector.Ceil());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Round(BVector2 val) => FromGDClass(val.vector.Round());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Abs(BVector2 val) => FromGDClass(val.vector.Abs());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Sign(BVector2 val) => FromGDClass(val.vector.Sign());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Clamp(BVector2 val, BVector2 min, BVector2 max) => FromGDClass(val.vector.Clamp(min.vector, max.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 ProjectOnPlane(BVector2 vector, BVector2 planeNormal) => FromGDClass(vector.vector.Slide(planeNormal.vector.Normalized()));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 Rotated(BVector2 val, float angle) => FromGDClass(val.vector.Rotated(angle));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector2 LimitLength(BVector2 val, float length) => FromGDClass(val.vector.LimitLength(length));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVector2 RadToDeg(BVector2 val) => FromGDClass(new()
	{
		X = Mathf.RadToDeg(val.X),
		Y = Mathf.RadToDeg(val.Y),
	});

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVector2 DegToRad(BVector2 val) => FromGDClass(new()
	{
		X = Mathf.DegToRad(val.X),
		Y = Mathf.DegToRad(val.Y),
	});
}

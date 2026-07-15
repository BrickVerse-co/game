// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Utils;
using System;

namespace BrickVerse.Scripting.Datatypes;

public class BVector3 : IScriptGDObject
{
	internal Vector3 vector;

	[ScriptProperty] public float X { get => vector.X; set => vector.X = value; }
	[ScriptProperty] public float Y { get => vector.Y; set => vector.Y = value; }
	[ScriptProperty] public float Z { get => vector.Z; set => vector.Z = value; }

	[ScriptProperty] public static BVector3 Forward { get; private set; } = new() { X = 0, Y = 0, Z = -1 };
	[ScriptProperty] public static BVector3 Back { get; private set; } = new() { X = 0, Y = 0, Z = 1 };
	[ScriptProperty] public static BVector3 Down { get; private set; } = new() { X = 0, Y = -1, Z = 0 };
	[ScriptProperty] public static BVector3 Left { get; private set; } = new() { X = -1, Y = 0, Z = 0 };
	[ScriptProperty] public static BVector3 One { get; private set; } = new() { X = 1, Y = 1, Z = 1 };
	[ScriptProperty] public static BVector3 Zero { get; private set; } = new() { X = 0, Y = 0, Z = 0 };
	[ScriptProperty] public static BVector3 Right { get; private set; } = new() { X = 1, Y = 0, Z = 0 };
	[ScriptProperty] public static BVector3 Up { get; private set; } = new() { X = 0, Y = 1, Z = 0 };

	[ScriptProperty] public float Magnitude => vector.Length();
	[ScriptProperty] public BVector3 Normalized => FromGDClass(vector.Normalized());
	[ScriptProperty] public float SqrMagnitude => vector.LengthSquared();

	public static BVector3 FromGDClass(Vector3 vec)
	{
		return new BVector3()
		{
			vector = vec
		};
	}

	public object ToGDClass()
	{
		return vector;
	}

	[ScriptMethod]
	public static BVector3 New()
	{
		return new()
		{
			X = 0,
			Y = 0,
			Z = 0
		};
	}

	[ScriptMethod]
	public static BVector3 New(float d)
	{
		return new()
		{
			X = d,
			Y = d,
			Z = d
		};
	}

	[ScriptMethod]
	public static BVector3 New(float x, float y)
	{
		return new()
		{
			X = x,
			Y = y,
			Z = 0
		};
	}

	[ScriptMethod]
	public static BVector3 New(float x, float y, float z)
	{
		//PT.Print("New vector3: ", x, y, z);
		return new()
		{
			X = x,
			Y = y,
			Z = z
		};
	}

	[ScriptMethod]
	public static BVector3 New(BVector2 v)
	{
		return new()
		{
			X = v.X,
			Y = v.Y,
			Z = 0
		};
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Add)]
	public static BVector3 Add(BVector3 a, BVector3 b)
	{
		return FromGDClass(a.vector + b.vector);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Sub)]
	public static BVector3 SubVectorVector(BVector3 a, BVector3 b)
	=> FromGDClass(a.vector - b.vector);

	[ScriptMetamethod(ScriptObjectMetamethod.Sub)]
	public static BVector3 SubVectorQuaternion(BVector3 a, BVQuaternion q)
		=> FromGDClass(a.vector - new Vector3(q.X, q.Y, q.Z));

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static BVector3 MulVectorVector(BVector3 a, BVector3 b)
	=> FromGDClass(a.vector * b.vector);

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static BVector3 MulVectorScalar(BVector3 a, double scalar)
		=> FromGDClass(a.vector * (float)scalar);

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static BVector3 MulScalarVector(double scalar, BVector3 b)
		=> FromGDClass(b.vector * (float)scalar);

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static BVector3 MulVectorQuaternion(BVector3 a, BVQuaternion q)
		=> FromGDClass(a.vector * q.quat.Normalized());

	[ScriptMetamethod(ScriptObjectMetamethod.Div)]
	public static BVector3 Div(BVector3 a, double b)
	{
		return FromGDClass(a.vector / (float)b);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Mod)]
	public static BVector3 Mod(BVector3 a, BVector3 b)
	{
		return FromGDClass(new Vector3(
			a.vector.X % b.vector.X,
			a.vector.Y % b.vector.Y,
			a.vector.Z % b.vector.Z
		));
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Unm)]
	public static BVector3 Unm(BVector3 a)
	{
		return FromGDClass(-a.vector);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Pow)]
	public static BVector3 Pow(BVector3 a, BVector3 b)
	{
		return FromGDClass(new Vector3(
			(float)Math.Pow(a.vector.X, b.vector.X),
			(float)Math.Pow(a.vector.Y, b.vector.Y),
			(float)Math.Pow(a.vector.Z, b.vector.Z)
		));
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Eq)]
	public static bool Eq(BVector3 a, BVector3 b)
	{
		return a.vector == b.vector;
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Lt)]
	public static bool Lt(BVector3 a, BVector3 b)
	{
		return a.vector.LengthSquared() < b.vector.LengthSquared();
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Le)]
	public static bool Le(BVector3 a, BVector3 b)
	{
		return a.vector.LengthSquared() <= b.vector.LengthSquared();
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Len)]
	public static double Len(BVector3 a)
	{
		return a.vector.Length();
	}

	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(BVector3? v)
	{
		if (v == null) return "<Vector3>";
		return $"<Vector3:({v.vector.X}, {v.vector.Y}, {v.vector.Z})>";
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float Angle(BVector3 from, BVector3 to) => from.vector.AngleTo(to.vector);
	//[ScriptMethod] public static Vector3 ClampMagnitude(Vector3 vector, float maxLength) => vector.Clamp(vector, maxLength);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Cross(BVector3 lhs, BVector3 rhs) => FromGDClass(lhs.vector.Cross(rhs.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float Distance(BVector3 a, BVector3 b) => a.vector.DistanceTo(b.vector);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float Dot(BVector3 lhs, BVector3 rhs) => lhs.vector.Dot(rhs.vector);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Lerp(BVector3 a, BVector3 b, float t) => FromGDClass(a.vector.Lerp(b.vector, t));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Max(BVector3 lhs, BVector3 rhs) => FromGDClass(lhs.vector.Max(rhs.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Min(BVector3 lhs, BVector3 rhs) => FromGDClass(lhs.vector.Min(rhs.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 MoveTowards(BVector3 current, BVector3 target, float maxDistanceDelta) => FromGDClass(current.vector.MoveToward(target.vector, maxDistanceDelta));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Normalize(BVector3 value) => FromGDClass(value.vector.Normalized());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Project(BVector3 vector, BVector3 onNormal) => FromGDClass(vector.vector.Project(onNormal.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 ProjectOnPlane(BVector3 vector, BVector3 planeNormal) => FromGDClass(vector.vector.Slide(planeNormal.vector.Normalized()));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Reflect(BVector3 inDirection, BVector3 inNormal) => FromGDClass(inDirection.vector.Reflect(inNormal.vector));
	//[ScriptMethod] public static Vector3 RotateTowards(Vector3 current, Vector3 target, float maxRadiansDelta, float maxMagnitudeDelta) => current.RotateTowards(current, target, maxRadiansDelta, maxMagnitudeDelta);
	//public static Vector3 Scale(Vector3 a, Vector3 b) => a.Scale(b);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float SignedAngle(BVector3 from, BVector3 to, BVector3 axis) => from.vector.SignedAngleTo(to.vector, axis.vector);

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVector3 Slerp(BVector3 a, BVector3 b, float t)
	{
		Vector3 normalizedA = a.vector.Normalized();
		Vector3 normalizedB = b.vector.Normalized();
		return FromGDClass(normalizedA.Slerp(normalizedB, t));
	}
	//public static Vector3 SlerpUnclamped(Vector3 a, Vector3 b, float t) => a.SlerpUnclamped(b, t);
	//public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime, float maxSpeed, float deltaTime) => current.SmoothDamp(target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Floor(BVector3 val) => FromGDClass(val.vector.Floor());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Ceil(BVector3 val) => FromGDClass(val.vector.Ceil());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Round(BVector3 val) => FromGDClass(val.vector.Round());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Abs(BVector3 val) => FromGDClass(val.vector.Abs());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Sign(BVector3 val) => FromGDClass(val.vector.Sign());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Rotated(BVector3 val, BVector3 axis, float angle) => FromGDClass(val.vector.Rotated(axis.vector, angle));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 LimitLength(BVector3 val, float length) => FromGDClass(val.vector.LimitLength(length));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 Clamp(BVector3 val, BVector3 min, BVector3 max) => FromGDClass(val.vector.Clamp(min.vector, max.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 RadToDeg(BVector3 val) => FromGDClass(val.vector.RadToDeg());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static BVector3 DegToRad(BVector3 val) => FromGDClass(val.vector.DegToRad());
}

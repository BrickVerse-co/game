// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Utils;

namespace BrickVerse.Scripting.Datatypes;

// NOTE: Quaternion exposed to developers is in degrees
public class BVQuaternion : IScriptGDObject
{
	internal Quaternion quat;

	[ScriptProperty] public float X { get => quat.X; set => quat.X = value; }
	[ScriptProperty] public float Y { get => quat.Y; set => quat.Y = value; }
	[ScriptProperty] public float Z { get => quat.Z; set => quat.Z = value; }
	[ScriptProperty] public float W { get => quat.W; set => quat.W = value; }
	[ScriptProperty] public static BVQuaternion Identity => new() { X = 0, Y = 0, Z = 0, W = 1 };

	public static BVQuaternion FromGDClass(Quaternion qu)
	{
		return new BVQuaternion()
		{
			quat = qu
		};
	}

	public object ToGDClass()
	{
		return quat;
	}

	[ScriptMethod]
	public static BVQuaternion New()
	{
		return Identity;
	}

	[ScriptMethod]
	public static BVQuaternion New(float x, float y, float z, float w)
	{
		return new()
		{
			X = x,
			Y = y,
			Z = z,
			W = w
		};
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Add)]
	public static BVQuaternion Add(BVQuaternion a, BVQuaternion b)
	{
		return FromGDClass(a.quat + b.quat);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Sub)]
	public static BVQuaternion SubQuaternionQuaternion(BVQuaternion a, BVQuaternion b)
		=> FromGDClass(a.quat - b.quat);

	[ScriptMetamethod(ScriptObjectMetamethod.Sub)]
	public static BVQuaternion SubQuaternionVector(BVQuaternion a, BVector3 v)
		=> FromGDClass(a.quat - new Quaternion(v.X, v.Y, v.Z, 1));

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static BVQuaternion MulQuaternionQuaternion(BVQuaternion a, BVQuaternion b)
		=> FromGDClass(a.quat.Normalized() * b.quat.Normalized());

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static BVector3 MulQuaternionVector(BVQuaternion a, BVector3 v)
		=> BVector3.FromGDClass(a.quat.Normalized() * v.vector);

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static BVector3 MulVectorQuaternion(BVector3 v, BVQuaternion q)
	=> BVector3.FromGDClass(q.quat.Normalized() * v.vector);

	[ScriptMetamethod(ScriptObjectMetamethod.Eq)]
	public static bool Eq(BVQuaternion a, BVQuaternion b)
	{
		return a.quat == b.quat;
	}

	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(BVQuaternion? v)
	{
		if (v == null) return "<Quaternion>";
		return $"<Quaternion:({v.quat.X}, {v.quat.Y}, {v.quat.Z}, {v.quat.W})>";
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static float Angle(BVQuaternion a, BVQuaternion b)
	{
		// Angle still Works with Deg
		return Mathf.RadToDeg(a.quat.AngleTo(b.quat));
	}

	[ScriptMethod]
	public static BVQuaternion AngleAxis(float angle, Vector3 axis)
	{
		return FromGDClass(new Quaternion(axis, angle));
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static float Dot(BVQuaternion a, BVQuaternion b)
	{
		return a.quat.Dot(b.quat);
	}

	[ScriptMethod]
	public static BVQuaternion Euler(float x, float y, float z)
	{
		return FromGDClass(Quaternion.FromEuler(MathUtils.Vector3DegToRad(new(x, y, z))));
	}

	[ScriptMethod]
	public static BVQuaternion Euler(Vector3 euler)
	{
		return FromGDClass(Quaternion.FromEuler(euler.DegToRad()));
	}


	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static Vector3 ToEuler(BVQuaternion euler)
	{
		return MathUtils.Vector3RadToDeg(euler.quat.GetEuler());
	}

	[ScriptMethod]
	public static BVQuaternion FromToRotation(Vector3 fromDirection, Vector3 toDirection)
	{
		Vector3 from = fromDirection.Normalized();
		Vector3 to = toDirection.Normalized();

		float dot = from.Dot(to);

		// same direction
		if (dot >= 1.0f - 1e-6f)
			return FromGDClass(Quaternion.Identity);

		// opposite directions
		if (dot <= -1.0f + 1e-6f)
		{
			Vector3 perpendicular = from.Cross(Vector3.Up);
			if (perpendicular.LengthSquared() < 1e-6f)
				perpendicular = from.Cross(Vector3.Right);
			return FromGDClass(new Quaternion(perpendicular.Normalized(), Mathf.Pi));
		}

		Vector3 axis = from.Cross(to).Normalized();
		float angle = Mathf.Acos(Mathf.Clamp(dot, -1.0f, 1.0f));
		return FromGDClass(new Quaternion(axis, angle));
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVQuaternion Inverse(BVQuaternion rotation)
	{
		return FromGDClass(rotation.quat.Inverse());
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVQuaternion Lerp(BVQuaternion a, BVQuaternion b, float t)
	{
		Quaternion q = new(
			Mathf.Lerp(a.quat.X, b.quat.X, t),
			Mathf.Lerp(a.quat.Y, b.quat.Y, t),
			Mathf.Lerp(a.quat.Z, b.quat.Z, t),
			Mathf.Lerp(a.quat.W, b.quat.W, t)
		);
		q = q.Normalized();
		return FromGDClass(q);
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVQuaternion LerpUnclamped(BVQuaternion a, BVQuaternion b, float t)
	{
		Quaternion q = new(
			Mathf.Lerp(a.quat.X, b.quat.X, t),
			Mathf.Lerp(a.quat.Y, b.quat.Y, t),
			Mathf.Lerp(a.quat.Z, b.quat.Z, t),
			Mathf.Lerp(a.quat.W, b.quat.W, t)
		);
		return FromGDClass(q);
	}

	[ScriptMethod]
	public static BVQuaternion LookRotation(Vector3 forward)
	{
		return LookRotation(forward, Vector3.Up);
	}

	[ScriptMethod]
	public static BVQuaternion LookRotation(Vector3 forward, Vector3 upwards)
	{
		forward = forward.Normalized();
		upwards = upwards.Normalized();

		var basis = Basis.LookingAt(-forward, upwards);
		return FromGDClass(basis.GetRotationQuaternion());
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVQuaternion Normalize(BVQuaternion quaternion)
	{
		return FromGDClass(quaternion.quat.Normalized());
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVQuaternion RotateTowards(BVQuaternion from, BVQuaternion to, float maxDegreesDelta)
	{
		Quaternion fromQ = from.quat;
		Quaternion toQ = to.quat;

		float angle = fromQ.AngleTo(toQ);
		float maxRadiansDelta = Mathf.DegToRad(maxDegreesDelta);

		if (angle == 0)
			return to; // already same rotation

		// Determine interpolation factor
		float t = Mathf.Min(1f, maxRadiansDelta / angle);
		Quaternion result = fromQ.Slerp(toQ, t);

		return FromGDClass(result);
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVQuaternion Slerp(BVQuaternion a, BVQuaternion b, float t)
	{
		return FromGDClass(a.quat.Slerp(b.quat, t));
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVQuaternion SlerpUnclamped(BVQuaternion a, BVQuaternion b, float t)
	{
		return FromGDClass(a.quat.Slerpni(b.quat, t));
	}

}

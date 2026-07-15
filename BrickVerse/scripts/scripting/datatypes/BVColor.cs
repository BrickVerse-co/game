// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;

namespace BrickVerse.Scripting.Datatypes;

public class BVColor : IScriptGDObject
{
	Color color;

	[ScriptProperty] public float R { get => color.R; set => color.R = value; }
	[ScriptProperty] public float G { get => color.G; set => color.G = value; }
	[ScriptProperty] public float B { get => color.B; set => color.B = value; }
	[ScriptProperty] public float A { get => color.A; set => color.A = value; }

	public static BVColor FromGDClass(Color clr)
	{
		return new BVColor()
		{
			color = clr
		};
	}

	public object ToGDClass()
	{
		return color;
	}

	[ScriptMethod]
	public static BVColor New()
	{
		return new()
		{
			R = 0,
			G = 0,
			B = 0,
			A = 1
		};
	}

	[ScriptMethod]
	public static BVColor New(float d)
	{
		return new()
		{
			R = d,
			G = d,
			B = d,
			A = 1
		};
	}

	[ScriptMethod]
	public static BVColor New(float r, float g, float b)
	{
		return new()
		{
			R = r,
			G = g,
			B = b,
			A = 1
		};
	}

	[ScriptMethod]
	public static BVColor New(float r, float g, float b, float a)
	{
		return new()
		{
			R = r,
			G = g,
			B = b,
			A = a
		};
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Add)]
	public static BVColor Add(BVColor a, BVColor b)
	{
		return FromGDClass(a.color + b.color);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Sub)]
	public static BVColor Sub(BVColor a, BVColor b)
	{
		return FromGDClass(a.color - b.color);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static object Mul(BVColor a, object b)
	{
		if (b is double d)
			return FromGDClass(a.color * new Color((float)d, (float)d, (float)d));
		return FromGDClass(a.color);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Eq)]
	public static bool Eq(BVColor a, BVColor b)
	{
		return a.color == b.color;
	}

	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(BVColor? v)
	{
		if (v == null) return "<Color>";
		return $"<Color:({v.color.R}, {v.color.G}, {v.color.B}, {v.color.A})>";
	}

	[ScriptMethod]
	public static BVColor Random()
	{
		return New(GD.Randf(), GD.Randf(), GD.Randf());
	}

	[ScriptMethod]
	public static BVColor FromRGB(float r, float g, float b, float a = 1)
	{
		return FromGDClass(new Color(r / 255, g / 255, b / 255, a));
	}

	[ScriptMethod]
	public static BVColor FromHex(string hex)
	{
		return FromGDClass(Color.FromString(hex, new(1, 1, 1)));
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static string ToHex(BVColor c)
	{
		return c.color.ToHtml();
	}

	[ScriptMethod]
	public static BVColor FromHSV(float h, float s, float v, float a = 1)
	{
		return FromGDClass(Color.FromHsv(h, s, v, a));
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static BVColor Lerp(BVColor a, BVColor b, float t)
	{
		return FromGDClass(a.color.Lerp(b.color, t));
	}
}

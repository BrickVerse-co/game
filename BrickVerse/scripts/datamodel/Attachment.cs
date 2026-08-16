// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A named local transform used as an endpoint for effects and constraints.</summary>
[Instantiable]
public sealed partial class Attachment : Dynamic
{
	[ScriptProperty] public Vector3 WorldPosition => Position;
	[ScriptProperty] public Vector3 WorldAxis => Right;
	[ScriptProperty] public Vector3 WorldSecondaryAxis => Up;

	public override void Init()
	{
#if CREATOR
		GDNode.AddChild(new global::BrickVerse.Creator.Spatial.SpatialIcon(ClassName), @internal: Node.InternalMode.Back);
#endif
		base.Init();
	}
}

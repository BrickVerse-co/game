using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A Part used as subtractive input when building a UnionOperation.</summary>
[Instantiable]
public sealed partial class NegateOperation : Part
{
	public override void Init()
	{
		base.Init();
		IsNegated = true;
		Color = new Color(1f, 0.25f, 0.25f, 0.55f);
		Name = "NegateOperation";
	}
}

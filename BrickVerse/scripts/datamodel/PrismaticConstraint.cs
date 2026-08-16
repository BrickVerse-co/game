using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class PrismaticConstraint : Constraint
{
	private float _lowerLimit, _upperLimit = 5;
	[Editable, ScriptProperty] public float LowerLimit { get => _lowerLimit; set { _lowerLimit = value; Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(5f)] public float UpperLimit { get => _upperLimit; set { _upperLimit = value; Rebuild(); OnPropertyChanged(); } }
	protected override Joint3D CreateJoint() { SliderJoint3D joint = new(); joint.Set("linear_limit/lower_distance", _lowerLimit); joint.Set("linear_limit/upper_distance", _upperLimit); return joint; }
}

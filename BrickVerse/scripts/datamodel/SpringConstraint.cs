using BrickVerse.Attributes;
using Godot;
namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class SpringConstraint : Constraint
{
	private float _freeLength = 2, _minLength, _maxLength = 10, _stiffness = 100, _damping = 5;
	[Editable, ScriptProperty, DefaultValue(2f)] public float FreeLength { get => _freeLength; set { _freeLength = Mathf.Max(0, value); Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float MinLength { get => _minLength; set { _minLength = Mathf.Max(0, value); Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(10f)] public float MaxLength { get => _maxLength; set { _maxLength = Mathf.Max(0, value); Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(100f)] public float Stiffness { get => _stiffness; set { _stiffness = Mathf.Max(0, value); Rebuild(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(5f)] public float Damping { get => _damping; set { _damping = Mathf.Max(0, value); Rebuild(); OnPropertyChanged(); } }
	protected override Joint3D CreateJoint()
	{
		Generic6DofJoint3D joint = new();
		foreach (string axis in new[] { "x", "y", "z" }) { joint.Set($"linear_limit_{axis}/enabled", true); joint.Set($"linear_limit_{axis}/lower_distance", -_maxLength); joint.Set($"linear_limit_{axis}/upper_distance", _maxLength); joint.Set($"linear_spring_{axis}/enabled", true); joint.Set($"linear_spring_{axis}/stiffness", _stiffness); joint.Set($"linear_spring_{axis}/damping", _damping); joint.Set($"linear_spring_{axis}/equilibrium_point", _freeLength); }
		return joint;
	}
}

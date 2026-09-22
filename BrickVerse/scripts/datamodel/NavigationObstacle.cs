// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A dynamic obstacle that navigation agents avoid and navigation meshes can carve around.</summary>
[Instantiable]
public sealed partial class NavigationObstacle : Dynamic
{
	private NavigationObstacle3D _obstacle = null!;
	private float _radius = 1, _height = 2;
	private bool _avoidanceEnabled = true, _affectNavigationMesh = true, _carveNavigationMesh;
	private Vector3 _velocity;

	[Editable, ScriptProperty, DefaultValue(1f)] public float Radius { get => _radius; set { _radius = Mathf.Max(0.01f, value); Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(2f)] public float Height { get => _height; set { _height = Mathf.Max(0.01f, value); Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool AvoidanceEnabled { get => _avoidanceEnabled; set { _avoidanceEnabled = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool AffectNavigationMesh { get => _affectNavigationMesh; set { _affectNavigationMesh = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(false)] public bool CarveNavigationMesh { get => _carveNavigationMesh; set { _carveNavigationMesh = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Vector3 Velocity { get => _velocity; set { _velocity = value; Apply(); OnPropertyChanged(); } }

	public override Node CreateGDNode()
	{
		_obstacle = new NavigationObstacle3D();
		return _obstacle;
	}

	public override void Init()
	{
		base.Init();
		Apply();
	}

	private void Apply()
	{
		if (_obstacle == null) return;
		_obstacle.Radius = _radius;
		_obstacle.Height = _height;
		_obstacle.AvoidanceEnabled = _avoidanceEnabled;
		_obstacle.AffectNavigationMesh = _affectNavigationMesh;
		_obstacle.CarveNavigationMesh = _carveNavigationMesh;
		_obstacle.Velocity = _velocity;
	}
}

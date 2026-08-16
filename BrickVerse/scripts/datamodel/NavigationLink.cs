using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>Connects otherwise disconnected navigation surfaces, such as jumps or ladders.</summary>
[Instantiable]
public sealed partial class NavigationLink : Instance
{
	private NavigationLink3D _link = null!;
	private Vector3 _start;
	private Vector3 _end;
	private bool _enabled = true;
	private bool _bidirectional = true;
	private float _costMultiplier = 1f;

	[Editable, ScriptProperty] public Vector3 Start { get => _start; set { _start = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Vector3 End { get => _end; set { _end = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Bidirectional { get => _bidirectional; set { _bidirectional = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float CostMultiplier { get => _costMultiplier; set { _costMultiplier = Mathf.Max(0, value); Apply(); OnPropertyChanged(); } }

	public override Node CreateGDNode() => new NavigationLink3D();
	public override void InitGDNode() { _link = (NavigationLink3D)GDNode; base.InitGDNode(); }
	public override void Init() { base.Init(); Apply(); }

	private void Apply()
	{
		if (_link == null) return;
		_link.StartPosition = _start;
		_link.EndPosition = _end;
		_link.Enabled = _enabled;
		_link.Bidirectional = _bidirectional;
		_link.TravelCost = _costMultiplier;
	}
}

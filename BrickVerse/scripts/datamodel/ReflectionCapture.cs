// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A localized reflection probe for reflective materials and interiors.</summary>
[Instantiable]
public sealed partial class ReflectionCapture : Dynamic
{
	private ReflectionProbe _probe = null!;
	private float _intensity = 1;
	private float _maxDistance = 100;
	private bool _boxProjection = true;
	private bool _realtime;
	private bool _enableShadows = true;
	private uint _cullMask = uint.MaxValue;

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Intensity { get => _intensity; set { _intensity = Mathf.Max(0, value); Apply(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(100f)]
	public float MaxDistance { get => _maxDistance; set { _maxDistance = Mathf.Max(0.1f, value); Apply(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool BoxProjection { get => _boxProjection; set { _boxProjection = value; Apply(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool Realtime { get => _realtime; set { _realtime = value; Apply(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool EnableShadows { get => _enableShadows; set { _enableShadows = value; if (_probe != null) _probe.EnableShadows = value; OnPropertyChanged(); } }

	[Editable(CustomPropertyControl = "Bitmap32"), ScriptProperty]
	public uint CullMask { get => _cullMask; set { _cullMask = value; if (_probe != null) _probe.CullMask = value; OnPropertyChanged(); } }

	public override void Init()
	{
		base.Init();
		_probe = new ReflectionProbe();
		GDNode.AddChild(_probe, false, Node.InternalMode.Back);
		Apply();
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		if (_probe != null) _probe.Size = new Vector3(Mathf.Max(0.01f, newSize.X), Mathf.Max(0.01f, newSize.Y), Mathf.Max(0.01f, newSize.Z));
		base.OnNodeSizeChanged(newSize);
	}

	private void Apply()
	{
		if (_probe == null) return;
		_probe.Intensity = _intensity;
		_probe.MaxDistance = _maxDistance;
		_probe.BoxProjection = _boxProjection;
		_probe.EnableShadows = _enableShadows;
		_probe.CullMask = _cullMask;
		_probe.UpdateMode = _realtime ? ReflectionProbe.UpdateModeEnum.Always : ReflectionProbe.UpdateModeEnum.Once;
		_probe.Size = NodeSize;
	}
}

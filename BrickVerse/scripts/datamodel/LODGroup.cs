// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Networking;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>Switches visual representations based on distance from the active camera.</summary>
[Instantiable]
public sealed partial class LODGroup : Dynamic
{
	private Dynamic? _highDetail, _mediumDetail, _lowDetail;
	private float _mediumDistance = 60, _lowDistance = 140, _cullDistance = 300;
	private int _currentLevel = -2;

	[Editable, ScriptProperty, SyncVar] public Dynamic? HighDetail { get => Valid(_highDetail); set { _highDetail = value == this ? null : value; Refresh(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, SyncVar] public Dynamic? MediumDetail { get => Valid(_mediumDetail); set { _mediumDetail = value == this ? null : value; Refresh(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, SyncVar] public Dynamic? LowDetail { get => Valid(_lowDetail); set { _lowDetail = value == this ? null : value; Refresh(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(60f)] public float MediumDistance { get => _mediumDistance; set { _mediumDistance = Mathf.Max(0, value); Refresh(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(140f)] public float LowDistance { get => _lowDistance; set { _lowDistance = Mathf.Max(_mediumDistance, value); Refresh(); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(300f)] public float CullDistance { get => _cullDistance; set { _cullDistance = Mathf.Max(_lowDistance, value); Refresh(); OnPropertyChanged(); } }
	[ScriptProperty] public int CurrentLevel => _currentLevel;
	[ScriptProperty] public BVSignal<int> LevelChanged { get; private set; } = new();

	public override void Init()
	{
		base.Init();
		SetProcess(true);
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		Refresh();
	}

	public override void PreDelete()
	{
		SetVisible(_highDetail, true);
		SetVisible(_mediumDetail, true);
		SetVisible(_lowDetail, true);
		base.PreDelete();
	}

	[ScriptMethod]
	public void Refresh()
	{
		Camera? camera = Root?.Environment?.CurrentCamera;
		if (camera == null) return;
		float distance = Position.DistanceTo(camera.Position);
		int level = distance >= _cullDistance ? -1 : distance >= _lowDistance ? 2 : distance >= _mediumDistance ? 1 : 0;
		SetVisible(_highDetail, level == 0);
		SetVisible(_mediumDetail, level == 1);
		SetVisible(_lowDetail, level == 2);
		if (_currentLevel == level) return;
		_currentLevel = level;
		OnPropertyChanged(nameof(CurrentLevel));
		LevelChanged.Invoke(level);
	}

	private static Dynamic? Valid(Dynamic? target) => target?.IsDeleted == true ? null : target;
	private static void SetVisible(Dynamic? target, bool visible)
	{
		if (target?.IsDeleted != false || target.GDNode3D == null) return;
		target.GDNode3D.Visible = visible && !target.IsHidden;
	}
}

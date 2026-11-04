// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>Deterministic area scatter for foliage, rocks, props, and other templates.</summary>
[Instantiable]
public sealed partial class PCGScatter : Dynamic
{
	private Vector2 _areaSize = new(24, 24);
	private int _count = 50;
	private int _seed = 1;
	private float _minimumScale = 0.8f;
	private float _maximumScale = 1.2f;
	private float _randomYaw = 180f;
	private float _heightJitter;
	private bool _projectToSurface = true;
	private bool _enabled = true;
	private Node3D _generated = null!;
	private MeshInstance3D _preview = null!;
	private readonly HashSet<Dynamic> _templates = [];
	private readonly List<Placement> _placements = [];
	private bool _rebuildQueued;

	private readonly record struct Placement(Dynamic Template, Transform3D Transform);

	[Editable, ScriptProperty, DefaultValue("24,24")]
	public Vector2 AreaSize
	{
		get => _areaSize;
		set { _areaSize = value.Max(Vector2.One * 0.1f); Rebuild(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(50)]
	public int Count
	{
		get => _count;
		set { _count = Math.Clamp(value, 0, 10000); Rebuild(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(1)]
	public int Seed
	{
		get => _seed;
		set { _seed = value; Rebuild(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(0.8f)]
	public float MinimumScale
	{
		get => _minimumScale;
		set { _minimumScale = Mathf.Clamp(value, 0.01f, 100f); NormalizeScale(); Rebuild(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(1.2f)]
	public float MaximumScale
	{
		get => _maximumScale;
		set { _maximumScale = Mathf.Clamp(value, 0.01f, 100f); NormalizeScale(); Rebuild(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(180f)]
	public float RandomYaw
	{
		get => _randomYaw;
		set { _randomYaw = Mathf.Clamp(value, 0, 180); Rebuild(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty]
	public float HeightJitter
	{
		get => _heightJitter;
		set { _heightJitter = Mathf.Clamp(value, 0, 1000); Rebuild(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool ProjectToSurface
	{
		get => _projectToSurface;
		set { _projectToSurface = value; Rebuild(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Enabled
	{
		get => _enabled;
		set { _enabled = value; Rebuild(); OnPropertyChanged(); }
	}

	[ScriptProperty]
	public int GeneratedInstanceCount => _placements.Count;

	public override void Init()
	{
		_generated = new Node3D { Name = "GeneratedInstances" };
		_preview = new MeshInstance3D
		{
			Name = "ScatterArea",
			Mesh = new BoxMesh { Size = new Vector3(_areaSize.X, 0.02f, _areaSize.Y) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.2f, 0.65f, 1f, 0.16f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		GDNode.AddChild(_generated, false, Node.InternalMode.Back);
		GDNode.AddChild(_preview, false, Node.InternalMode.Back);
		ChildAdded.Connect(OnTemplateAdded);
		ChildRemoved.Connect(OnTemplateRemoved);
		Rebuild();
		base.Init();
	}

	public override void PreDelete()
	{
		ChildAdded.Disconnect(OnTemplateAdded);
		ChildRemoved.Disconnect(OnTemplateRemoved);
		foreach (Dynamic template in _templates)
			DisconnectTemplate(template);
		base.PreDelete();
	}

	[ScriptMethod]
	public void Regenerate() => Rebuild();

	[ScriptMethod]
	public Instance[] Bake()
	{
		if (Parent == null) return [];
		List<Instance> baked = [];
		foreach (Placement placement in _placements)
		{
			if (placement.Template.Clone(Parent) is not Dynamic clone) continue;
			clone.Name = placement.Template.Name + "_Scatter";
			clone.SetGlobalTransform(GDNode3D.GlobalTransform * placement.Transform * placement.Template.GDNode3D.Transform);
#if CREATOR
			clone.CreatorInserted();
#endif
			baked.Add(clone);
		}
		return [.. baked];
	}

	private void Rebuild()
	{
		if (_generated == null || _preview == null) return;
		if (_preview.Mesh is BoxMesh box)
			box.Size = new Vector3(_areaSize.X, 0.02f, _areaSize.Y);
		RefreshTemplates();
		ClearGenerated();
		_placements.Clear();
		if (!_enabled || _templates.Count == 0) return;
		Dynamic[] templates = [.. _templates.Where(item => GodotObject.IsInstanceValid(item.GDNode3D))];
		RandomNumberGenerator random = new() { Seed = unchecked((ulong)(uint)_seed) };
		for (int index = 0; index < _count && templates.Length > 0; index++)
		{
			Dynamic template = templates[random.RandiRange(0, templates.Length - 1)];
			Vector3 local = new(
				random.RandfRange(-_areaSize.X * 0.5f, _areaSize.X * 0.5f),
				random.RandfRange(-_heightJitter, _heightJitter),
				random.RandfRange(-_areaSize.Y * 0.5f, _areaSize.Y * 0.5f)
			);
			if (_projectToSurface)
				local = Project(local);
			float scale = random.RandfRange(_minimumScale, _maximumScale);
			Basis basis = Basis.Identity
				.Rotated(Vector3.Up, Mathf.DegToRad(random.RandfRange(-_randomYaw, _randomYaw)))
				.Scaled(Vector3.One * scale);
			Transform3D transform = new(basis, local);
			_placements.Add(new(template, transform));
			Node3D holder = new() { Name = $"Placement{index}", Transform = transform };
			Node duplicate = template.GDNode3D.Duplicate();
			if (duplicate is Node3D duplicate3D) duplicate3D.Visible = true;
			holder.AddChild(duplicate);
			_generated.AddChild(holder);
		}
	}

	private Vector3 Project(Vector3 local)
	{
		Vector3 world = GDNode3D.GlobalTransform * local;
		Datamodel.Environment.RayResult? hit = Root.Environment.Raycast(
			world + Vector3.Up * 1000,
			Vector3.Down,
			2000,
			[.. _templates]
		);
		return hit.HasValue ? GDNode3D.GlobalTransform.AffineInverse() * hit.Value.Position : local;
	}

	private void RefreshTemplates()
	{
		HashSet<Dynamic> current = [.. GetChildren().OfType<Dynamic>()];
		foreach (Dynamic old in _templates.Except(current)) DisconnectTemplate(old);
		foreach (Dynamic added in current.Except(_templates)) ConnectTemplate(added);
		_templates.Clear();
		foreach (Dynamic item in current) _templates.Add(item);
	}

	private void ConnectTemplate(Dynamic template)
	{
		template.GDNode3D.Visible = false;
		template.PropertyChanged.Connect(OnTemplateChanged);
		template.TransformChanged += QueueRebuild;
	}

	private void DisconnectTemplate(Dynamic template)
	{
		template.PropertyChanged.Disconnect(OnTemplateChanged);
		template.TransformChanged -= QueueRebuild;
		if (GodotObject.IsInstanceValid(template.GDNode3D)) template.GDNode3D.Visible = true;
	}

	private void OnTemplateAdded(Instance child) { if (child is Dynamic) QueueRebuild(); }
	private void OnTemplateRemoved(Instance child)
	{
		if (child is Dynamic dynamic && _templates.Remove(dynamic)) DisconnectTemplate(dynamic);
		QueueRebuild();
	}
	private void OnTemplateChanged(string property) => QueueRebuild();

	private void QueueRebuild()
	{
		if (_rebuildQueued) return;
		_rebuildQueued = true;
		Callable.From(() => { _rebuildQueued = false; Rebuild(); }).CallDeferred();
	}

	private void ClearGenerated()
	{
		foreach (Node child in _generated.GetChildren()) child.QueueFree();
	}

	private void NormalizeScale()
	{
		if (_minimumScale > _maximumScale)
			(_minimumScale, _maximumScale) = (_maximumScale, _minimumScale);
	}
}

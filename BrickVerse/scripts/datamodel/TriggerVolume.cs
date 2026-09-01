// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;
using System.Collections.Generic;

namespace BrickVerse.Datamodel;

/// <summary>A placeable collision volume that tracks physical instances entering and exiting it.</summary>
[Instantiable]
public partial class TriggerVolume : Dynamic
{
	private Area3D _area = null!;
	private BoxShape3D _shape = null!;
	private readonly Dictionary<Physical, int> _contacts = [];
	private bool _enabled = true;
	private int _collisionMask = int.MaxValue;

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Enabled
	{
		get => _enabled;
		set { if (_enabled == value) return; _enabled = value; ApplySettings(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(int.MaxValue)]
	public int CollisionMask
	{
		get => _collisionMask;
		set { if (_collisionMask == value) return; _collisionMask = value; ApplySettings(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(true)] public bool DetectPlayers { get; set; } = true;
	[Editable, ScriptProperty, DefaultValue(true)] public bool DetectNPCs { get; set; } = true;
	[Editable, ScriptProperty, DefaultValue(true)] public bool DetectEntities { get; set; } = true;
	[ScriptProperty] public BVSignal<Physical> Entered { get; private set; } = new();
	[ScriptProperty] public BVSignal<Physical> Exited { get; private set; } = new();
	[ScriptProperty] public int OccupantCount => GetOccupants().Length;

	public override Node CreateGDNode() => new Area3D();

	public override void Init()
	{
		base.Init();
		_area = (Area3D)GDNode3D;
		_shape = new BoxShape3D { Size = Size };
		CollisionShape3D collision = new() { Shape = _shape };
		_area.AddChild(collision, @internal: Node.InternalMode.Back);
		_area.AreaEntered += OnAreaEntered;
		_area.AreaExited += OnAreaExited;
		_area.BodyEntered += OnBodyEntered;
		_area.BodyExited += OnBodyExited;
		ApplySettings();
	}

	public override void PreDelete()
	{
		if (GodotObject.IsInstanceValid(_area))
		{
			_area.AreaEntered -= OnAreaEntered;
			_area.AreaExited -= OnAreaExited;
			_area.BodyEntered -= OnBodyEntered;
			_area.BodyExited -= OnBodyExited;
		}
		_contacts.Clear();
		base.PreDelete();
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		if (_shape != null) _shape.Size = new Vector3(Mathf.Max(0.001f, newSize.X), Mathf.Max(0.001f, newSize.Y), Mathf.Max(0.001f, newSize.Z));
		base.OnNodeSizeChanged(newSize);
	}

	[ScriptMethod]
	public Physical[] GetOccupants()
	{
		foreach (Physical physical in new List<Physical>(_contacts.Keys))
			if (!Accepts(physical)) _contacts.Remove(physical);
		return [.. _contacts.Keys];
	}
	[ScriptMethod] public bool Contains(Physical physical) => physical != null && Accepts(physical) && _contacts.ContainsKey(physical);

	protected virtual bool Accepts(Physical physical)
	{
		if (physical.IsDeleted) return false;
		if (physical is Player) return DetectPlayers;
		if (physical is NPC) return DetectNPCs;
		return DetectEntities;
	}

	private void OnAreaEntered(Area3D area) => AddContact(Physical.GetPhysicalFromCollider(area));
	private void OnAreaExited(Area3D area) => RemoveContact(Physical.GetPhysicalFromCollider(area));
	private void OnBodyEntered(Node3D body) => AddContact(body is CollisionObject3D collider ? Physical.GetPhysicalFromBodyShape(collider) : null);
	private void OnBodyExited(Node3D body) => RemoveContact(body is CollisionObject3D collider ? Physical.GetPhysicalFromBodyShape(collider) : null);

	private void AddContact(Physical? physical)
	{
		if (!_enabled || physical == null || !Accepts(physical)) return;
		if (_contacts.TryGetValue(physical, out int count)) { _contacts[physical] = count + 1; return; }
		_contacts[physical] = 1;
		Entered.Invoke(physical);
	}

	private void RemoveContact(Physical? physical)
	{
		if (physical == null || !_contacts.TryGetValue(physical, out int count)) return;
		if (count > 1) { _contacts[physical] = count - 1; return; }
		_contacts.Remove(physical);
		Exited.Invoke(physical);
	}

	private void ApplySettings()
	{
		if (_area == null) return;
		_area.Monitoring = _enabled;
		_area.Monitorable = _enabled;
		_area.CollisionLayer = 0;
		_area.CollisionMask = unchecked((uint)_collisionMask);
		if (!_enabled) _contacts.Clear();
	}
}

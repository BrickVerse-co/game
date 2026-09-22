// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Networking;
using BrickVerse.Scripting;
using Godot;
using System;
using System.Linq;

namespace BrickVerse.Datamodel;

/// <summary>A replaceable player rig. Children can be Meshes, Parts, animations and named Attachments.</summary>
[Instantiable]
public sealed partial class Pawn : CharacterModel
{
	private Player.PlayerMovementModeEnum _controlMode = Player.PlayerMovementModeEnum.Default;
	private Dynamic? _cameraAttachment;
	private Dynamic? _toolAttachment;
	private CollisionShape3D? _collisionShape;
	private Physical? _collisionOwner;
	private Vector3 _collisionSize = new(2f, 5.8f, 1f);
	private Vector3 _collisionOffset = new(0, -0.1f, 0);
	private PawnCollisionShapeEnum _collisionShapeType = PawnCollisionShapeEnum.Box;

	[ScriptEnum]
	public enum PawnCollisionShapeEnum { None, Box, Capsule }

	[Editable, ScriptProperty, SyncVar]
	public PawnCollisionShapeEnum CollisionShapeType
	{
		get => _collisionShapeType;
		set { _collisionShapeType = value; RefreshCollision(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, SyncVar]
	public Vector3 CollisionSize
	{
		get => _collisionSize;
		set { _collisionSize = value; RefreshCollision(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, SyncVar]
	public Vector3 CollisionOffset
	{
		get => _collisionOffset;
		set { _collisionOffset = value; RefreshCollision(); OnPropertyChanged(); }
	}

	[ScriptProperty]
	public Player? Controller => Parent is Player player && player.Character == this ? player : null;

	[ScriptProperty]
	public bool IsLocallyControlled => Controller?.IsLocal == true;

	[ScriptProperty]
	public Vector3 Velocity => Controller?.Velocity ?? Vector3.Zero;

	[ScriptProperty]
	public float Health => Controller?.Health ?? 0;

	[ScriptProperty]
	public bool IsOnGround => Controller?.IsOnGround == true;

	/// <summary>X is steering/right, Y is forward throttle; zero when gameplay input is unavailable.</summary>
	[ScriptProperty]
	public Vector2 InputVector => !CanSampleInput ? Vector2.Zero : new Vector2(
		Input.GetActionStrength("rightward") - Input.GetActionStrength("leftward"),
		Input.GetActionStrength("forward") - Input.GetActionStrength("backward")).LimitLength(1);

	[ScriptProperty]
	public bool JumpPressed => CanSampleInput && Input.IsActionPressed("jump");

	[ScriptProperty]
	public bool SprintPressed => CanSampleInput && Input.IsActionPressed("sprint");

	private bool CanSampleInput => Controller is Player player && player.IsLocal && player.CanMove && !player.IsDead && Root.Input.IsGameFocused;

	[ScriptProperty]
	public BVSignal<Player> Possessed { get; private set; } = new();

	[ScriptProperty]
	public BVSignal<Player> Unpossessed { get; private set; } = new();

	/// <summary>Local physics tick for scripted movement and vehicle controllers.</summary>
	[ScriptProperty]
	public BVSignal<double> ControlTick { get; private set; } = new();

	[Editable, ScriptProperty, SyncVar]
	public Player.PlayerMovementModeEnum ControlMode
	{
		get => _controlMode;
		set
		{
			_controlMode = value;
			if (Controller is Player player) player.MovementMode = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Dynamic? CameraAttachment
	{
		get => _cameraAttachment?.IsDeleted == true ? null : _cameraAttachment;
		set { _cameraAttachment = value; OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, SyncVar]
	public Dynamic? ToolAttachment
	{
		get => _toolAttachment?.IsDeleted == true ? null : _toolAttachment;
		set { _toolAttachment = value; OnPropertyChanged(); }
	}

	[ScriptMethod]
	public override Dynamic GetAttachment(CharacterAttachmentEnum attachmentEnum)
	{
		if (attachmentEnum == CharacterAttachmentEnum.Head && CameraAttachment != null) return CameraAttachment;
		if (attachmentEnum == CharacterAttachmentEnum.HandRight && ToolAttachment != null) return ToolAttachment;
		return GetDescendants().OfType<Dynamic>().FirstOrDefault(child => child.Name == attachmentEnum.ToString()) ?? this;
	}

	[ScriptMethod]
	public void Possess(Player player) => player.Possess(this);

	[ScriptMethod]
	public void Unpossess() => Controller?.Unpossess();

	[ScriptMethod]
	public void Move(Vector3 velocity) => Controller?.Move(velocity);

	[ScriptMethod]
	public void Jump() => Controller?.Jump();

	internal void InvokePossessed(Player player) => Possessed.Invoke(player);
	internal void InvokeUnpossessed(Player player) => Unpossessed.Invoke(player);

	public override void Init()
	{
		base.Init();
		SetPhysicsProcess(true);
	}

	public override void PhysicsProcess(double delta)
	{
		base.PhysicsProcess(delta);
		if (IsLocallyControlled && ControlMode == Player.PlayerMovementModeEnum.Scripted)
			ControlTick.Invoke(delta);
	}

	public override void EnterTree()
	{
		base.EnterTree();
		RefreshCollision();
	}

	public override void ExitTree()
	{
		RemoveCollision();
		base.ExitTree();
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		base.OnNodeSizeChanged(newSize);
		RefreshCollision();
	}

	private void RefreshCollision()
	{
		if (GDNode == null) return;
		RemoveCollision();
		if (Parent is not Physical owner || CollisionShapeType == PawnCollisionShapeEnum.None) return;
		Vector3 scaled = new(Mathf.Max(0.05f, Mathf.Abs(_collisionSize.X * NodeSize.X)),
			Mathf.Max(0.05f, Mathf.Abs(_collisionSize.Y * NodeSize.Y)),
			Mathf.Max(0.05f, Mathf.Abs(_collisionSize.Z * NodeSize.Z)));
		Shape3D shape = CollisionShapeType == PawnCollisionShapeEnum.Capsule
			? new CapsuleShape3D { Radius = Mathf.Max(scaled.X, scaled.Z) * 0.5f, Height = Mathf.Max(scaled.Y, Mathf.Max(scaled.X, scaled.Z)) }
			: new BoxShape3D { Size = scaled };
		_collisionShape = new CollisionShape3D { Shape = shape, Position = _collisionOffset * NodeSize };
		_collisionOwner = owner;
		owner.GDNode.AddChild(_collisionShape);
		owner.AddCollisionShape(_collisionShape);
		owner.UpdateCollision();
	}

	private void RemoveCollision()
	{
		if (_collisionShape == null) return;
		_collisionOwner?.RemoveCollisionShape(_collisionShape);
		if (GodotObject.IsInstanceValid(_collisionShape)) _collisionShape.QueueFree();
		_collisionShape = null;
		_collisionOwner = null;
	}

}

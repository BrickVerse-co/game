// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Datamodel.Services;

/// <summary>Optional client-side world streaming controls. Disabled by default while the feature is in beta.</summary>
[Static("Streaming")]
public sealed partial class StreamingService : Instance
{
	private readonly HashSet<Dynamic> _streamHidden = [];
	private bool _enabled;
	private float _minimumDistance = 128f;
	private float _maximumDistance = 512f;
	private bool _smartStreaming = true;
	private bool _occlusionCulling = true;
	private double _updateElapsed;

	/// <summary>Fires locally when a streamed object becomes visible to this player.</summary>
	[ScriptProperty]
	public BVSignal<Dynamic> InstanceStreamedIn { get; private set; } = new();

	/// <summary>Fires locally when a streamed object leaves this player's streaming radius.</summary>
	[ScriptProperty]
	public BVSignal<Dynamic> InstanceStreamedOut { get; private set; } = new();

	[Editable, ScriptProperty, SyncVar]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			if (_enabled == value)
				return;
			_enabled = value;
			OnPropertyChanged();
			ApplyViewportSettings();
			if (!value)
				RestoreHiddenInstances();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float MinimumDistance
	{
		get => _minimumDistance;
		set
		{
			float sanitized = Mathf.Max(0, value);
			if (Mathf.IsEqualApprox(_minimumDistance, sanitized))
				return;
			_minimumDistance = sanitized;
			if (_maximumDistance < sanitized)
				_maximumDistance = sanitized;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float MaximumDistance
	{
		get => _maximumDistance;
		set
		{
			float sanitized = Mathf.Max(_minimumDistance, value);
			if (Mathf.IsEqualApprox(_maximumDistance, sanitized))
				return;
			_maximumDistance = sanitized;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public bool SmartStreaming
	{
		get => _smartStreaming;
		set
		{
			if (_smartStreaming == value)
				return;
			_smartStreaming = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public bool OcclusionCulling
	{
		get => _occlusionCulling;
		set
		{
			if (_occlusionCulling == value)
				return;
			_occlusionCulling = value;
			OnPropertyChanged();
			ApplyViewportSettings();
		}
	}

	public override void Init()
	{
		base.Init();
		SetProcess(true);
		ApplyViewportSettings();
	}

	public override void PreDelete()
	{
		RestoreHiddenInstances();
		base.PreDelete();
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		if (!_enabled || Root.Network.IsServer || Root.Entry == null)
			return;
		_updateElapsed += delta;
		if (_updateElapsed < 0.25)
			return;
		_updateElapsed = 0;
		UpdateStreaming();
	}

	private void ApplyViewportSettings()
	{
		if (Root?.RootViewport != null)
			Root.RootViewport.UseOcclusionCulling = _enabled && _occlusionCulling;
	}

	private void UpdateStreaming()
	{
		Camera3D? camera = Root.Environment.CurrentGDCamera;
		if (camera == null || !GodotObject.IsInstanceValid(camera))
			return;

		float effectiveMaximum = _maximumDistance;
		if (_smartStreaming)
		{
			float frameBudget = Mathf.Clamp((float)Engine.GetFramesPerSecond() / 60f, 0f, 1f);
			effectiveMaximum = Mathf.Lerp(_minimumDistance, _maximumDistance, frameBudget);
		}

		HashSet<Dynamic> evaluated = [];
		foreach (Dynamic dynamic in Root.Objects.Values.OfType<Dynamic>().ToArray())
		{
			Dynamic target = ResolveStreamingTarget(dynamic, out bool persistent);
			if (!evaluated.Add(target) || !Node.IsInstanceValid(target.GDNode3D))
				continue;

			if (persistent || IsCharacterPart(target))
			{
				StreamIn(target);
				continue;
			}

			float maximum =
				target.StreamingDistance > 0 ? target.StreamingDistance : effectiveMaximum;
			float minimum =
				target.StreamingDistance > 0
					? Mathf.Min(_minimumDistance, target.StreamingDistance * 0.75f)
					: _minimumDistance;
			float distance = target.GDNode3D.GlobalPosition.DistanceTo(camera.GlobalPosition);

			// Hysteresis keeps objects from flickering at the boundary: visible
			// objects leave at MaximumDistance and return at MinimumDistance.
			if (_streamHidden.Contains(target))
			{
				if (distance <= minimum)
					StreamIn(target);
			}
			else if (distance > maximum)
			{
				StreamOut(target);
			}
		}

		foreach (
			Dynamic stale in _streamHidden
				.Where(item => item.IsDeleted || !Node.IsInstanceValid(item.GDNode3D))
				.ToArray()
		)
			_streamHidden.Remove(stale);
	}

	private static Dynamic ResolveStreamingTarget(Dynamic dynamic, out bool persistent)
	{
		persistent = false;
		Dynamic target = dynamic;
		for (Instance? current = dynamic; current != null; current = current.Parent)
		{
			if (current is not Dynamic ancestor)
				continue;
			if (ancestor.StreamingMode == Dynamic.StreamingModeEnum.Persistent)
			{
				persistent = true;
				return ancestor;
			}
			if (ancestor.StreamingMode == Dynamic.StreamingModeEnum.Nonatomic)
				break;
			if (ancestor.StreamingMode == Dynamic.StreamingModeEnum.Atomic)
				target = ancestor;
		}
		return target;
	}

	internal bool ShouldReplicateToPlayer(Player player, NetworkedObject obj, bool isResident)
	{
		if (!_enabled || obj is Player || IsOwnedByPlayerCharacter(player, obj))
			return true;

		Dynamic? dynamic = obj as Dynamic;
		for (
			NetworkedObject? current = obj.NetworkParent;
			dynamic == null && current != null;
			current = current.NetworkParent
		)
			dynamic = current as Dynamic;
		if (dynamic == null || !Node.IsInstanceValid(dynamic.GDNode3D))
			return true;

		Dynamic target = ResolveStreamingTarget(dynamic, out bool persistent);
		if (persistent || IsCharacterPart(target) || !Node.IsInstanceValid(target.GDNode3D))
			return true;

		float maximum = target.StreamingDistance > 0 ? target.StreamingDistance : _maximumDistance;
		float minimum =
			target.StreamingDistance > 0
				? Mathf.Min(_minimumDistance, target.StreamingDistance * 0.75f)
				: _minimumDistance;
		float threshold = isResident ? maximum : minimum;
		return target.GetGlobalPosition().DistanceTo(player.GetGlobalPosition()) <= threshold;
	}

	private static bool IsOwnedByPlayerCharacter(Player player, NetworkedObject obj)
	{
		for (NetworkedObject? current = obj; current != null; current = current.NetworkParent)
			if (current == player || current == player.Character)
				return true;
		return false;
	}

	private void StreamOut(Dynamic dynamic)
	{
		if (dynamic.IsHidden || !_streamHidden.Add(dynamic))
			return;
		dynamic.GDNode3D.Visible = false;
		SetGroupStreamingState(dynamic, false);
	}

	private void StreamIn(Dynamic dynamic)
	{
		if (!_streamHidden.Remove(dynamic))
			return;
		if (Node.IsInstanceValid(dynamic.GDNode3D))
			dynamic.GDNode3D.Visible = !dynamic.IsHidden;
		SetGroupStreamingState(dynamic, true);
	}

	private void SetGroupStreamingState(Dynamic root, bool streamedIn)
	{
		SetStreamingState(root, streamedIn);
		if (root.StreamingMode != Dynamic.StreamingModeEnum.Atomic)
			return;
		foreach (Dynamic descendant in root.GetDescendants().OfType<Dynamic>())
			SetStreamingState(descendant, streamedIn);
	}

	private void SetStreamingState(Dynamic dynamic, bool streamedIn)
	{
		if (dynamic.IsStreamedIn == streamedIn)
			return;
		dynamic.SetStreamedInState(streamedIn);
		if (streamedIn)
			InstanceStreamedIn.Invoke(dynamic);
		else
			InstanceStreamedOut.Invoke(dynamic);
	}

	private static bool IsCharacterPart(Instance instance)
	{
		for (Instance? current = instance; current != null; current = current.Parent)
			if (current is Player or NPC or BrickversianModel)
				return true;
		return false;
	}

	private void RestoreHiddenInstances()
	{
		foreach (Dynamic dynamic in _streamHidden.ToArray())
		{
			if (Node.IsInstanceValid(dynamic.GDNode3D))
				dynamic.GDNode3D.Visible = !dynamic.IsHidden;
			SetGroupStreamingState(dynamic, true);
		}
		_streamHidden.Clear();
		if (Root?.RootViewport != null)
			Root.RootViewport.UseOcclusionCulling = false;
	}
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel.Services;

/// <summary>Optional client-side world streaming controls. Disabled by default while the feature is in beta.</summary>
[Static("Streaming")]
public sealed partial class StreamingService : Instance
{
	private readonly HashSet<Physical> _streamHidden = [];
	private bool _enabled;
	private float _minimumDistance = 128f;
	private float _maximumDistance = 512f;
	private bool _smartStreaming = true;
	private bool _occlusionCulling = true;
	private double _updateElapsed;

	[Editable, ScriptProperty, SyncVar]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			if (_enabled == value) return;
			_enabled = value;
			OnPropertyChanged();
			ApplyViewportSettings();
			if (!value) RestoreHiddenInstances();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public float MinimumDistance
	{
		get => _minimumDistance;
		set
		{
			float sanitized = Mathf.Max(0, value);
			if (Mathf.IsEqualApprox(_minimumDistance, sanitized)) return;
			_minimumDistance = sanitized;
			if (_maximumDistance < sanitized) _maximumDistance = sanitized;
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
			if (Mathf.IsEqualApprox(_maximumDistance, sanitized)) return;
			_maximumDistance = sanitized;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public bool SmartStreaming
	{
		get => _smartStreaming;
		set { if (_smartStreaming == value) return; _smartStreaming = value; OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, SyncVar]
	public bool OcclusionCulling
	{
		get => _occlusionCulling;
		set { if (_occlusionCulling == value) return; _occlusionCulling = value; OnPropertyChanged(); ApplyViewportSettings(); }
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
		if (!_enabled || Root.Network.IsServer || Root.Entry == null) return;
		_updateElapsed += delta;
		if (_updateElapsed < 0.25) return;
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
		if (camera == null || !GodotObject.IsInstanceValid(camera)) return;

		float effectiveMaximum = _maximumDistance;
		if (_smartStreaming)
		{
			float frameBudget = Mathf.Clamp((float)Engine.GetFramesPerSecond() / 60f, 0f, 1f);
			effectiveMaximum = Mathf.Lerp(_minimumDistance, _maximumDistance, frameBudget);
		}

		foreach (Physical physical in Root.Objects.Values.OfType<Physical>().ToArray())
		{
			if (!Node.IsInstanceValid(physical.GDNode3D) || IsCharacterPart(physical)) continue;
			bool shouldStreamOut = physical.GDNode3D.GlobalPosition.DistanceTo(camera.GlobalPosition) > effectiveMaximum;
			if (shouldStreamOut)
			{
				if (!physical.IsHidden && _streamHidden.Add(physical)) physical.GDNode3D.Visible = false;
			}
			else if (_streamHidden.Remove(physical))
			{
				physical.GDNode3D.Visible = !physical.IsHidden;
			}
		}

		foreach (Physical stale in _streamHidden.Where(item => !Node.IsInstanceValid(item.GDNode3D)).ToArray())
			_streamHidden.Remove(stale);
	}

	private static bool IsCharacterPart(Instance instance)
	{
		for (Instance? current = instance; current != null; current = current.Parent)
			if (current is Player or NPC or BrickversianModel) return true;
		return false;
	}

	private void RestoreHiddenInstances()
	{
		foreach (Physical physical in _streamHidden.ToArray())
			if (Node.IsInstanceValid(physical.GDNode3D)) physical.GDNode3D.Visible = !physical.IsHidden;
		_streamHidden.Clear();
		if (Root?.RootViewport != null) Root.RootViewport.UseOcclusionCulling = false;
	}
}

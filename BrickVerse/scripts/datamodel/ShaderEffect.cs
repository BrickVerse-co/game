// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel;

/// <summary>An engine-approved shader effect applied by parenting it to a Part or Model.</summary>
[Instantiable]
public sealed partial class ShaderEffect : Instance
{
	internal static readonly ShaderMaterial SharedMaterial = new()
	{
		Shader = GD.Load<Godot.Shader>("res://resources/shaders/ugc_visual_effects.gdshader")
	};

	private readonly HashSet<Part> _targets = [];
	private EffectEnum _effect = EffectEnum.Hologram;
	private Color _effectColor = new(0.15f, 0.8f, 1.0f);
	private Color _secondaryColor = new(0.7f, 0.2f, 1.0f);
	private float _strength = 0.75f;
	private float _speed = 1.0f;
	private float _scale = 4.0f;
	private float _progress;
	private bool _enabled = true;
	private double _reconcileTimer;

	[Editable, ScriptProperty, DefaultValue(EffectEnum.Hologram)]
	public EffectEnum Effect { get => _effect; set { _effect = value; RefreshTargets(); OnPropertyChanged(); } }

	[Editable, ScriptProperty]
	public Color EffectColor { get => _effectColor; set { _effectColor = value; RefreshTargets(); OnPropertyChanged(); } }

	[Editable, ScriptProperty]
	public Color SecondaryColor { get => _secondaryColor; set { _secondaryColor = value; RefreshTargets(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(0.75f)]
	public float Strength { get => _strength; set { _strength = Mathf.Clamp(value, 0, 1); RefreshTargets(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(1.0f)]
	public float Speed { get => _speed; set { _speed = Mathf.Clamp(value, 0, 10); RefreshTargets(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(4.0f)]
	public float Scale { get => _scale; set { _scale = Mathf.Clamp(value, 0.1f, 100); RefreshTargets(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(0.0f)]
	public float Progress { get => _progress; set { _progress = Mathf.Clamp(value, 0, 1); RefreshTargets(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Enabled { get => _enabled; set { _enabled = value; ReconcileTargets(); OnPropertyChanged(); } }

	public override void Init()
	{
		SetProcess(true);
		base.Init();
	}

	public override void Ready()
	{
		ReconcileTargets();
		base.Ready();
	}

	public override void Process(double delta)
	{
		_reconcileTimer -= delta;
		if (_reconcileTimer <= 0)
		{
			_reconcileTimer = 0.5;
			ReconcileTargets();
		}
		base.Process(delta);
	}

	public override void PreDelete()
	{
		foreach (Part target in _targets) target.RefreshShaderEffect(this);
		_targets.Clear();
		base.PreDelete();
	}

	private void ReconcileTargets()
	{
		HashSet<Part> desired = _enabled ? ResolveTargets().ToHashSet() : [];
		foreach (Part removed in _targets.Except(desired).ToArray())
		{
			removed.RefreshShaderEffect();
			_targets.Remove(removed);
		}
		foreach (Part added in desired.Except(_targets))
		{
			added.RefreshShaderEffect();
			_targets.Add(added);
		}
		RefreshTargets();
	}

	private IEnumerable<Part> ResolveTargets()
	{
		if (Parent is Part part) return [part];
		if (Parent is Model model) return model.GetDescendants().OfType<Part>();
		return [];
	}

	private void RefreshTargets()
	{
		foreach (Part target in _targets) target.RefreshShaderEffect();
	}

	[ScriptEnum("ShaderEffectType")]
	public enum EffectEnum
	{
		Hologram = 1,
		ForceField,
		Toon,
		NeonPulse,
		Dissolve,
		RimGlow,
		Glitch,
	}
}

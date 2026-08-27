// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Linq;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Data;
using BrickVerse.Datamodel.Creator;
using Godot;
using System;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class ParticleEditorWindow : Window
{
	private Particles _particles = null!;

	public static void Open()
	{
		Particles? particles = World.Current?.CreatorContext.Selections.GetSelected().OfType<Particles>().FirstOrDefault();
		if (particles == null) { OS.Alert("Select a Particles object first, or insert one from Insert → Effects.", "Particle Editor"); return; }
		ParticleEditorWindow window = GD.Load<PackedScene>("res://scenes/creator/popups/particle_editor.tscn").Instantiate<ParticleEditorWindow>();
		window._particles = particles;
		CreatorService.Interface.GetTree().Root.AddChild(window);
		window.PopupCentered();
	}

	public override void _Ready()
	{
		Title = $"Particle Editor — {_particles.Name}";
		CloseRequested += QueueFree;
		GetNode<Button>("Surface/Margin/Layout/Controls/Play").Pressed += _particles.Play;
		GetNode<Button>("Surface/Margin/Layout/Controls/Pause").Pressed += _particles.Pause;
		GetNode<Button>("Surface/Margin/Layout/Controls/Stop").Pressed += _particles.Stop;
		SpinBox burstCount = GetNode<SpinBox>("Surface/Margin/Layout/Controls/BurstCount");
		GetNode<Button>("Surface/Margin/Layout/Controls/Burst").Pressed += () => _particles.Emit((int)burstCount.Value);
		GetNode<Button>("Surface/Margin/Layout/Controls/Clear").Pressed += _particles.Clear;
		GetNode<Button>("Surface/Margin/Layout/Controls/Restart").Pressed += () => { _particles.Stop(); _particles.Clear(); _particles.Play(); };
		Bind("Amount", _particles.Amount, value => _particles.Amount = (int)value);
		Bind("Speed", _particles.SpeedScale, value => _particles.SpeedScale = (float)value);
		Bind("Spread", _particles.Spread, value => _particles.Spread = (float)value);
		Bind("Explosiveness", _particles.Explosiveness, value => _particles.Explosiveness = (float)value);
		Bind("Flatness", _particles.Flatness, value => _particles.Flatness = (float)value);
		BindRange("Lifetime", _particles.Lifetime, value => _particles.Lifetime = value);
		BindRange("Velocity", _particles.InitialVelocity, value => _particles.InitialVelocity = value);
		BindRange("Scale", _particles.Scale, value => _particles.Scale = value);
		BindRange("AngularVelocity", _particles.AngularVelocity, value => _particles.AngularVelocity = value);
		BindRange("LinearAcceleration", _particles.LinearAcceleration, value => _particles.LinearAcceleration = value);
		BindRange("Damping", _particles.Damping, value => _particles.Damping = value);
		BindVector("Gravity", _particles.Gravity, value => _particles.Gravity = value);
		BindVector("Direction", _particles.VelocityDirection, value => _particles.VelocityDirection = value);
		BindEnum("EmissionShape", _particles.EmissionShape, value => _particles.EmissionShape = value);
		BindEnum("BlendMode", _particles.BlendMode, value => _particles.BlendMode = value);
		BindEnum("SimulationSpace", _particles.SimulationSpace, value => _particles.SimulationSpace = value);
		CheckButton turbulence = GetNode<CheckButton>("Surface/Margin/Layout/Fields/Turbulence");
		turbulence.SetPressedNoSignal(_particles.TurbulenceEnabled);
		turbulence.Toggled += value => _particles.TurbulenceEnabled = value;
	}

	private void BindVector(string name, Vector3 initial, Action<Vector3> changed)
	{
		SpinBox x = GetNode<SpinBox>($"Surface/Margin/Layout/Fields/{name}X");
		SpinBox y = GetNode<SpinBox>($"Surface/Margin/Layout/Fields/{name}Y");
		SpinBox z = GetNode<SpinBox>($"Surface/Margin/Layout/Fields/{name}Z");
		x.SetValueNoSignal(initial.X); y.SetValueNoSignal(initial.Y); z.SetValueNoSignal(initial.Z);
		void Update(double _) => changed(new Vector3((float)x.Value, (float)y.Value, (float)z.Value));
		x.ValueChanged += Update; y.ValueChanged += Update; z.ValueChanged += Update;
	}

	private void BindEnum<T>(string name, T initial, Action<T> changed) where T : struct, Enum
	{
		OptionButton field = GetNode<OptionButton>($"Surface/Margin/Layout/Fields/{name}");
		T[] values = Enum.GetValues<T>();
		for (int i = 0; i < values.Length; i++) field.AddItem(values[i].ToString(), i);
		int selected = Array.IndexOf(values, initial);
		field.Select(Math.Max(0, selected));
		field.ItemSelected += index => changed(values[(int)index]);
	}

	private void Bind(string name, double initial, System.Action<double> changed)
	{
		SpinBox field = GetNode<SpinBox>($"Surface/Margin/Layout/Fields/{name}");
		field.SetValueNoSignal(initial);
		field.ValueChanged += value => changed(value);
	}

	private void BindRange(string name, NumberRange initial, System.Action<NumberRange> changed)
	{
		SpinBox min = GetNode<SpinBox>($"Surface/Margin/Layout/Fields/{name}Min");
		SpinBox max = GetNode<SpinBox>($"Surface/Margin/Layout/Fields/{name}Max");
		min.SetValueNoSignal(initial.Min); max.SetValueNoSignal(initial.Max);
		void Update(double _) => changed(new NumberRange { Min = (float)min.Value, Max = (float)max.Value });
		min.ValueChanged += Update; max.ValueChanged += Update;
	}
}

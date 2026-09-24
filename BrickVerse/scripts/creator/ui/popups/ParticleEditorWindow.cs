// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Linq;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Data;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>A document-style, isolated particle preview and inspector.</summary>
public sealed partial class ParticleEditorWindow : Control
{
	public Particles Target { get; }
	private Node3D _previewRoot = null!;
	private Camera3D _camera = null!;
	private GpuParticles3D? _emitter;
	private Label _status = null!;
	private bool _playing = true;
	private float _yaw = -35, _pitch = -18, _distance = 8;
	private double _elapsed;

	public ParticleEditorWindow(Particles target) => Target = target;

	public static void Open()
	{
		Particles? target = World.Current?.CreatorContext.Selections.GetSelected().OfType<Particles>().FirstOrDefault();
		if (target == null) { OS.Alert("Select a Particles object first, or insert one from Insert → Effects.", "Particle Editor"); return; }
		Tabs.Singleton.Insert(new Tabs.ParticleEditorTab { Target = target, Title = $"{target.Name} — Particles" });
	}

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		BuildInterface();
		RebuildPreview();
		Target.Deleted += OnTargetDeleted;
	}

	public override void _ExitTree() { Target.Deleted -= OnTargetDeleted; base._ExitTree(); }

	public override void _Process(double delta)
	{
		if (!_playing || _emitter == null) return;
		_elapsed += delta * _emitter.SpeedScale;
		_status.Text = $"Previewing  •  {Target.Amount:N0} particles  •  {_elapsed:0.0}s  •  drag to orbit, wheel to zoom";
	}

	private void BuildInterface()
	{
		VBoxContainer page = new() { Name = "ParticleEditor" };
		page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(page);
		HBoxContainer toolbar = new() { CustomMinimumSize = new Vector2(0, 48) }; page.AddChild(toolbar);
		AddButton(toolbar, "▶ Play", Play); AddButton(toolbar, "Ⅱ Pause", Pause); AddButton(toolbar, "■ Stop", Stop); AddButton(toolbar, "↻ Restart", Restart);
		SpinBox burst = NewSpin(1, 10000, 1, 25); burst.CustomMinimumSize = new Vector2(86, 36); toolbar.AddChild(burst);
		AddButton(toolbar, "✦ Burst", () => Burst((int)burst.Value)); AddButton(toolbar, "Frame", ResetCamera);
		toolbar.AddChild(new Label { Text = $"  {Target.Name}", VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill });
		HSplitContainer split = new() { SizeFlagsVertical = SizeFlags.ExpandFill }; page.AddChild(split);
		BuildPreview(split); BuildInspector(split);
		_status = new Label { Text = "Preview ready", CustomMinimumSize = new Vector2(0, 26) }; page.AddChild(_status);
	}

	private void BuildPreview(HSplitContainer split)
	{
		SubViewportContainer host = new() { Stretch = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(420, 320) };
		host.GuiInput += PreviewInput; split.AddChild(host);
		SubViewport viewport = new() { HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, World3D = new World3D() }; host.AddChild(viewport);
		_previewRoot = new Node3D(); viewport.AddChild(_previewRoot);
		Godot.Environment env = new() { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("151820"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .55f };
		_previewRoot.AddChild(new WorldEnvironment { Environment = env });
		_previewRoot.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, -35, 0), LightEnergy = 1.4f, ShadowEnabled = true });
		_previewRoot.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(35, 145, 0), LightEnergy = .55f });
		_camera = new Camera3D { Current = true, Fov = 55 }; _previewRoot.AddChild(_camera); ResetCamera();
	}

	private void BuildInspector(HSplitContainer split)
	{
		ScrollContainer scroll = new() { CustomMinimumSize = new Vector2(360, 0) }; split.AddChild(scroll);
		VBoxContainer fields = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(fields);
		Section(fields, "EMISSION");
		BindNumber(fields, "Amount", Target.Amount, 1, 100000, 1, v => Target.Amount = (int)v);
		BindRange(fields, "Lifetime", Target.Lifetime, .01, 10000, .05, v => Target.Lifetime = v);
		BindNumber(fields, "Explosiveness", Target.Explosiveness, 0, 1, .01, v => Target.Explosiveness = (float)v);
		BindEnum(fields, "Shape", Target.EmissionShape, v => Target.EmissionShape = v); BindEnum(fields, "Space", Target.SimulationSpace, v => Target.SimulationSpace = v);
		Section(fields, "MOTION");
		BindRange(fields, "Initial velocity", Target.InitialVelocity, -10000, 10000, .05, v => Target.InitialVelocity = v);
		BindVector(fields, "Direction", Target.VelocityDirection, v => Target.VelocityDirection = v); BindVector(fields, "Gravity", Target.Gravity, v => Target.Gravity = v);
		BindRange(fields, "Angular velocity", Target.AngularVelocity, -10000, 10000, .1, v => Target.AngularVelocity = v);
		BindRange(fields, "Acceleration", Target.LinearAcceleration, -10000, 10000, .05, v => Target.LinearAcceleration = v); BindRange(fields, "Damping", Target.Damping, 0, 10000, .05, v => Target.Damping = v);
		Section(fields, "APPEARANCE & PLAYBACK");
		BindRange(fields, "Scale", Target.Scale, 0, 10000, .05, v => Target.Scale = v); BindNumber(fields, "Speed", Target.SpeedScale, 0, 10, .05, v => Target.SpeedScale = (float)v);
		BindNumber(fields, "Spread", Target.Spread, 0, 180, .5, v => Target.Spread = (float)v); BindNumber(fields, "Flatness", Target.Flatness, 0, 1, .01, v => Target.Flatness = (float)v);
		BindEnum(fields, "Blend mode", Target.BlendMode, v => Target.BlendMode = v);
		CheckButton turbulence = new() { Text = "Turbulence", ButtonPressed = Target.TurbulenceEnabled }; turbulence.Toggled += value => { Target.TurbulenceEnabled = value; RebuildPreview(); }; fields.AddChild(turbulence);
	}

	private void PreviewInput(InputEvent input)
	{
		if (input is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0) { _yaw -= motion.Relative.X * .35f; _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * .35f, -80, 80); UpdateCamera(); }
		else if (input is InputEventMouseButton wheel && wheel.Pressed) { if (wheel.ButtonIndex == MouseButton.WheelUp) _distance = Mathf.Max(1.5f, _distance * .88f); else if (wheel.ButtonIndex == MouseButton.WheelDown) _distance = Mathf.Min(80, _distance * 1.14f); UpdateCamera(); }
	}

	private void ResetCamera() { _yaw = -35; _pitch = -18; _distance = 8; if (_camera != null) UpdateCamera(); }
	private void UpdateCamera() { Quaternion rotation = Quaternion.FromEuler(new Vector3(Mathf.DegToRad(_pitch), Mathf.DegToRad(_yaw), 0)); _camera.Position = rotation * new Vector3(0, 0, _distance); _camera.LookAt(Vector3.Zero, Vector3.Up); }
	private void RebuildPreview() { _emitter?.QueueFree(); _emitter = Target.CreateEditorPreviewEmitter(); _previewRoot.AddChild(_emitter); _emitter.Emitting = _playing; }
	private void Play() { _playing = true; if (_emitter != null) { _emitter.SpeedScale = Math.Max(.001f, Target.SpeedScale); _emitter.Emitting = true; } }
	private void Pause() { _playing = false; if (_emitter != null) _emitter.SpeedScale = 0; _status.Text = "Preview paused"; }
	private void Stop() { _playing = false; _elapsed = 0; if (_emitter != null) { _emitter.Emitting = false; _emitter.Restart(); } _status.Text = "Preview stopped"; }
	private void Restart() { _elapsed = 0; _playing = true; RebuildPreview(); if (_emitter != null) _emitter.Restart(); }
	private void Burst(int count)
	{
		if (_emitter == null) return;
		GpuParticles3D burst = (GpuParticles3D)_emitter.Duplicate();
		_previewRoot.AddChild(burst);
		burst.Amount = count; burst.Explosiveness = 1; burst.OneShot = true; burst.Emitting = true;
		void Finished() { burst.Finished -= Finished; burst.QueueFree(); }
		burst.Finished += Finished;
	}
	private void OnTargetDeleted() { _playing = false; _status.Text = "The particle emitter was deleted. Close this tab."; }

	private static void AddButton(Container p, string text, Action action) { Button b = new() { Text = text, CustomMinimumSize = new Vector2(78, 36) }; b.Pressed += action; p.AddChild(b); }
	private static void Section(Container p, string text) { Label l = new() { Text = text, CustomMinimumSize = new Vector2(0, 34), VerticalAlignment = VerticalAlignment.Bottom }; l.AddThemeColorOverride("font_color", new Color("75bfff")); p.AddChild(l); }
	private static SpinBox NewSpin(double min, double max, double step, double value) => new() { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(92, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill };
	private static void Row(Container p, string label, params Control[] controls) { HBoxContainer row = new(); row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(122, 0), VerticalAlignment = VerticalAlignment.Center }); foreach (Control c in controls) row.AddChild(c); p.AddChild(row); }
	private void BindNumber(Container p, string label, double initial, double min, double max, double step, Action<double> changed) { SpinBox f = NewSpin(min, max, step, initial); f.ValueChanged += v => { changed(v); RebuildPreview(); }; Row(p, label, f); }
	private void BindRange(Container p, string label, NumberRange initial, double min, double max, double step, Action<NumberRange> changed) { SpinBox a = NewSpin(min, max, step, initial.Min), b = NewSpin(min, max, step, initial.Max); void Update(double _) { changed(new NumberRange { Min = (float)a.Value, Max = (float)b.Value }); RebuildPreview(); } a.ValueChanged += Update; b.ValueChanged += Update; Row(p, label, a, b); }
	private void BindVector(Container p, string label, Vector3 initial, Action<Vector3> changed) { SpinBox x = NewSpin(-10000, 10000, .05, initial.X), y = NewSpin(-10000, 10000, .05, initial.Y), z = NewSpin(-10000, 10000, .05, initial.Z); void Update(double _) { changed(new Vector3((float)x.Value, (float)y.Value, (float)z.Value)); RebuildPreview(); } x.ValueChanged += Update; y.ValueChanged += Update; z.ValueChanged += Update; Row(p, label, x, y, z); }
	private void BindEnum<T>(Container p, string label, T initial, Action<T> changed) where T : struct, Enum { OptionButton f = new() { CustomMinimumSize = new Vector2(160, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill }; T[] values = Enum.GetValues<T>(); for (int i = 0; i < values.Length; i++) f.AddItem(values[i].ToString(), i); f.Select(Math.Max(0, Array.IndexOf(values, initial))); f.ItemSelected += i => { changed(values[(int)i]); RebuildPreview(); }; Row(p, label, f); }
}

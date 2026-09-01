// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A reusable, pauseable DataModel timer which can repeat or fire once.</summary>
[Instantiable]
public sealed partial class Countdown : Instance
{
	private double _duration = 1;
	private double _timeLeft;
	private bool _running;
	private bool _paused;
	private ulong _lastTickUsec;

	[Editable, ScriptProperty, DefaultValue(1d)]
	public double Duration
	{
		get => _duration;
		set { double next = double.IsFinite(value) ? Mathf.Max(0.001, value) : 1; if (Mathf.IsEqualApprox(_duration, next)) return; _duration = next; OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(true)] public bool OneShot { get; set; } = true;
	[Editable, ScriptProperty, DefaultValue(false)] public bool AutoStart { get; set; }
	[Editable, ScriptProperty, DefaultValue(false)] public bool IgnoreTimeScale { get; set; }
	[ScriptProperty, SaveIgnore, CloneIgnore] public double TimeLeft => _timeLeft;
	[ScriptProperty, SaveIgnore, CloneIgnore] public bool IsRunning => _running;
	[ScriptProperty, SaveIgnore, CloneIgnore] public bool IsPaused => _paused;
	[ScriptProperty] public BVSignal Timeout { get; private set; } = new();
	[ScriptProperty] public BVSignal Started { get; private set; } = new();
	[ScriptProperty] public BVSignal Stopped { get; private set; } = new();

	public override void Init()
	{
		base.Init();
		SetProcess(true);
		_lastTickUsec = Time.GetTicksUsec();
		if (AutoStart) Start();
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		ulong tick = Time.GetTicksUsec();
		double unscaledDelta = (tick - _lastTickUsec) / 1_000_000d;
		_lastTickUsec = tick;
		if (!_running || _paused) return;
		double elapsed = IgnoreTimeScale ? unscaledDelta : delta;
		_timeLeft -= elapsed;
		if (_timeLeft > 0) return;

		Timeout.Invoke();
		if (OneShot) Stop();
		else _timeLeft += _duration;
	}

	[ScriptMethod]
	public void Start(double duration = -1)
	{
		if (duration >= 0) Duration = duration;
		_timeLeft = _duration;
		_lastTickUsec = Time.GetTicksUsec();
		_running = true;
		_paused = false;
		Started.Invoke();
	}

	[ScriptMethod]
	public void Stop()
	{
		if (!_running && _timeLeft == 0) return;
		_running = false;
		_paused = false;
		_timeLeft = 0;
		Stopped.Invoke();
	}

	[ScriptMethod] public void Pause() { if (_running) _paused = true; }
	[ScriptMethod] public void Resume() { if (_running) _paused = false; }
}

using Godot;
using System;
using System.Collections.Generic;

namespace BrickVerse.Shared;

public sealed partial class BootSplash : Control
{
	[Export] private Control _content = null!;
	[Export] private VBoxContainer _brickVerseStage = null!;
	[Export] private VBoxContainer _metaGamesStage = null!;
	[Export] private VBoxContainer _godotStage = null!;
	[Export] private VBoxContainer _creditsStage = null!;
	[Export] private Label _copyright = null!;
	[Export] private Control _glowOne = null!;
	[Export] private Control _glowTwo = null!;
	[Export] private Control _ambientRingOuter = null!;
	[Export] private Control _ambientRingInner = null!;
	[Export] private ColorRect _blackOverlay = null!;
	[Export] private AudioStreamPlayer _introAudio = null!;

	private Tween? _activeTween;
	private Vector2 _glowOneOrigin;
	private Vector2 _glowTwoOrigin;
	private Vector2 _outerRingOrigin;
	private Vector2 _innerRingOrigin;
	private double _ambientElapsed;
	private bool _finished;
	private ulong _startedAt;

	public override void _Ready()
	{
		SetProcess(false);
		SetProcessUnhandledInput(true);
		_startedAt = Time.GetTicksMsec();
		_copyright.Text = $"© {DateTime.UtcNow.Year} Meta Games, LLC. All rights reserved.";

		if (ShouldBypassSplash())
		{
			Callable.From(FinishImmediately).CallDeferred();
			return;
		}

		_ = BeginSplashAsync();
	}

	private async System.Threading.Tasks.Task BeginSplashAsync()
	{
		// Anchor-relative controls settle over the first layout frames. Capturing
		// before then causes their animation origin to jump when the window resolves.
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (_finished) return;

		_glowOne.PivotOffset = _glowOne.Size / 2f;
		_glowTwo.PivotOffset = _glowTwo.Size / 2f;
		_ambientRingOuter.PivotOffset = _ambientRingOuter.Size / 2f;
		_ambientRingInner.PivotOffset = _ambientRingInner.Size / 2f;
		_glowOneOrigin = _glowOne.Position;
		_glowTwoOrigin = _glowTwo.Position;
		_outerRingOrigin = _ambientRingOuter.Position;
		_innerRingOrigin = _ambientRingInner.Position;

		SetProcess(true);
		_introAudio.Play();
		await PlaySequenceAsync();
	}

	public override void _Process(double delta)
	{
		_ambientElapsed += delta;
		float time = (float)_ambientElapsed;

		_glowOne.Position = _glowOneOrigin + new Vector2(Mathf.Sin(time * 0.72f) * 190f, Mathf.Sin(time * 0.49f) * 115f);
		_glowOne.RotationDegrees = Mathf.Sin(time * 0.58f) * 8f;
		_glowOne.Scale = Vector2.One * (1f + Mathf.Sin(time * 0.67f) * 0.08f);
		_glowOne.Modulate = new Color(1f, 1f, 1f, 0.73f + Mathf.Sin(time * 0.83f) * 0.09f);

		_glowTwo.Position = _glowTwoOrigin + new Vector2(Mathf.Sin(time * 0.61f) * -210f, Mathf.Sin(time * 0.79f) * 130f);
		_glowTwo.RotationDegrees = Mathf.Sin(time * 0.53f) * -10f;
		_glowTwo.Scale = Vector2.One * (1f + Mathf.Sin(time * 0.59f) * 0.1f);
		_glowTwo.Modulate = new Color(1f, 1f, 1f, 0.72f + Mathf.Sin(time * 0.71f) * 0.17f);

		_ambientRingOuter.Position = _outerRingOrigin + new Vector2(Mathf.Sin(time * 0.31f) * 28f, Mathf.Sin(time * 0.43f) * 18f);
		_ambientRingOuter.RotationDegrees = time * 2.2f;
		_ambientRingOuter.Scale = Vector2.One * (1f + Mathf.Sin(time * 0.46f) * 0.055f);
		_ambientRingOuter.Modulate = new Color(1f, 1f, 1f, 0.65f + Mathf.Sin(time * 0.66f) * 0.2f);

		_ambientRingInner.Position = _innerRingOrigin + new Vector2(Mathf.Sin(time * 0.47f) * -22f, Mathf.Sin(time * 0.36f) * 25f);
		_ambientRingInner.RotationDegrees = time * -3.1f;
		_ambientRingInner.Scale = Vector2.One * (1f + Mathf.Sin(time * 0.57f) * -0.07f);
		_ambientRingInner.Modulate = new Color(1f, 1f, 1f, 0.55f + Mathf.Sin(time * 0.74f) * 0.16f);
	}

	private static bool ShouldBypassSplash()
	{
		Dictionary<string, string> args = Globals.ReadCmdArgs();
		return Globals.IsServerBuild
			|| DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase)
			|| OS.HasFeature("renderer")
			|| args.ContainsKey("renderer")
			|| args.ContainsKey("genapi")
			|| args.ContainsKey("dmtest");
	}

	private async System.Threading.Tasks.Task PlaySequenceAsync()
	{
		await RevealStage(_brickVerseStage, 0.85f, 1f);
		if (_finished) return;
		await HideStage(_brickVerseStage, 0.45f);
		if (_finished) return;
		await RevealStage(_metaGamesStage, 0.7f, 0.8f);
		if (_finished) return;
		await HideStage(_metaGamesStage, 0.4f);
		if (_finished) return;
		await RevealStage(_godotStage, 0.7f, 0.85f);
		if (_finished) return;
		await HideStage(_godotStage, 0.4f);
		if (_finished) return;
		await RevealStage(_creditsStage, 0.65f, 1f);
		if (_finished) return;

		Finish();
	}

	private async System.Threading.Tasks.Task RevealStage(Control stage, float duration, float hold)
	{
		if (_finished) return;
		stage.Visible = true;
		stage.Modulate = new Color(1, 1, 1, 0);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (_finished) return;
		stage.Scale = new Vector2(0.9f, 0.9f);
		stage.PivotOffset = stage.Size / 2f;

		_activeTween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quart);
		_activeTween.SetParallel();
		_activeTween.TweenProperty(stage, "modulate:a", 1f, duration);
		_activeTween.TweenProperty(stage, "scale", Vector2.One, duration);
		await ToSignal(_activeTween, Tween.SignalName.Finished);
		if (!_finished) await ToSignal(GetTree().CreateTimer(hold), SceneTreeTimer.SignalName.Timeout);
	}

	private async System.Threading.Tasks.Task HideStage(Control stage, float duration)
	{
		if (_finished) return;
		_activeTween = CreateTween().SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
		_activeTween.SetParallel();
		_activeTween.TweenProperty(stage, "modulate:a", 0f, duration);
		_activeTween.TweenProperty(stage, "scale", new Vector2(1.035f, 1.035f), duration);
		await ToSignal(_activeTween, Tween.SignalName.Finished);
		if (_finished) return;
		stage.Visible = false;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_finished || Time.GetTicksMsec() - _startedAt < 750) return;
		if (@event is InputEventKey { Pressed: true, Echo: false }
			|| @event is InputEventMouseButton { Pressed: true }
			|| @event is InputEventJoypadButton { Pressed: true })
		{
			Finish();
			GetViewport().SetInputAsHandled();
		}
	}

	private void Finish()
	{
		if (_finished) return;
		_finished = true;
		_ = FinishTransitionAsync();
	}

	private async System.Threading.Tasks.Task FinishTransitionAsync()
	{
		_activeTween?.Kill();
		SetProcess(false);
		SetProcessUnhandledInput(false);

		_activeTween = CreateTween().SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
		_activeTween.SetParallel();
		_activeTween.TweenProperty(_blackOverlay, "modulate:a", 1f, 0.55f);
		_activeTween.TweenProperty(_content, "modulate:a", 0f, 0.45f);
		_activeTween.TweenProperty(_introAudio, "volume_db", -40f, 0.55f);
		await ToSignal(_activeTween, Tween.SignalName.Finished);
		_introAudio.Stop();

		AppEntry entry = new() { Name = "AppEntry" };
		GetNode("/root").AddChild(entry);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree().CreateTimer(0.18f), SceneTreeTimer.SignalName.Timeout);

		_activeTween = CreateTween().SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
		_activeTween.TweenProperty(_blackOverlay, "modulate:a", 0f, 0.5f);
		await ToSignal(_activeTween, Tween.SignalName.Finished);
		QueueFree();
	}

	private void FinishImmediately()
	{
		if (_finished) return;
		_finished = true;
		AppEntry entry = new() { Name = "AppEntry" };
		GetNode("/root").AddChild(entry);
		QueueFree();
	}
}

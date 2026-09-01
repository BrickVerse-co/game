// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Formats;
using BrickVerse.Scripting;
using Godot;
using System;

namespace BrickVerse.Datamodel;

/// <summary>A persistent, editable skeletal animation document stored in the DataModel.</summary>
[Instantiable]
public sealed partial class AnimationTrack : Instance
{
	private string _animationData = "";
	private float _length = 1f;
	private string _loopMode = "None";
	private Animator? _animator;
	private string _runtimeKey = "";
	private bool _isPlaying;
	private float _timePosition;
	private float _speed = 1f;

	[ScriptProperty] public bool IsPlaying => _isPlaying;
	[ScriptProperty]
	public float TimePosition
	{
		get => _timePosition;
		set => Seek(value);
	}
	[ScriptProperty] public float Speed => _speed;
	[ScriptProperty] public Animator? Animator => _animator;
	[ScriptProperty] public BVSignal Played { get; private set; } = new();
	[ScriptProperty] public BVSignal Stopped { get; private set; } = new();
	[ScriptProperty] public BVSignal Ended { get; private set; } = new();

	[ScriptProperty]
	public string AnimationData
	{
		get => _animationData;
		set
		{
			if (_animationData == (value ?? "")) return;
			_animator?.InvalidateAnimationTrack(this);
			_animationData = value ?? "";
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1.0f)]
	public float Length
	{
		get => _length;
		set { _length = Math.Clamp(value, 0.01f, 3600f); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue("None")]
	public string LoopMode
	{
		get => _loopMode;
		set { _loopMode = value is "Linear" or "Pingpong" ? value : "None"; OnPropertyChanged(); }
	}

	public void SetClip(BVAnimationClip clip)
	{
		BVAnimationFormat.Validate(clip);
		_animator?.InvalidateAnimationTrack(this);
		_animationData = Convert.ToBase64String(BVAnimationFormat.Write(clip));
		_length = clip.Length;
		_loopMode = clip.LoopMode;
		Name = clip.Name;
		OnPropertyChanged(nameof(AnimationData));
		OnPropertyChanged(nameof(Length));
		OnPropertyChanged(nameof(LoopMode));
	}

	public BVAnimationClip? GetClip()
	{
		if (string.IsNullOrWhiteSpace(_animationData)) return null;
		try { return BVAnimationFormat.Read(Convert.FromBase64String(_animationData)); }
		catch (Exception exception) { Godot.GD.PrintErr("Invalid AnimationTrack data: ", exception.Message); return null; }
	}

	internal void Bind(Animator? animator, string runtimeKey)
	{
		_animator = animator;
		_runtimeKey = runtimeKey;
	}

	internal void UpdatePlayback(float position, bool playing)
	{
		_timePosition = Mathf.Clamp(position, 0, Length);
		_isPlaying = playing;
	}

	internal void NotifyPlayed()
	{
		_isPlaying = true;
		_timePosition = 0;
		Played.Invoke();
	}

	internal void NotifyStopped(bool ended)
	{
		bool wasPlaying = _isPlaying;
		_isPlaying = false;
		if (wasPlaying) Stopped.Invoke();
		if (ended) Ended.Invoke();
	}

	[ScriptMethod]
	public void Play() => _animator?.PlayAnimationTrack(this);

	[ScriptMethod]
	public void Stop() => _animator?.StopAnimationTrack(this);

	[ScriptMethod]
	public void Seek(float seconds)
	{
		_timePosition = Mathf.Clamp(seconds, 0, Length);
		_animator?.SeekAnimationTrack(this, _timePosition);
	}

	[ScriptMethod]
	public void AdjustSpeed(float speed)
	{
		_speed = Mathf.Clamp(speed, 0.01f, 8f);
		_animator?.SetAnimationTrackSpeed(this, _speed);
	}

	internal string RuntimeKey => _runtimeKey;

	public override void PreDelete()
	{
		_animator?.UnloadAnimation(this);
		base.PreDelete();
	}
}

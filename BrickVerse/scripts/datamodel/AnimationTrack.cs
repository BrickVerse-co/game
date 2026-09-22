// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Formats;
using BrickVerse.Scripting;
using Godot;
using System;

namespace BrickVerse.Datamodel;

/// <summary>A persistent, editable property or skeletal animation document stored in the DataModel.</summary>
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
	private AnimationPlayer? _objectPlayer;
	private InstanceAnimationBinding? _instanceBinding;

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
			ReleaseObjectPlayer();
			_instanceBinding?.Dispose();
			_instanceBinding = null;
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
		ReleaseObjectPlayer();
		_instanceBinding?.Dispose();
		_instanceBinding = null;
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
		if (animator != null) ReleaseObjectPlayer();
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
	public void Play()
	{
		if (_objectPlayer != null && GodotObject.IsInstanceValid(_objectPlayer)) { _objectPlayer.SpeedScale = _speed; _objectPlayer.Play("sequence/clip", customSpeed: 1); NotifyPlayed(); }
		else _animator?.PlayAnimationTrack(this);
	}

	/// <summary>Play this sequence relative to any 3D instance, without a skeletal Animator.</summary>
	[ScriptMethod]
	public void PlayOn(Dynamic target)
	{
		if (target == null || target.IsDeleted || target.Root != Root) throw new InvalidOperationException("Target must be a live 3D instance in the same World.");
		BVAnimationClip clip = GetClip() ?? throw new InvalidOperationException("AnimationTrack has no valid clip.");
		Stop();
		ReleaseObjectPlayer();
		_objectPlayer = new AnimationPlayer { RootNode = new NodePath("..") };
		target.GDNode3D.AddChild(_objectPlayer);
		AnimationLibrary library = new();
		library.AddAnimation("clip", BVAnimationFormat.ToAnimation(clip));
		_objectPlayer.AddAnimationLibrary("sequence", library);
		_objectPlayer.AnimationFinished += _ => { _timePosition = Length; NotifyStopped(true); };
		SetProcess(true);
		Play();
	}

	/// <summary>Play property tracks on any DataModel instance, including GUI/UIField objects.</summary>
	[ScriptMethod]
	public void PlayOn(Instance target)
	{
		if (target == null || target.IsDeleted || target.Root != Root)
			throw new InvalidOperationException("Target must be a live instance in the same World.");
		BVAnimationClip clip = GetClip() ?? throw new InvalidOperationException("AnimationTrack has no valid clip.");
		Stop();
		ReleaseObjectPlayer();
		_instanceBinding?.Dispose();
		_instanceBinding = new InstanceAnimationBinding(target, clip);
		SetProcess(true);
		NotifyPlayed();
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		if (_instanceBinding != null)
		{
			UpdatePlayback(_timePosition + (float)delta * _speed, true);
			_instanceBinding.Apply(_timePosition);
			if (_timePosition >= Length)
			{
				if (LoopMode == "Linear") Seek(0);
				else { _timePosition = Length; NotifyStopped(true); }
			}
		}
		else if (_objectPlayer != null && GodotObject.IsInstanceValid(_objectPlayer))
			{ if (_objectPlayer.IsPlaying()) UpdatePlayback((float)_objectPlayer.CurrentAnimationPosition, true); }
		else if (_objectPlayer != null) { _objectPlayer = null; NotifyStopped(false); }
	}

	private void ReleaseObjectPlayer()
	{
		if (_objectPlayer != null && GodotObject.IsInstanceValid(_objectPlayer)) { _objectPlayer.Stop(); _objectPlayer.QueueFree(); }
		_objectPlayer = null;
		NotifyStopped(false);
	}

	[ScriptMethod]
	public void Stop()
	{
		if (_instanceBinding != null) { _instanceBinding.Dispose(); _instanceBinding = null; _timePosition = 0; NotifyStopped(false); }
		else if (_objectPlayer != null && GodotObject.IsInstanceValid(_objectPlayer)) { _objectPlayer.Stop(); _timePosition = 0; NotifyStopped(false); }
		else _animator?.StopAnimationTrack(this);
	}

	[ScriptMethod]
	public void Seek(float seconds)
	{
		_timePosition = Mathf.Clamp(seconds, 0, Length);
		if (_instanceBinding != null) { _instanceBinding.Apply(_timePosition); return; }
		if (_objectPlayer != null && GodotObject.IsInstanceValid(_objectPlayer)) _objectPlayer.Seek(_timePosition, true);
		else _animator?.SeekAnimationTrack(this, _timePosition);
	}

	[ScriptMethod]
	public void AdjustSpeed(float speed)
	{
		_speed = Mathf.Clamp(speed, 0.01f, 8f);
		if (_objectPlayer != null && GodotObject.IsInstanceValid(_objectPlayer)) _objectPlayer.SpeedScale = _speed;
		else _animator?.SetAnimationTrackSpeed(this, _speed);
	}

	internal string RuntimeKey => _runtimeKey;

	public override void PreDelete()
	{
		ReleaseObjectPlayer();
		_instanceBinding?.Dispose();
		_instanceBinding = null;
		_animator?.UnloadAnimation(this);
		base.PreDelete();
	}
}

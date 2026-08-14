// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileLoadingScreen : Node
{
	private AnimationPlayer _animPlay = null!;
	private AnimationPlayer _spinner = null!;
	private Label _title = null!;
	private Label _status = null!;
	private Label _elapsed = null!;
	private ulong _shownAt;
	private bool _active;

	public override void _Ready()
	{
		// The layout container owns the status labels and must never inherit the
		// spinner rotation (including stale state from a hot-reloaded scene).
		GetNode<Control>("Control").Rotation = 0.0f;
		_animPlay = GetNode<AnimationPlayer>("AnimPlay");
		_spinner = GetNode<AnimationPlayer>("SpinnerAnim");
		_title = GetNode<Label>("Title");
		_status = GetNode<Label>("Status");
		_elapsed = GetNode<Label>("Elapsed");
		base._Ready();
	}

	public void ShowScreen(string title = "Loading", string status = "Please wait…")
	{
		_title.Text = title;
		_status.Text = status;
		_elapsed.Text = "";
		_shownAt = Time.GetTicksMsec();
		_active = true;
		if (!_spinner.IsPlaying()) _spinner.Play("spin");
		_animPlay.Play("appear");
	}

	public void UpdateStatus(string title, string status)
	{
		_title.Text = title;
		_status.Text = status;
	}

	public void HideScreen()
	{
		_active = false;
		_animPlay.Play("disappear");
	}

	public override void _Process(double delta)
	{
		if (!_active) return;
		ulong seconds = (Time.GetTicksMsec() - _shownAt) / 1000;
		_elapsed.Text = seconds < 2 ? "" : $"Waiting {seconds}s";
	}
}

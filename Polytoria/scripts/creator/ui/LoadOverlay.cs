// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Creator;
using Polytoria.Schemas.Progress;
using System;

namespace Polytoria.Creator.UI;

public partial class LoadOverlay : Control
{
	[Export] private Label _titleLabel = null!;
	[Export] private Label _statusLabel = null!;
	[Export] private ProgressBar _progressBar = null!;

	public LoadOverlay()
	{
		Visible = false;
	}

	public override void _Ready()
	{
		CreatorService.Interface.LoadOverlay = this;
		base._Ready();
	}

	public new void Show()
	{
		if (!IsInsideTree()) return;
		SetProgress(0);
		Visible = true;
	}

	public new void Hide()
	{
		Visible = false;
	}

	public void SetTitle(string text)
	{
		Callable.From(() =>
		{
			_titleLabel.Text = text;
		}).CallDeferred();
	}

	public void SetStatus(string text)
	{
		Callable.From(() =>
		{
			_statusLabel.Text = text;
		}).CallDeferred();
	}

	public void SetProgress(float val)
	{
		Callable.From(() =>
		{
			_progressBar.Value = val;
		}).CallDeferred();
	}

	public void SetMaxProgress(float val)
	{
		Callable.From(() =>
		{
			_progressBar.MaxValue = val;
		}).CallDeferred();
	}
}

public static class LoadOverlayExtensions
{
	public static IProgress<LoadOverlayProgress> CreateProgressReporter(this LoadOverlay? overlay, string title)
	{
		overlay?.SetTitle(title);
		overlay?.Show();
		return new Progress<LoadOverlayProgress>(p =>
		{
			if (overlay == null) return;

			overlay.SetStatus($"{p.Status} ({p.Current}/{p.Total})");
			overlay.SetProgress(p.Current);
			overlay.SetMaxProgress(p.Total);
		});
	}
}

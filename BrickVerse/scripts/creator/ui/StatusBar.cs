// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;

namespace BrickVerse.Creator.UI;

public partial class StatusBar : Control
{
	[Export] private Label _titleLabel = null!;
	[Export] private Label _versionLabel = null!;

	public override void _Ready()
	{
		CreatorService.Interface.StatusBar = this;
		_versionLabel.Text = $"BrickVerse Creator {Globals.AppVersion}";
		base._Ready();
	}

	public void SetStatus(string text)
	{
		_titleLabel.Text = text;
	}

	public void SetEmpty()
	{
		_titleLabel.Text = "";
	}
}

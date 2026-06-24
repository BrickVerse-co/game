// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.UI.Wizards;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using Polytoria.Utils;

namespace Polytoria.Creator.UI.Splashes;

public partial class StartupSplash : Control
{
	private const string BannersLocation = "res://assets/textures/creator/banner/";

	[Export] private Button _newButton = null!;
	[Export] private Button _openButton = null!;
	[Export] private Button _recentsButton = null!;
	[Export] private Button _closeButton = null!;
	[Export] private Label _versionNumber = null!;
	[Export] private TextureRect _banner = null!;

	public static StartupSplash Singleton { get; private set; } = null!;

	public StartupSplash()
	{
		Singleton = this;
	}

	public override void _Ready()
	{
		_newButton.Pressed += OnNew;
		_openButton.Pressed += CreatorService.Interface.PromptOpenWorld;
		_closeButton.Pressed += Close;
		_versionNumber.Text = Globals.AppVersion;

		string randomized = ArrayUtils.GetRandom(ResourceLoader.ListDirectory(BannersLocation));
		string pathTo = BannersLocation.PathJoin(randomized);
		_banner.Texture = GD.Load<Texture2D>(pathTo);
		base._Ready();
	}

	private void OnNew()
	{
		NewProjectWizard.Singleton.ReturnToSplash = true;
		NewProjectWizard.Singleton.Open();
		Close();
	}

	public void Open()
	{
		Visible = true;
	}

	public void Close()
	{
		Visible = false;
	}

}

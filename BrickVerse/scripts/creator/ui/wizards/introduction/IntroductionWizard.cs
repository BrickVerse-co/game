// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.UI.Splashes;

namespace Polytoria.Creator.UI.Wizards;

public partial class IntroductionWizard : Control
{
	private const string BannerFolderPath = "res://assets/textures/creator/introduct/";
	public static IntroductionWizard Singleton { get; private set; } = null!;

	[Export] private IntroPage _firstPage = null!;
	[Export] private TextureRect _bannerImg = null!;
	[Export] private TabContainer _tabs = null!;

	public IntroductionWizard()
	{
		Singleton = this;
	}

	public override void _Ready()
	{
		base._Ready();
	}

	public void ShowImage(string img)
	{
		_bannerImg.Texture = GD.Load<Texture2D>(BannerFolderPath.PathJoin(img));
	}

	public void Open()
	{
		_firstPage.Show();
		Visible = true;
	}

	public void Close()
	{
		Visible = false;
		using FileAccess f = FileAccess.Open(CreatorInterface.IntroRanFile, FileAccess.ModeFlags.Write);
		f.StoreString("1");
		f.Close();
		StartupSplash.Singleton.Open();
	}

	public void Next()
	{
		int nextTab = _tabs.CurrentTab + 1;
		if (nextTab >= _tabs.GetTabCount())
		{
			Close();
		}
		else
		{
			_tabs.CurrentTab = nextTab;
			((IntroPage)_tabs.GetCurrentTabControl()).Show();
		}
	}

	public void Prev()
	{
		int prevTab = _tabs.CurrentTab - 1;
		_tabs.CurrentTab = prevTab;
	}
}

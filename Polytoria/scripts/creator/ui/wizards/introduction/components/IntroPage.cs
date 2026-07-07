// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Creator.UI.Wizards;

public partial class IntroPage : Control
{
	[Export] public string ImageName = "";
	[Export] private Button? _closeBtn = null!;
	[Export] private Button? _prevBtn = null!;
	[Export] private Button? _nextBtn = null!;

	public override void _Ready()
	{
		_closeBtn?.Pressed += IntroductionWizard.Singleton.Close;
		_prevBtn?.Pressed += IntroductionWizard.Singleton.Prev;
		_nextBtn?.Pressed += IntroductionWizard.Singleton.Next;
		base._Ready();
	}

	public new void Show()
	{
		Visible = true;
		IntroductionWizard.Singleton.ShowImage(ImageName);
	}
}

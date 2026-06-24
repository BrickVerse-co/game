// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI;

public partial class UIEmoteItem : Control
{
	private bool _isActive = false;

	[Export] private Control _activeIndicator = null!;
	[Export] private TextureRect _iconRect = null!;
	[Export] private Label _emoteLabel = null!;
	[Export] private AnimationPlayer _animPlay = null!;

	public string EmoteName = "";

	public bool IsActive
	{
		get => _isActive;
		set
		{
			bool old = _isActive;
			_isActive = value;
			_activeIndicator.Visible = _isActive;

			if (old != value)
			{
				if (_isActive)
				{
					_animPlay.Play("hover");
				}
				else
				{
					_animPlay.Pause();
				}
			}
		}
	}

	public override void _Ready()
	{
		_activeIndicator.Visible = false;
		_emoteLabel.Text = EmoteName;
		string iconPath = UIEmoteWheel.EmoteIconPath.PathJoin(EmoteName + ".png");

		if (ResourceLoader.Exists(iconPath))
		{
			_iconRect.Texture = GD.Load<Texture2D>(iconPath);
		}
	}
}

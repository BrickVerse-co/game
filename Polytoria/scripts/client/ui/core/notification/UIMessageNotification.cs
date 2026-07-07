// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI.Notification;

public partial class UIMessageNotification : UINotificationBase
{
	[Export] public AnimationPlayer AnimPlay = null!;
	[Export] public Label TitleLabel = null!;
	[Export] public Label SubTitleLabel = null!;
	[Export] public TextureRect IconRect = null!;

	public override void Fire(object? data)
	{
		if (data is MessageNotifyPayload payload)
		{
			TitleLabel.Text = payload.Title;
			SubTitleLabel.Text = payload.SubTitle;
			IconRect.Texture = payload.Icon;
			AnimPlay.Play("appear");
		}
		else
		{
			QueueFree();
		}
	}

	public struct MessageNotifyPayload()
	{
		public string Title = "";
		public string SubTitle = "";
		public Texture2D? Icon;
	}
}

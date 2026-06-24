// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI.Notification;

public partial class UIScreenshotNotification : UINotificationBase
{
	[Export] public AnimationPlayer AnimPlay = null!;
	[Export] public TextureRect IconRect = null!;
	[Export] public Button ViewButton = null!;

	public override void Fire(object? data)
	{
		if (data is ScreenshotNotifyPayload payload)
		{
			IconRect.Texture = payload.Icon;
			AnimPlay.Play("appear");

			World game = NotificationCenter.CoreUI.Root;

			ViewButton.Pressed += game.Capture.ViewCurrentPhoto;
		}
		else
		{
			QueueFree();
		}
	}

	public struct ScreenshotNotifyPayload()
	{
		public Texture2D? Icon;
	}
}
